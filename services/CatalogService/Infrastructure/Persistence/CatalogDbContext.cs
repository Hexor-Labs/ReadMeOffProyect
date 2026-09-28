using HubNegocios.CatalogService.Domain.Entities;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HubNegocios.CatalogService.Infrastructure.Persistence;

/// <summary>
/// Base de datos de catalog-service (<c>catalog_db</c>).
///
/// Cada servicio tiene la suya y nadie más la toca: si order-service necesita
/// saber de un item, lo pregunta por API o se entera por evento. Un JOIN entre
/// bases de dos servicios es el atajo que convierte diez microservicios en un
/// monolito distribuido, que es lo peor de los dos mundos.
/// </summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemPriceHistory> ItemPriceHistories => Set<ItemPriceHistory>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        modelBuilder.ApplyTenantFilters();

        /*
          Aquí está el detalle que más fácil se rompe de todo el servicio.

          EF Core admite UN SOLO filtro global por entidad: el último
          HasQueryFilter gana y los anteriores se pierden sin avisar. Como
          `Item` necesita dos condiciones —el tenant y el borrado lógico—, no se
          pueden poner por separado: si ItemConfiguration añadiera el de
          IsDeleted, la llamada de arriba a ApplyTenantFilters() lo pisaría y
          los items borrados volverían a aparecer en todas las búsquedas; y si
          el de IsDeleted se pusiera después en la configuración, se perdería el
          del tenant, que es infinitamente peor: se verían los items de otros
          negocios.

          La solución es un único filtro que combina las dos condiciones,
          aplicado DESPUÉS de ApplyTenantFilters() para que sea el que queda. El
          orden de estas dos líneas es, por tanto, obligatorio y no estético.

          Se deja que ApplyTenantFilters() corra igualmente porque es quien pone
          el filtro de ItemPriceHistory y el de cualquier entidad que se añada
          mañana: quitarlo convertiría este servicio en el único donde hay que
          acordarse de filtrar a mano.

          La expresión vive en el dominio (`Item.Visible`) para que el
          repositorio y sus dobles de test usen literalmente la misma, y no dos
          copias que un día dejan de coincidir.
        */
        modelBuilder.Entity<Item>().HasQueryFilter(Item.Visible);

        base.OnModelCreating(modelBuilder);
    }
}
