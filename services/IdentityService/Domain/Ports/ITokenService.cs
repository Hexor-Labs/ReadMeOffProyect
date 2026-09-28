using HubNegocios.IdentityService.Domain.Entities;

namespace HubNegocios.IdentityService.Domain.Ports;

/// <summary>Token de acceso firmado y cuándo caduca.</summary>
public sealed record AccessToken(string Value, DateTime ExpiresAt);

/// <summary>
/// Refresh token recién emitido.
/// </summary>
/// <param name="Value">El secreto. Se devuelve al cliente y no se guarda en ninguna parte.</param>
/// <param name="Hash">Lo único que se persiste.</param>
public sealed record IssuedRefreshToken(string Value, string Hash, DateTime ExpiresAt);

/// <summary>
/// Puerto de emisión de credenciales.
///
/// Está en el dominio porque los casos de uso necesitan emitir tokens, y la
/// implementación en infraestructura porque firmar es cosa de una biblioteca y de
/// una clave que sale de la configuración.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Firma un token de acceso con <c>sub</c>, <c>tenant_id</c>, <c>role</c> y
    /// <c>exp</c>. El <c>tenant_id</c> dentro del token es la pieza que sostiene
    /// el aislamiento de los otros nueve servicios: de ahí lo saca su middleware.
    /// </summary>
    AccessToken IssueAccessToken(User user);

    /// <summary>Genera un refresh token aleatorio y su hash.</summary>
    IssuedRefreshToken IssueRefreshToken();

    /// <summary>Hash de un refresh token que llega de fuera, para poder buscar su sesión.</summary>
    string HashRefreshToken(string refreshToken);
}
