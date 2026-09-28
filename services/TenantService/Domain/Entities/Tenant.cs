using HubNegocios.SharedKernel.Auditing;
using HubNegocios.SharedKernel.Http;
using HubNegocios.TenantService.Domain.ValueObjects;

namespace HubNegocios.TenantService.Domain.Entities;

public enum TenantStatus
{
    Active,
    Suspended,
    Deleted,
}

/// <summary>
/// Un negocio dentro del hub.
///
/// Ojo con algo que distingue a este servicio de los otros nueve: aquí
/// <c>Tenant</c> NO implementa <c>ITenantOwned</c>, y es deliberado. En el
/// resto del sistema el tenant es el filtro; aquí el tenant es la fila. Si
/// implementara la interfaz se le pondría un filtro global comparando contra
/// el tenant del contexto —que en este servicio no existe— y ninguna consulta
/// devolvería nada.
///
/// La contrapartida es que este servicio se protege de otra forma: sus
/// endpoints son de plataforma, no de cliente, y deben quedar detrás de un rol
/// administrativo. La única excepción es la resolución por slug o dominio, que
/// el gateway necesita antes de que exista sesión alguna.
///
/// El dominio es puro: no conoce EF Core ni ASP.NET. Las propiedades tienen
/// <c>private set</c> y se cambian por métodos con nombre, para que un estado
/// imposible —suspendido sin motivo, slug vacío— no se pueda construir.
/// </summary>
public sealed class Tenant : IAuditable
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Vertical de negocio: restaurante, hotel, tienda…</summary>
    public string VerticalType { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? Description { get; private set; }
    public string? LogoUrl { get; private set; }

    /// <summary>Colores, tipografías y demás. jsonb: forma libre por diseño.</summary>
    public string BrandingJson { get; private set; } = "{}";

    public TenantStatus Status { get; private set; } = TenantStatus.Active;

    public bool IsWhiteLabel { get; private set; }

    /// <summary>Dominio propio del cliente, si lo trae.</summary>
    public string? CustomDomain { get; private set; }

    public string Plan { get; private set; } = DefaultPlan;

    public string FeaturesJson { get; private set; } = "{}";

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    public const string DefaultPlan = "starter";

    private Tenant()
    {
    }

    public static Tenant Register(
        Guid id,
        string name,
        string verticalType,
        Slug slug,
        string email,
        string? phone,
        string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(verticalType);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        return new Tenant
        {
            Id = id,
            Name = name.Trim(),
            VerticalType = verticalType.Trim(),
            Slug = slug.Value,
            Email = email.Trim().ToLowerInvariant(),
            Phone = phone?.Trim(),
            Description = description?.Trim(),
            Status = TenantStatus.Active,
            Plan = DefaultPlan,
        };
    }

    public void UpdateBranding(string brandingJson, string? logoUrl)
    {
        EnsureUsable();

        BrandingJson = string.IsNullOrWhiteSpace(brandingJson) ? "{}" : brandingJson;
        LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
    }

    /// <summary>
    /// Suspende el tenant. Devuelve el estado anterior para que el caso de uso
    /// pueda registrarlo en el historial sin tener que leerlo antes.
    /// </summary>
    public TenantStatus Suspend()
    {
        if (Status == TenantStatus.Deleted)
        {
            throw new ConflictException(
                "TENANT_DELETED",
                "Un tenant eliminado no se puede suspender.");
        }

        var previous = Status;
        Status = TenantStatus.Suspended;
        return previous;
    }

    public TenantStatus Reactivate()
    {
        if (Status != TenantStatus.Suspended)
        {
            throw new ConflictException(
                "TENANT_NOT_SUSPENDED",
                "Solo se puede reactivar un tenant suspendido.");
        }

        var previous = Status;
        Status = TenantStatus.Active;
        return previous;
    }

    public void AssignCustomDomain(string? customDomain, bool isWhiteLabel)
    {
        EnsureUsable();

        CustomDomain = string.IsNullOrWhiteSpace(customDomain) ? null : customDomain.Trim().ToLowerInvariant();
        IsWhiteLabel = isWhiteLabel;
    }

    public void ChangePlan(string plan, string featuresJson)
    {
        EnsureUsable();
        ArgumentException.ThrowIfNullOrWhiteSpace(plan);

        Plan = plan.Trim().ToLowerInvariant();
        FeaturesJson = string.IsNullOrWhiteSpace(featuresJson) ? "{}" : featuresJson;
    }

    private void EnsureUsable()
    {
        if (Status == TenantStatus.Deleted)
        {
            throw new ConflictException("TENANT_DELETED", "El tenant está eliminado.");
        }
    }
}
