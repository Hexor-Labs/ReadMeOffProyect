using HubNegocios.IdentityService.Domain.Entities;
using HubNegocios.IdentityService.Domain.Ports;
using HubNegocios.SharedKernel.Http;

using Microsoft.Extensions.Logging;

namespace HubNegocios.IdentityService.Application.UseCases;

public sealed record RefreshTokenCommand(string RefreshToken, string? IpAddress, string? UserAgent);

public sealed record RefreshTokenResult(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt);

/// <summary>
/// Canjea un refresh token por un token de acceso nuevo, ROTANDO el refresh.
///
/// La rotación es lo que da sentido a este caso de uso. Si el mismo refresh token
/// sirviera indefinidamente, robarlo una vez daría acceso permanente y no habría
/// forma de notarlo. Rotando, cada canje invalida el anterior: el token robado
/// deja de servir en cuanto el dueño legítimo refresca, y el atacante que lo usa
/// deja al dueño fuera — que es un síntoma visible, al contrario que un intruso
/// silencioso.
///
/// La sesión vieja se borra y la nueva se crea en la MISMA transacción. Si fueran
/// dos guardados, un fallo en medio dejaría al usuario sin ninguna sesión válida
/// o con dos, y la segunda es justo lo que la rotación quiere evitar.
/// </summary>
public sealed class RefreshTokenHandler(
    IIdentityRepository repository,
    ITokenService tokenService,
    TimeProvider clock,
    ILogger<RefreshTokenHandler> logger)
{
    public async Task<RefreshTokenResult> HandleAsync(
        RefreshTokenCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            throw TokenInvalido();
        }

        var now = clock.GetUtcNow().UtcDateTime;

        // Se busca por hash: el token que llega no se guarda ni se registra en
        // ningún sitio, solo se hashea para encontrar su fila.
        var hash = tokenService.HashRefreshToken(command.RefreshToken);

        var session = await repository
            .GetSessionByRefreshTokenHashAsync(hash, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            logger.LogWarning("Canje de refresh token rechazado: no hay sesión con ese hash");
            throw TokenInvalido();
        }

        if (session.IsExpiredAt(now))
        {
            // Se aprovecha para limpiar: una sesión caducada no vuelve a servir y
            // dejarla solo engorda la tabla.
            repository.RemoveSession(session);
            await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Sesión {SessionId} caducada y eliminada en el canje", session.Id);
            throw TokenInvalido();
        }

        var user = await repository.GetUserByIdAsync(session.UserId, cancellationToken).ConfigureAwait(false);

        if (user is null || !user.IsActive || user.IsLockedAt(now))
        {
            /*
              Un refresh token válido no puede sobrevivir a la cuenta. Sin esta
              comprobación, desactivar o bloquear a alguien no tendría efecto real
              hasta que caducara su refresh, que son semanas.
            */
            repository.RemoveSession(session);
            await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogWarning("Canje rechazado: la cuenta {UserId} no puede renovar sesión", session.UserId);
            throw TokenInvalido();
        }

        var accessToken = tokenService.IssueAccessToken(user);
        var refreshToken = tokenService.IssueRefreshToken();

        repository.RemoveSession(session);

        repository.AddSession(Session.Open(
            Guid.NewGuid(),
            user.Id,
            user.TenantId,
            refreshToken.Hash,
            now,
            refreshToken.ExpiresAt,
            // Se toman los del canje, no los de la sesión anterior: el mismo
            // usuario puede haber cambiado de red desde que inició sesión.
            command.IpAddress,
            command.UserAgent));

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Sesión rotada para el usuario {UserId}", user.Id);

        return new RefreshTokenResult(
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Value,
            refreshToken.ExpiresAt);
    }

    /// <summary>
    /// Un solo error para todos los motivos: token desconocido, caducado o de una
    /// cuenta que ya no puede entrar. Igual que en el login, decir cuál de los
    /// tres fue solo ayuda a quien está probando tokens.
    /// </summary>
    private static DomainException TokenInvalido() =>
        new("REFRESH_TOKEN_INVALID", "La sesión no es válida. Vuelve a iniciar sesión.");
}
