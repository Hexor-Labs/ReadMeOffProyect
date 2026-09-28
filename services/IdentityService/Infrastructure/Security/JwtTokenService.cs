using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using HubNegocios.IdentityService.Domain.Entities;
using HubNegocios.IdentityService.Domain.Ports;

using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace HubNegocios.IdentityService.Infrastructure.Security;

/// <summary>
/// Emite las credenciales del hub.
///
/// Dos cosas distintas con vidas distintas:
///
/// - El **token de acceso** dura 15 minutos y lleva firmados <c>sub</c>,
///   <c>tenant_id</c> y <c>role</c>. Los otros nueve servicios lo validan sin
///   preguntarle a nadie —de ahí que el sistema pueda escalar sin que
///   identity-service sea el cuello de botella— y por eso mismo no se puede
///   revocar antes de que caduque. Quince minutos es el equilibrio: suficiente
///   para no molestar, poco para que un token robado sirva de algo.
///
/// - El **refresh token** dura días, se guarda solo como hash y se rota en cada
///   uso. Es el que sí se puede invalidar.
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(14);

    private readonly string _issuer;
    private readonly string _audience;
    private readonly SigningCredentials _signingCredentials;
    private readonly TimeProvider _clock;

    public JwtTokenService(IConfiguration configuration, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var jwt = configuration.GetSection("Jwt");

        var signingKey = jwt["SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
            {
            // HMAC-SHA256 con una clave más corta que su propia salida es
            // criptográficamente débil. Mejor no arrancar que firmar así.
            throw new InvalidOperationException(
                "Jwt:SigningKey falta o tiene menos de 32 bytes. Sale del vault, nunca del código.");
        }

        _issuer = jwt["Issuer"] ?? throw new InvalidOperationException("Falta Jwt:Issuer.");
        _audience = jwt["Audience"] ?? throw new InvalidOperationException("Falta Jwt:Audience.");
        _clock = clock;

        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);
    }

    public AccessToken IssueAccessToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = _clock.GetUtcNow().UtcDateTime;
        var expiresAt = now.Add(AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            // Esta es la pieza que sostiene el aislamiento de todo el hub: de
            // aquí lo saca el middleware de los otros nueve servicios.
            new("tenant_id", user.TenantId.ToString()),
            new("role", user.Role.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            // Identificador único del token: permite listarlo si algún día hace
            // falta una lista de revocados.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: _signingCredentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public IssuedRefreshToken IssueRefreshToken()
    {
        /*
          256 bits de aleatoriedad criptográfica. No Random ni Guid: un Guid v4
          tiene 122 bits y, sobre todo, no promete ser impredecible. Un refresh
          token adivinable es una sesión ajena regalada.
        */
        var bytes = RandomNumberGenerator.GetBytes(32);
        var value = Base64UrlEncoder.Encode(bytes);

        return new IssuedRefreshToken(
            value,
            HashRefreshToken(value),
            _clock.GetUtcNow().UtcDateTime.Add(RefreshTokenLifetime));
    }

    /*
      SHA-256 a secas, sin BCrypt y sin sal, y es correcto aquí.

      BCrypt existe para hacer LENTO el probar contraseñas, porque una
      contraseña humana se puede adivinar a fuerza de intentos. Un refresh token
      son 256 bits aleatorios: no hay diccionario que lo alcance, así que no hay
      nada que ralentizar. Usar BCrypt aquí solo añadiría cientos de
      milisegundos a cada renovación sin ganar seguridad.

      Lo que sí importa es que en la tabla no quede el token, sino su hash.
    */
    public string HashRefreshToken(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
    }
}
