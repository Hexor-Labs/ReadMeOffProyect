using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.IdentityService.Domain.Entities;

/// <summary>
/// Una sesión viva: el refresh token que un dispositivo concreto puede canjear
/// por tokens de acceso nuevos.
///
/// Aquí se guarda un HASH del refresh token, nunca el token.
/// La diferencia importa: el token de acceso vive quince minutos y se puede dar
/// por perdido, pero el refresh token dura semanas, y una copia de esta tabla
/// —un volcado, una réplica de lectura mal protegida, un backup— sería una
/// llave maestra para todas las sesiones abiertas si guardase el valor en claro.
/// Con el hash, quien lea la tabla no puede canjear nada.
///
/// El hash es SHA-256 y no BCrypt, al contrario que las contraseñas. No es un
/// descuido: BCrypt es lento a propósito porque una contraseña humana tiene poca
/// entropía y hay que encarecer cada intento del atacante. Un refresh token son
/// 256 bits aleatorios; no hay diccionario que lo adivine, así que el coste extra
/// solo lo pagaríamos nosotros en cada refresco. Además BCrypt no permite buscar
/// por igualdad, y este valor se busca en cada canje.
///
/// No hay columna de «revocada»: invalidar una sesión es borrar su fila. El
/// conjunto de datos de una sesión es justo lo que hace falta para canjear, y una
/// fila que ya no sirve solo puede confundir a quien la lea.
/// </summary>
public sealed class Session : ITenantOwned
{
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>SHA-256 del refresh token, en Base64.</summary>
    public string RefreshTokenHash { get; private set; } = string.Empty;

    public DateTime IssuedAt { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Desde dónde se abrió. Se guarda para que el dueño de la cuenta pueda
    /// reconocer sus sesiones; es un dato personal, así que no se compone con
    /// nada ni se saca del servicio.
    /// </summary>
    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    private Session()
    {
    }

    public static Session Open(
        Guid id,
        Guid userId,
        Guid tenantId,
        string refreshTokenHash,
        DateTime issuedAt,
        DateTime expiresAt,
        string? ipAddress,
        string? userAgent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshTokenHash);

        if (expiresAt <= issuedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                "Una sesión que nace caducada no sirve para nada.");
        }

        return new Session
        {
            Id = id,
            UserId = userId,
            TenantId = tenantId,
            RefreshTokenHash = refreshTokenHash,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress.Trim(),
            // Recortado al ancho de la columna: los agentes de usuario son
            // larguísimos y los manda el cliente, así que aquí no se confía en
            // que quepan.
            UserAgent = string.IsNullOrWhiteSpace(userAgent)
                ? null
                : userAgent.Trim()[..Math.Min(userAgent.Trim().Length, MaxUserAgentLength)],
        };
    }

    public const int MaxUserAgentLength = 400;

    public bool IsExpiredAt(DateTime now) => ExpiresAt <= now;
}
