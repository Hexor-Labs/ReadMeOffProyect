using HubNegocios.IdentityService.Domain.Entities;
using HubNegocios.IdentityService.Domain.Ports;
using HubNegocios.IdentityService.Domain.ValueObjects;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace HubNegocios.IdentityService.Application.UseCases;

public sealed record RegisterUserCommand(
    string Email,
    string FullName,
    string? Phone,
    string Password,
    UserRole Role,
    bool AcceptsTerms,
    bool AcceptsPrivacyPolicy,
    string? AvatarUrl = null,
    string? ExternalProvider = null,
    string? ExternalId = null);

public sealed record RegisterUserResult(Guid UserId, string Email, string Role);

/// <summary>
/// Da de alta una cuenta dentro del tenant en curso.
/// </summary>
public sealed class RegisterUserHandler(
    IIdentityRepository repository,
    IPasswordHasher passwordHasher,
    IOutboxWriter outbox,
    TimeProvider clock,
    ILogger<RegisterUserHandler> logger)
{
    public async Task<RegisterUserResult> HandleAsync(
        RegisterUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Password);

        // Sin tenant no hay alta posible: una cuenta que no pertenece a ningún
        // negocio no puede entrar a ninguno. Falla aquí y no al guardar.
        var tenantId = TenantContext.Current.RequireTenantId();

        var email = Email.Create(command.Email);

        /*
          La unicidad es POR TENANT, no global, y esta consulta ya lo es: el
          filtro global del DbContext la acota al tenant en curso. Que el mismo
          correo pueda ser cliente de dos negocios distintos no es un detalle
          menor — es el caso normal en un hub de negocios, y una restricción
          global de correo lo haría imposible.

          Comprobar antes de insertar solo sirve para dar un error legible: entre
          esta consulta y el INSERT cabe otra petición con el mismo correo. La
          garantía de verdad es el índice único (tenant_id, email).
        */
        if (await repository.EmailExistsAsync(email.Value, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException(
                "USER_EMAIL_TAKEN",
                "Ya existe una cuenta con ese correo en este negocio.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var userId = Guid.NewGuid();

        var user = User.Register(
            userId,
            tenantId,
            email,
            command.FullName,
            command.Phone,
            // El hash se calcula aquí y la contraseña en claro no sale de este
            // método: no va al dominio, ni al log, ni al evento.
            passwordHasher.Hash(command.Password),
            command.Role,
            now,
            command.AcceptsTerms,
            command.AcceptsPrivacyPolicy,
            command.AvatarUrl,
            command.ExternalProvider,
            command.ExternalId);

        repository.AddUser(user);

        /*
          El evento se encola, no se publica: se convierte en una fila más de la
          misma transacción que el usuario. O se guardan los dos o ninguno, que es
          lo que evita avisar de un alta que después no ocurrió.

          En el cuerpo van datos de identificación y nada de credenciales. Un
          evento lo leen los diez servicios y acaba en logs, colas y volcados; un
          hash de contraseña ahí sería una copia de la credencial en sitios que
          nadie audita.
        */
        outbox.Enqueue(
            userId,
            nameof(User),
            "user.registered",
            new
            {
                userId,
                tenantId,
                email = user.Email,
                fullName = user.FullName,
                role = user.Role.ToString(),
                registeredAt = now,
            });

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Usuario {UserId} registrado en el tenant {TenantId} con rol {Role}",
            userId,
            tenantId,
            user.Role);

        return new RegisterUserResult(userId, user.Email, user.Role.ToString());
    }
}
