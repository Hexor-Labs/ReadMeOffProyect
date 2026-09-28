using System.ComponentModel.DataAnnotations;

using HubNegocios.TenantService.Application.UseCases;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HubNegocios.TenantService.Infrastructure.Http;

/// <summary>
/// Cuerpo de alta de tenant.
///
/// Es un DTO y no la entidad de dominio. La diferencia no es ceremonia: si el
/// endpoint aceptara un <c>Tenant</c>, cualquiera podría mandar
/// <c>"status": "Active"</c> o <c>"plan": "enterprise"</c> en el JSON y el
/// binder los asignaría sin preguntar. Aquí solo entra lo que el cliente tiene
/// derecho a decidir.
/// </summary>
public sealed record CreateTenantRequest(
    [property: Required, StringLength(200, MinimumLength = 2)] string Name,
    [property: Required, StringLength(50)] string VerticalType,
    [property: Required, StringLength(63, MinimumLength = 3)] string Slug,
    [property: Required, EmailAddress, StringLength(320)] string Email,
    [property: Phone, StringLength(30)] string? Phone,
    [property: StringLength(1000)] string? Description);

public sealed record UpdateBrandingRequest(
    [property: Required] string BrandingJson,
    [property: Url, StringLength(500)] string? LogoUrl);

public sealed record SuspendTenantRequest([property: StringLength(500)] string? Reason);

[ApiController]
[Route("api/tenants")]
public sealed class TenantsController(
    CreateTenantHandler createTenant,
    UpdateTenantBrandingHandler updateBranding,
    SuspendTenantHandler suspendTenant,
    GetTenantBySlugOrDomainHandler resolveTenant) : ControllerBase
{
    /// <summary>
    /// Alta de un negocio. Operación de plataforma, no de cliente.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType<CreateTenantResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateTenantRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await createTenant.HandleAsync(
            new CreateTenantCommand(
                request.Name,
                request.VerticalType,
                request.Slug,
                request.Email,
                request.Phone,
                request.Description),
            cancellationToken).ConfigureAwait(false);

        return CreatedAtAction(nameof(ResolveAsync), new { slugOrDomain = result.Slug }, result);
    }

    [HttpPatch("{tenantId:guid}/branding")]
    [Authorize(Roles = "PlatformAdmin,Owner")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateBrandingAsync(
        Guid tenantId,
        [FromBody] UpdateBrandingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await updateBranding.HandleAsync(
            new UpdateTenantBrandingCommand(tenantId, request.BrandingJson, request.LogoUrl),
            cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    [HttpPost("{tenantId:guid}/suspend")]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SuspendAsync(
        Guid tenantId,
        [FromBody] SuspendTenantRequest request,
        CancellationToken cancellationToken)
    {
        await suspendTenant.HandleAsync(
            new SuspendTenantCommand(tenantId, request?.Reason),
            cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// Resuelve el tenant desde el subdominio o el dominio propio.
    ///
    /// Anónimo por necesidad: el gateway llama aquí antes de que exista sesión,
    /// para saber a qué negocio pertenece la petición que acaba de entrar. Por
    /// eso devuelve solo datos públicos —los que de todas formas se ven en la
    /// página del negocio— y nada de facturación ni de consumo.
    /// </summary>
    [HttpGet("resolve/{slugOrDomain}")]
    [AllowAnonymous]
    [ProducesResponseType<TenantResolution>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResolveAsync(string slugOrDomain, CancellationToken cancellationToken)
    {
        var tenant = await resolveTenant.HandleAsync(slugOrDomain, cancellationToken).ConfigureAwait(false);
        return Ok(tenant);
    }
}
