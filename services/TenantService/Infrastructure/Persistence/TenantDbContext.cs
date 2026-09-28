using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Persistence;
using HubNegocios.TenantService.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace HubNegocios.TenantService.Infrastructure.Persistence;

/// <summary>
/// Base de datos de tenant-service (<c>tenant_db</c>).
///
/// Cada servicio tiene la suya y nadie más la toca: si otro servicio necesita
/// saber de un tenant, lo pregunta por API o se entera por evento. Un JOIN
/// entre bases de dos servicios es el atajo que convierte diez microservicios
/// en un monolito distribuido, que es lo peor de los dos mundos.
/// </summary>
public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<TenantStatusHistory> TenantStatusHistories => Set<TenantStatusHistory>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TenantDbContext).Assembly);
        modelBuilder.ApplyOutbox();

        /*
          Aquí NO se llama a ApplyTenantFilters(), y es la diferencia de fondo
          entre este servicio y los otros nueve.

          En el resto del hub el tenant es el filtro de todo. Aquí el tenant es
          la fila: la tabla `tenants` ES el catálogo de tenants. Un filtro
          global comparando contra el tenant del contexto —que en este servicio
          no existe— dejaría todas las consultas a cero.

          A cambio, este servicio se protege de otra forma: sus endpoints son de
          plataforma y exigen rol administrativo, con la única excepción de la
          resolución por slug, que el gateway necesita antes de que haya sesión.
        */

        base.OnModelCreating(modelBuilder);
    }
}
