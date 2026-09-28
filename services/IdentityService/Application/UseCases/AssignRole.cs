using HubNegocios.IdentityService.Domain.Entities;
using HubNegocios.IdentityService.Domain.Ports;
using HubNegocios.SharedKernel.Http;

using Microsoft.Extensions.Logging;

namespace HubNegocios.IdentityService.Application.UseCases;

public sealed record AssignRoleCommand(Guid UserId, UserRole Role);

/// <summary>
/// Cambia el rol de un usuario del tenant en curso.
///
/// No recibe el tenant: el usuario se busca con el filtro global puesto, así que
/// un identificador de otro negocio simplemente no se encuentra y responde «no
/// existe». Eso es lo correcto y además lo deseable — confirmar que el usuario
/// existe pero es de otro tenant ya sería contar algo.
/// </summary>
public sealed class AssignRoleHandler(
    IIdentityRepository repository,
    ILogger<AssignRoleHandler> logger)
{
    public async Task HandleAsync(AssignRoleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await repository.GetUserByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("USER_NOT_FOUND", "El usuario no existe.");

        if (user.Role == command.Role)
        {
            // Idempotente: asignar el rol que ya tiene no es un error, y tratarlo
            // como tal obligaría a quien llama a consultar el rol antes, que es
            // una carrera en sí misma.
            logger.LogInformation("El usuario {UserId} ya tenía el rol {Role}; no se hace nada", user.Id, command.Role);
            return;
        }

        var previous = user.ChangeRole(command.Role);

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        /*
          Se registra en WARNING y no en INFORMATION: un cambio de rol es un cambio
          de privilegios, y es lo primero que se busca en los logs cuando hay que
          entender cómo alguien acabó pudiendo hacer algo que no debía.

          Nótese que el token de acceso que ese usuario ya tiene en la mano sigue
          llevando el rol anterior hasta que caduque. Son quince minutos, que es
          precisamente el motivo de que el token de acceso dure tan poco.
        */
        logger.LogWarning(
            "Rol del usuario {UserId} cambiado de {RolAnterior} a {RolNuevo}",
            user.Id,
            previous,
            user.Role);
    }
}
