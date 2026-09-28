using HubNegocios.OrderService.Domain.Entities;

namespace HubNegocios.OrderService.Domain.Ports;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Historial del cliente, paginado y de más reciente a más antigua.</summary>
    Task<(IReadOnlyList<Order> Orders, int Total)> GetHistoryAsync(
        Guid customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Correlativo del año para el tenant. Legible para el cliente.</summary>
    Task<int> NextSequenceAsync(int year, CancellationToken cancellationToken = default);

    void Add(Order order);

    void AddStatusHistory(OrderStatusHistory history);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Lo que catalog-service confirma de un item al validar una orden.</summary>
public sealed record CatalogItemSnapshot(
    Guid ItemId,
    string Name,
    string? Sku,
    decimal Price,
    string Currency,
    bool IsActive,
    string AttributesJson);

/// <summary>
/// Puerto hacia catalog-service.
///
/// Es una llamada SÍNCRONA, y eso es una decisión con coste: mientras dure,
/// order-service depende de que catalog-service esté vivo. Se acepta porque
/// crear una orden con un precio inventado es peor que no crearla — pero por eso
/// el adaptador lleva tiempo de espera corto, reintentos y cortacircuitos.
/// </summary>
public interface ICatalogClient
{
    Task<IReadOnlyList<CatalogItemSnapshot>> GetItemsAsync(
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken = default);
}
