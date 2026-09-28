using HubNegocios.IdentityService.Domain.Entities;
using HubNegocios.IdentityService.Domain.Ports;

using Microsoft.Extensions.Logging;

namespace HubNegocios.IdentityService.Application.UseCases;

public sealed record CheckPermissionQuery(Guid UserId, string PermissionCode);

/// <summary>Respuesta de la comprobación. <paramref name="Role"/> es nulo cuando no hay nada que autorizar.</summary>
public sealed record CheckPermissionResult(bool Allowed, string? Role);

/// <summary>
/// Responde si un usuario puede hacer algo. La llaman los demás servicios de
/// forma síncrona, en línea con la petición que están atendiendo.
///
/// Ser síncrona condiciona el diseño: es una consulta en el camino crítico de
/// otros servicios, así que hace dos lecturas y ninguna escritura, no emite
/// eventos y no lanza excepciones por casos normales. Un usuario que no existe no
/// es un 404 sino un «no»: este endpoint lo llama cualquier servicio del hub, y
/// distinguir «no existe» de «no puede» lo convertiría en una forma cómoda de
/// averiguar qué identificadores de usuario son reales.
/// </summary>
public sealed class CheckPermissionHandler(
    IIdentityRepository repository,
    ILogger<CheckPermissionHandler> logger)
{
    public async Task<CheckPermissionResult> HandleAsync(
        CheckPermissionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.PermissionCode))
        {
            return new CheckPermissionResult(false, null);
        }

        var user = await repository.GetUserByIdAsync(query.UserId, cancellationToken).ConfigureAwait(false);

        if (user is null || !user.IsActive)
        {
            return new CheckPermissionResult(false, null);
        }

        var allowed = await repository
            // Se normaliza el código antes de buscar: quien llama es otro
            // servicio y no tiene por qué acertar con las mayúsculas.
            .RoleHasPermissionAsync(user.Role, Permission.Normalize(query.PermissionCode), cancellationToken)
            .ConfigureAwait(false);

        if (!allowed)
        {
            logger.LogInformation(
                "Permiso {Permiso} denegado al rol {Role}",
                Permission.Normalize(query.PermissionCode),
                user.Role);
        }

        return new CheckPermissionResult(allowed, user.Role.ToString());
    }
}
