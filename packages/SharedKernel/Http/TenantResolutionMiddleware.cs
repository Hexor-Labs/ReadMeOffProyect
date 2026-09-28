using System.Diagnostics;
using System.Security.Claims;

using HubNegocios.SharedKernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HubNegocios.SharedKernel.Http;

/// <summary>
/// El borde: aquí, y solo aquí, entra un tenant al sistema.
///
/// Lee <c>tenant_id</c>, <c>sub</c> y <c>role</c> del JWT ya validado por el
/// middleware de autenticación y abre un ámbito de <see cref="TenantContext"/>
/// que dura lo que dure la petición. De ahí en adelante nadie vuelve a pasar
/// el tenant a mano: lo recogen los filtros de EF Core, los interceptores y la
/// outbox por su cuenta.
///
/// Va después de <c>UseAuthentication()</c>, nunca antes: sin eso leería
/// claims de un token que todavía no se ha comprobado, que es como no
/// comprobarlo.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
{
    public const string CorrelationHeader = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = ResolveCorrelationId(context);

        // Se devuelve siempre, también en los errores: es el número que el
        // usuario puede leernos por teléfono para que encontremos su caso.
        context.Response.Headers[CorrelationHeader] = correlationId.ToString();
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationHeader] = correlationId.ToString();
            return Task.CompletedTask;
        });

        Activity.Current?.SetTag("correlation_id", correlationId);

        var identity = new TenantIdentity(
            TenantId: ReadGuidClaim(context.User, "tenant_id"),
            UserId: ReadGuidClaim(context.User, ClaimTypes.NameIdentifier) ?? ReadGuidClaim(context.User, "sub"),
            Role: context.User.FindFirst(ClaimTypes.Role)?.Value ?? context.User.FindFirst("role")?.Value,
            CorrelationId: correlationId);

        if (identity.TenantId is not null)
        {
            Activity.Current?.SetTag("tenant_id", identity.TenantId);
        }

        using (TenantContext.BeginScope(identity))
        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["tenant_id"] = identity.TenantId,
            ["user_id"] = identity.UserId,
            ["correlation_id"] = correlationId,
        }))
        {
            await next(context).ConfigureAwait(false);
        }
    }

    private static Guid ResolveCorrelationId(HttpContext context)
    {
        // Si la petición viene de otro servicio, se respeta su correlación:
        // eso es lo que permite seguir una operación entre microservicios.
        var header = context.Request.Headers[CorrelationHeader].FirstOrDefault();
        return Guid.TryParse(header, out var parsed) ? parsed : Guid.NewGuid();
    }

    private static Guid? ReadGuidClaim(ClaimsPrincipal? principal, string claimType) =>
        Guid.TryParse(principal?.FindFirst(claimType)?.Value, out var value) ? value : null;
}
