using HubNegocios.IdentityService.Domain.Entities;
using HubNegocios.IdentityService.Domain.Ports;
using HubNegocios.IdentityService.Domain.ValueObjects;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace HubNegocios.IdentityService.Application.UseCases;

public sealed record LoginCommand(string Email, string Password, string? IpAddress, string? UserAgent);

public sealed record LoginResult(
    Guid UserId,
    string Role,
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt);

/// <summary>
/// Valida credenciales y abre sesión.
///
/// Este caso de uso tiene una regla que manda sobre la comodidad de quien lo
/// usa: <b>un fallo de acceso nunca dice por qué falló</b>. Correo que no existe,
/// contraseña equivocada y cuenta desactivada devuelven el mismo código y el
/// mismo mensaje. Distinguirlos convierte el formulario de acceso en un
/// comprobador de correos: se prueban diez mil direcciones y las que contestan
/// «contraseña incorrecta» son cuentas confirmadas del negocio, listas para
/// vender o para intentar phishing.
///
/// El único fallo que sí se distingue es el bloqueo, y solo cuando la contraseña
/// era correcta: a esas alturas quien pregunta ya conocía la credencial, así que
/// decirle que espere quince minutos no le revela nada que no supiera, y ahorra a
/// un usuario legítimo la desesperación de teclear bien y que no pase nada.
/// </summary>
public sealed class LoginHandler(
    IIdentityRepository repository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IOutboxWriter outbox,
    TimeProvider clock,
    ILogger<LoginHandler> logger)
{
    public async Task<LoginResult> HandleAsync(LoginCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = TenantContext.Current.RequireTenantId();
        var now = clock.GetUtcNow().UtcDateTime;

        var user = await BuscarUsuarioAsync(command.Email, cancellationToken).ConfigureAwait(false);

        /*
          Se comprueba la contraseña incluso cuando el usuario no existe. El puerto
          acepta un hash nulo justo para esto: la implementación gasta el mismo
          tiempo comparando contra un hash señuelo. Si en cambio se saliera antes
          con un `if (user is null)`, el mensaje sería el mismo pero el tiempo de
          respuesta delataría qué correos están registrados.
        */
        var passwordOk = passwordHasher.Verify(command.Password, user?.PasswordHash);

        if (user is null || !passwordOk)
        {
            if (user is not null)
            {
                // Los intentos se cuentan en la fila del usuario, así que solo hay
                // dónde anotarlos si el usuario existe. El atacante no nota la
                // diferencia: el error que recibe es el mismo.
                user.RegisterFailedLogin(now);
                await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                if (user.IsLockedAt(now))
                {
                    logger.LogWarning(
                        "Cuenta {UserId} bloqueada hasta {LockedUntil} tras {Intentos} intentos fallidos",
                        user.Id,
                        user.LockedUntil,
                        user.LoginAttempts);
                }
            }

            // Sin correo ni contraseña en el log: un log de accesos fallidos con
            // los correos dentro es la misma lista de usuarios que intentamos no
            // publicar, solo que en otro sistema.
            logger.LogWarning("Intento de acceso fallido en el tenant {TenantId}", tenantId);

            throw CredencialesInvalidas();
        }

        if (user.IsLockedAt(now))
        {
            throw new DomainException(
                "ACCOUNT_LOCKED",
                "La cuenta está bloqueada temporalmente tras varios intentos fallidos. " +
                "Vuelve a intentarlo en unos minutos.");
        }

        if (!user.IsActive)
        {
            // Misma respuesta que unas credenciales malas: que una cuenta esté
            // desactivada también es información sobre quién existe.
            logger.LogWarning("Acceso rechazado: la cuenta {UserId} está desactivada", user.Id);
            throw CredencialesInvalidas();
        }

        user.RegisterSuccessfulLogin(now);

        var accessToken = tokenService.IssueAccessToken(user);
        var refreshToken = tokenService.IssueRefreshToken();

        repository.AddSession(Session.Open(
            Guid.NewGuid(),
            user.Id,
            user.TenantId,
            // Del refresh token solo se guarda el hash.
            refreshToken.Hash,
            now,
            refreshToken.ExpiresAt,
            command.IpAddress,
            command.UserAgent));

        outbox.Enqueue(
            user.Id,
            nameof(User),
            "user.logged_in",
            new
            {
                userId = user.Id,
                tenantId,
                role = user.Role.ToString(),
                at = now,
                ipAddress = command.IpAddress,
            });

        // Un único SaveChanges: la sesión, el contador de intentos a cero y el
        // evento entran en la misma transacción.
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Se registra el identificador, nunca el token ni parte de él. Un token en
        // un log es un token robado en cuanto alguien tiene acceso al log.
        logger.LogInformation("Usuario {UserId} inició sesión en el tenant {TenantId}", user.Id, tenantId);

        return new LoginResult(
            user.Id,
            user.Role.ToString(),
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Value,
            refreshToken.ExpiresAt);
    }

    /// <summary>
    /// Un correo con forma inválida tampoco puede distinguirse de uno que no
    /// existe: si lanzara <c>USER_EMAIL_INVALID</c>, el formulario de acceso
    /// respondería distinto según la entrada y volveríamos a tener un oráculo,
    /// esta vez sobre el formato.
    /// </summary>
    private async Task<User?> BuscarUsuarioAsync(string rawEmail, CancellationToken cancellationToken)
    {
        string normalizado;

        try
        {
            normalizado = Email.Create(rawEmail).Value;
        }
        catch (DomainException)
        {
            return null;
        }

        return await repository.GetUserByEmailAsync(normalizado, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// El único error de credenciales que sale de aquí. Está en un método y no
    /// escrito tres veces para que nadie pueda «mejorar» uno de los mensajes y
    /// romper sin querer la garantía de que los tres son indistinguibles.
    /// </summary>
    private static DomainException CredencialesInvalidas() =>
        new("INVALID_CREDENTIALS", "El correo o la contraseña no son correctos.");
}
