using System.ComponentModel.DataAnnotations;

using HubNegocios.IdentityService.Application.UseCases;
using HubNegocios.IdentityService.Domain.Entities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HubNegocios.IdentityService.Infrastructure.Http;

public sealed record RegisterRequest(
    [property: Required, EmailAddress, StringLength(320)] string Email,
    [property: Required, StringLength(200, MinimumLength = 2)] string FullName,
    [property: Phone, StringLength(30)] string? Phone,
    [property: Required, StringLength(128, MinimumLength = 10)] string Password,
    [property: Required] bool AcceptsTerms,
    [property: Required] bool AcceptsPrivacyPolicy);

public sealed record LoginRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required] string Password);

public sealed record AssignRoleRequest([property: Required] UserRole Role);

/// <summary>
/// Autenticación del hub.
///
/// El refresh token viaja en una cookie <c>HttpOnly</c>, nunca en el cuerpo de
/// la respuesta ni en <c>localStorage</c>. Es la diferencia práctica entre que
/// un XSS robe la sesión y que no pueda: JavaScript no puede leer una cookie
/// marcada <c>HttpOnly</c>, pero lee <c>localStorage</c> sin esfuerzo.
///
/// El token de acceso sí va en el cuerpo: dura quince minutos y el cliente
/// tiene que poder ponerlo en la cabecera <c>Authorization</c>.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    RegisterUserHandler registerUser,
    LoginHandler login,
    RefreshTokenHandler refreshToken,
    LogoutHandler logout,
    AssignRoleHandler assignRole,
    CheckPermissionHandler checkPermission) : ControllerBase
{
    private const string RefreshCookie = "hub_refresh";

    /// <summary>
    /// Alta de una cuenta dentro del tenant que resuelva el gateway.
    ///
    /// Anónimo por necesidad: quien se registra todavía no tiene token. El
    /// tenant lo pone el gateway en la cabecera, no el cuerpo de la petición.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<RegisterUserResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await registerUser.HandleAsync(
            new RegisterUserCommand(
                request.Email,
                request.FullName,
                request.Phone,
                request.Password,
                // El rol NO se acepta del cuerpo: quien se registra desde fuera
                // es siempre cliente. Si viniera en el JSON, cualquiera podría
                // darse de alta como Owner del negocio.
                UserRole.Customer,
                request.AcceptsTerms,
                request.AcceptsPrivacyPolicy),
            cancellationToken).ConfigureAwait(false);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LoginAsync(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await login.HandleAsync(
            new LoginCommand(request.Email, request.Password, ClientIp(), UserAgent()),
            cancellationToken).ConfigureAwait(false);

        SetRefreshCookie(result.RefreshToken, result.RefreshTokenExpiresAt);

        return Ok(new
        {
            userId = result.UserId,
            role = result.Role,
            accessToken = result.AccessToken,
            expiresAt = result.AccessTokenExpiresAt,
        });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshAsync(CancellationToken cancellationToken)
    {
        // Se lee de la cookie, no del cuerpo: si el cliente tuviera que
        // mandarlo, JavaScript tendría que poder leerlo, y entonces la cookie
        // HttpOnly no habría servido de nada.
        var token = Request.Cookies[RefreshCookie];

        var result = await refreshToken.HandleAsync(
            new RefreshTokenCommand(token ?? string.Empty, ClientIp(), UserAgent()),
            cancellationToken).ConfigureAwait(false);

        SetRefreshCookie(result.RefreshToken, result.RefreshTokenExpiresAt);

        return Ok(new
        {
            accessToken = result.AccessToken,
            expiresAt = result.AccessTokenExpiresAt,
        });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        var token = Request.Cookies[RefreshCookie];

        if (!string.IsNullOrWhiteSpace(token))
        {
            await logout.HandleAsync(new LogoutCommand(token), cancellationToken).ConfigureAwait(false);
        }

        Response.Cookies.Delete(RefreshCookie);
        return NoContent();
    }

    [HttpPut("users/{userId:guid}/role")]
    [Authorize(Roles = "Owner,Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AssignRoleAsync(
        Guid userId,
        [FromBody] AssignRoleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await assignRole.HandleAsync(new AssignRoleCommand(userId, request.Role), cancellationToken)
            .ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// Comprobación de permiso. La llaman otros servicios, no el navegador.
    /// </summary>
    [HttpGet("permissions/{permissionCode}")]
    [ProducesResponseType<CheckPermissionResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckPermissionAsync(
        string permissionCode,
        [FromQuery] Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await checkPermission
            .HandleAsync(new CheckPermissionQuery(userId, permissionCode), cancellationToken)
            .ConfigureAwait(false);

        return Ok(result);
    }

    private void SetRefreshCookie(string value, DateTime expiresAt) =>
        Response.Cookies.Append(RefreshCookie, value, new CookieOptions
        {
            // Las cuatro banderas importan y cada una tapa una cosa distinta:
            HttpOnly = true,                     // JavaScript no lo lee: un XSS no roba la sesión
            Secure = true,                       // solo por HTTPS: no viaja en claro
            SameSite = SameSiteMode.Strict,      // no se envía desde otros sitios: corta el CSRF
            Expires = new DateTimeOffset(expiresAt, TimeSpan.Zero),
            Path = "/api/auth",                  // solo donde hace falta
        });

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? UserAgent()
    {
        var value = Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(value.Length, 500)];
    }
}
