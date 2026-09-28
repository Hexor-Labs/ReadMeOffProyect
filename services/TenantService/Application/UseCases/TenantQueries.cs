using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.TenantService.Domain.Entities;
using HubNegocios.TenantService.Domain.Ports;

using Microsoft.Extensions.Logging;

namespace HubNegocios.TenantService.Application.UseCases;

public sealed record UpdateTenantBrandingCommand(Guid TenantId, string BrandingJson, string? LogoUrl);

/// <summary>Cambia la identidad visual del tenant.</summary>
public sealed class UpdateTenantBrandingHandler(
    ITenantRepository repository,
    IOutboxWriter outbox,
    ILogger<UpdateTenantBrandingHandler> logger)
{
    public async Task HandleAsync(UpdateTenantBrandingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenant = await repository.GetByIdAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("TENANT_NOT_FOUND", "El tenant no existe.");

        tenant.UpdateBranding(command.BrandingJson, command.LogoUrl);

        // landing-builder-service escucha esto para repintar las páginas
        // publicadas sin que nadie tenga que entrar a tocarlas.
        outbox.EnqueueFor(
            tenant.Id,
            tenant.Id,
            nameof(Tenant),
            "tenant.branding_updated",
            new { tenantId = tenant.Id, logoUrl = tenant.LogoUrl });

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Identidad visual del tenant {TenantId} actualizada", tenant.Id);
    }
}

public sealed record TenantResolution(
    Guid Id,
    string Name,
    string Slug,
    string VerticalType,
    string Status,
    string Plan,
    bool IsWhiteLabel,
    string? CustomDomain,
    string? LogoUrl,
    string BrandingJson);

/// <summary>
/// Resuelve el tenant a partir del subdominio o del dominio propio.
///
/// Es la consulta más caliente del servicio: la hace el gateway en CADA
/// petición que entra al hub, antes incluso de saber quién es el usuario. Dos
/// consecuencias de diseño: es el único endpoint de este servicio que puede ser
/// anónimo, y es el primer candidato a caché —el slug de un tenant cambia una
/// vez en la vida, si es que cambia—.
/// </summary>
public sealed class GetTenantBySlugOrDomainHandler(ITenantRepository repository)
{
    public async Task<TenantResolution> HandleAsync(
        string slugOrDomain,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slugOrDomain))
        {
            throw new NotFoundException("TENANT_NOT_FOUND", "No se indicó slug ni dominio.");
        }

        var tenant = await repository
            .GetBySlugOrDomainAsync(slugOrDomain.Trim().ToLowerInvariant(), cancellationToken)
            .ConfigureAwait(false);

        /*
          Un tenant eliminado se responde igual que uno inexistente. Distinguirlos
          convertiría este endpoint —que es público— en una forma cómoda de
          averiguar qué negocios hubo en el hub y cuáles se fueron.
        */
        if (tenant is null || tenant.Status == TenantStatus.Deleted)
        {
            throw new NotFoundException("TENANT_NOT_FOUND", "No hay ningún negocio en esa dirección.");
        }

        return new TenantResolution(
            tenant.Id,
            tenant.Name,
            tenant.Slug,
            tenant.VerticalType,
            tenant.Status.ToString(),
            tenant.Plan,
            tenant.IsWhiteLabel,
            tenant.CustomDomain,
            tenant.LogoUrl,
            tenant.BrandingJson);
    }
}
