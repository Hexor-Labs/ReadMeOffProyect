using HubNegocios.CatalogService.Domain.Entities;
using HubNegocios.CatalogService.Domain.Ports;

using Microsoft.EntityFrameworkCore;

namespace HubNegocios.CatalogService.Infrastructure.Persistence;

/// <summary>
/// Adaptador de salida: implementa el puerto del dominio con EF Core.
///
/// Todo lo que sabe de bases de datos en este servicio empieza y acaba en clases
/// como esta. Los casos de uso hablan con la interfaz, así que se prueban sin
/// Postgres y sin simular EF.
///
/// Ninguna consulta de aquí filtra por tenant ni por <c>is_deleted</c> a mano:
/// eso lo hace el filtro global que <see cref="CatalogDbContext"/> monta con
/// <c>Item.Visible</c>. Escribirlo otra vez aquí sería la segunda copia de una
/// regla que debe tener una sola.
/// </summary>
public sealed class ItemRepository(CatalogDbContext context) : IItemRepository
{
    public Task<Item?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Items.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    /// <summary>
    /// Ojo con lo que esto significa: como el filtro global quita los borrados,
    /// el SKU de un item borrado cuenta como libre. Es deliberado y va a juego
    /// con el índice único, que también excluye los borrados — si no, un negocio
    /// no podría volver a dar de alta un producto que retiró por error.
    /// </summary>
    public Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken = default) =>
        context.Items.AsNoTracking().AnyAsync(item => item.Sku == sku, cancellationToken);

    public async Task<ItemPage> SearchAsync(
        ItemSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        // Sin rastreo: es una consulta de solo lectura y la más frecuente del
        // servicio. Rastrear aquí es trabajo y memoria que no se usan.
        var consulta = context.Items.AsNoTracking();

        if (criteria.CategoryId is { } categoryId)
        {
            consulta = consulta.Where(item => item.CategoryId == categoryId);
        }

        /*
          Una condición por etiqueta, todas obligatorias: pedir «vegano» y
          «sin-gluten» devuelve lo que cumple las dos, no lo que cumple alguna.
          Traducido, cada Contains sobre una columna text[] se resuelve con el
          índice GIN que crea la migración, así que añadir etiquetas acota la
          búsqueda en vez de encarecerla.
        */
        foreach (var etiqueta in criteria.Tags)
        {
            consulta = consulta.Where(item => item.Tags.Contains(etiqueta));
        }

        if (criteria.MinPrice is { } minPrice)
        {
            consulta = consulta.Where(item => item.PriceAmount >= minPrice);
        }

        if (criteria.MaxPrice is { } maxPrice)
        {
            consulta = consulta.Where(item => item.PriceAmount <= maxPrice);
        }

        // El total se cuenta con los filtros puestos y sin paginar: es lo que
        // necesita el cliente para saber cuántas páginas hay.
        var total = await consulta.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await consulta
            /*
              Orden explícito y con desempate por id. Sin ORDER BY, Postgres
              puede devolver las filas en cualquier orden entre dos consultas, y
              entonces la página 2 repite u omite items respecto a la 1 sin que
              nada falle visiblemente.
            */
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Skip(criteria.Skip)
            .Take(criteria.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ItemPage(items, total);
    }

    public async Task<IReadOnlyList<Item>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        return await context.Items
            .AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(Item item) => context.Items.Add(item);

    public void AddPriceHistory(ItemPriceHistory history) => context.ItemPriceHistories.Add(history);

    /// <summary>
    /// Un solo <c>SaveChanges</c> ya es atómico: EF Core envuelve todos los
    /// cambios pendientes en una transacción implícita. Por eso el cambio de
    /// precio, su fila de historial y el evento de la outbox entran o no entran
    /// juntos, sin necesidad de abrir la transacción a mano.
    /// </summary>
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
