using HubNegocios.CatalogService.Domain.Entities;

namespace HubNegocios.CatalogService.Domain.Ports;

/// <summary>
/// Criterios de búsqueda del catálogo, ya normalizados por el caso de uso.
///
/// Llega al repositorio con el tamaño de página YA acotado: el adaptador no
/// vuelve a comprobarlo porque el sitio donde se decide el tope debe ser uno
/// solo, y ese sitio es el caso de uso.
/// </summary>
public sealed record ItemSearchCriteria(
    Guid? CategoryId,
    IReadOnlyCollection<string> Tags,
    decimal? MinPrice,
    decimal? MaxPrice,
    int Page,
    int PageSize)
{
    /// <summary>Filas que se salta la consulta. La página 1 no se salta ninguna.</summary>
    public int Skip => (Page - 1) * PageSize;
}

/// <summary>Una página de items y el total de coincidencias sin paginar.</summary>
public sealed record ItemPage(IReadOnlyList<Item> Items, int Total);

/// <summary>
/// Puerto de salida hacia el almacenamiento.
///
/// Vive en el dominio y lo implementa la infraestructura: esa inversión es lo
/// que permite que los casos de uso se prueben sin base de datos y sin simular
/// EF Core. Por eso el contrato habla de items y no de <c>IQueryable</c>,
/// <c>DbSet</c> ni <c>Include</c> — en cuanto asoma un tipo de EF por aquí, el
/// dominio deja de ser puro y la promesa se rompe.
///
/// Fíjate en lo que NO hay: ningún <c>Remove</c> ni <c>Delete</c>. El borrado
/// de un item es lógico, así que la operación física no existe en el contrato y
/// nadie puede invocarla por descuido.
/// </summary>
public interface IItemRepository
{
    Task<Item?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>¿Ya hay un item con ese SKU en el tenant en curso?</summary>
    Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>Búsqueda paginada con el total, para que el cliente pueda pintar el paginador.</summary>
    Task<ItemPage> SearchAsync(ItemSearchCriteria criteria, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lectura por lote de ids. La usa order-service de forma síncrona al crear
    /// una orden: una sola consulta en vez de una por línea del pedido.
    /// </summary>
    Task<IReadOnlyList<Item>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);

    void Add(Item item);

    void AddPriceHistory(ItemPriceHistory history);

    /// <summary>
    /// Confirma los cambios pendientes.
    ///
    /// Nótese que no hay <c>Update</c>: las entidades que vienen del repositorio
    /// están rastreadas, así que basta con modificarlas y guardar. Un
    /// <c>Update(item)</c> aquí sería ruido que además invita a pensar que se
    /// puede guardar un objeto que no se leyó antes.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
