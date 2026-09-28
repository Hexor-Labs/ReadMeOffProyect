using HubNegocios.TenantService.Domain.Entities;
using HubNegocios.TenantService.Domain.Ports;

using Microsoft.EntityFrameworkCore;

namespace HubNegocios.TenantService.Infrastructure.Persistence;

/// <summary>
/// Adaptador de salida: implementa el puerto del dominio con EF Core.
///
/// Todo lo que sabe de bases de datos en este servicio empieza y acaba en
/// clases como esta. Los casos de uso hablan con la interfaz, así que se
/// prueban sin Postgres y sin simular EF.
/// </summary>
public sealed class TenantRepository(TenantDbContext context) : ITenantRepository
{
    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Tenant?> GetBySlugOrDomainAsync(string slugOrDomain, CancellationToken cancellationToken = default) =>
        context.Tenants
            // Sin rastreo: es una consulta de solo lectura y la más frecuente
            // del servicio. Rastrear aquí es trabajo y memoria que no se usan.
            .AsNoTracking()
            .FirstOrDefaultAsync(
                t => t.Slug == slugOrDomain || t.CustomDomain == slugOrDomain,
                cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default) =>
        context.Tenants.AsNoTracking().AnyAsync(t => t.Slug == slug, cancellationToken);

    public void Add(Tenant tenant) => context.Tenants.Add(tenant);

    public void AddSubscription(Subscription subscription) => context.Subscriptions.Add(subscription);

    public void AddStatusHistory(TenantStatusHistory history) => context.TenantStatusHistories.Add(history);

    /// <summary>
    /// Un solo <c>SaveChanges</c> ya es atómico: EF Core envuelve todos los
    /// cambios pendientes en una transacción implícita. Por eso el evento de la
    /// outbox y el cambio de negocio entran o no entran juntos, sin necesidad
    /// de abrir la transacción a mano.
    ///
    /// Donde sí hace falta <c>BeginTransactionAsync</c> explícito es cuando un
    /// caso de uso guarda dos veces o mezcla SQL crudo —por ejemplo el
    /// <c>SELECT FOR UPDATE</c> de reservations-service—. Ahí la transacción
    /// abarca lo que EF no puede saber que va junto.
    /// </summary>
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
