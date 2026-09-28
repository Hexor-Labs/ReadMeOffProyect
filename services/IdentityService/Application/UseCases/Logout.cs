using HubNegocios.IdentityService.Domain.Ports;

using Microsoft.Extensions.Logging;

namespace HubNegocios.IdentityService.Application.UseCases;

public sealed record LogoutCommand(string RefreshToken);

/// <summary>
/// Cierra la sesión: borra la fila del refresh token que se presenta.
///
/// Solo mata ESA sesión, no todas las del usuario. Cerrar sesión en el móvil no
/// tiene por qué echar a nadie del ordenador de la oficina.
///
/// Es idempotente y silencioso: si el token no corresponde a ninguna sesión, no
/// pasa nada y no es un error. Por un lado, cerrar dos veces lo ya cerrado es un
/// resultado correcto; por otro, responder distinto convertiría este endpoint en
/// una forma de averiguar si un token es válido sin usarlo.
/// </summary>
public sealed class LogoutHandler(
    IIdentityRepository repository,
    ITokenService tokenService,
    ILogger<LogoutHandler> logger)
{
    public async Task HandleAsync(LogoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return;
        }

        var session = await repository
            .GetSessionByRefreshTokenHashAsync(
                tokenService.HashRefreshToken(command.RefreshToken),
                cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            logger.LogInformation("Cierre de sesión sin efecto: el token no corresponde a ninguna sesión viva");
            return;
        }

        repository.RemoveSession(session);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Sesión {SessionId} del usuario {UserId} cerrada", session.Id, session.UserId);
    }
}
