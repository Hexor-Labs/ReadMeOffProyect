using HubNegocios.TenantService.Domain.Entities;

namespace HubNegocios.TenantService.Domain.Ports;

/// <summary>
/// Puerto de salida hacia el almacenamiento.
///
/// Vive en el dominio y lo implementa la infraestructura: esa inversión es lo
/// que permite que los casos de uso se prueben sin base de datos y sin simular
/// EF Core. Por eso el contrato habla de tenants y no de <c>IQueryable</c>,
/// <c>DbSet</c> ni <c>Include</c> — en cuanto asoma un tipo de EF por aquí, el
/// dominio deja de ser puro y la promesa se rompe.
/// </summary>
public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Resolución desde el subdominio o el dominio propio. La usa el gateway.</summary>
    Task<Tenant?> GetBySlugOrDomainAsync(string slugOrDomain, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    void Add(Tenant tenant);

    void AddSubscription(Subscription subscription);

    void AddStatusHistory(TenantStatusHistory history);

    /// <summary>
    /// Confirma los cambios pendientes.
    ///
    /// Nótese que no hay <c>Update</c>: las entidades que vienen del
    /// repositorio están rastreadas, así que basta con modificarlas y guardar.
    /// Un <c>Update(tenant)</c> aquí sería ruido que además invita a pensar que
    /// se puede guardar un objeto que no se leyó antes.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
