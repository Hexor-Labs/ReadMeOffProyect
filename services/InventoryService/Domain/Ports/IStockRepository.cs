using HubNegocios.InventoryService.Domain.Entities;

namespace HubNegocios.InventoryService.Domain.Ports;

/// <summary>
/// Una transacción de base de datos vista desde el dominio.
///
/// El puerto existe para que <c>ReserveStockHandler</c> —donde vive la regla de
/// «no vender unidades que no hay»— pueda abrir y confirmar una transacción sin
/// conocer EF Core, y para que las pruebas puedan comprobar que la abrió de
/// verdad. Es deliberadamente mínimo: confirmar, deshacer y cerrar.
///
/// Cerrar sin confirmar deshace. Eso es lo que hace que un <c>await using</c>
/// alrededor del caso de uso sea suficiente: cualquier excepción sale con la
/// transacción abortada y sin stock apartado a medias.
/// </summary>
public interface ITransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Un saldo por debajo de su umbral, tal y como lo devuelve el barrido
/// periódico. Lleva el tenant porque el barrido cruza todos los negocios y el
/// evento hay que emitirlo al que corresponde.
/// </summary>
public sealed record LowStockSnapshot(
    Guid TenantId,
    Guid StockItemId,
    Guid ItemId,
    int QuantityAvailable,
    int ReorderThreshold,
    string? WarehouseLocation);

/// <summary>
/// Puerto de salida hacia el almacenamiento del inventario.
///
/// Vive en el dominio y lo implementa la infraestructura: por eso los casos de
/// uso —incluido el crítico— se prueban sin Postgres y sin simular EF Core.
///
/// Los métodos <c>…ForUpdateAsync</c> son la excepción interesante: piden
/// explícitamente un bloqueo de fila. El nombre lo dice porque no es un detalle
/// del adaptador, es una decisión del caso de uso: quien llama necesita saber
/// que está serializando accesos, y por qué.
/// </summary>
public interface IStockRepository
{
    /// <summary>Abre una transacción explícita. Solo hace falta cuando hay bloqueo o dos escrituras.</summary>
    Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lee el saldo de un producto bloqueando su fila hasta el final de la
    /// transacción (<c>SELECT … FOR UPDATE</c>).
    ///
    /// Es la pieza que impide la sobreventa: dos órdenes que pidan el mismo
    /// producto a la vez se ponen en fila aquí, y la segunda no ve el disponible
    /// hasta que la primera ha terminado de apartarlo.
    /// </summary>
    Task<StockItem?> LockStockItemForUpdateAsync(
        Guid itemId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Lectura sin bloqueo, para consultas y para la comprobación de idempotencia.</summary>
    Task<StockItem?> GetByItemIdAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Si el pago de esa orden ya se descontó. La garantía real es el índice único.</summary>
    Task<bool> PaymentAlreadyProcessedAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saldos por debajo de su umbral, de TODOS los tenants.
    ///
    /// Lo llama el barrido periódico, que corre sin tenant en contexto porque su
    /// trabajo es precisamente mirar todos. Por eso el adaptador tiene que
    /// saltarse el filtro global, y por eso esta firma devuelve el tenant en cada
    /// fila: quien emita el evento necesita saber de quién es.
    /// </summary>
    Task<IReadOnlyList<LowStockSnapshot>> GetBelowThresholdAsync(CancellationToken cancellationToken = default);

    void AddStockItem(StockItem stockItem);

    void AddMovement(StockMovement movement);

    void AddProcessedPayment(ProcessedOrderPayment processedPayment);

    /// <summary>
    /// Confirma los cambios pendientes.
    ///
    /// No hay <c>Update</c>: lo que sale del repositorio viene rastreado, así que
    /// basta con modificarlo y guardar.
    ///
    /// El adaptador traduce las violaciones de índice único a
    /// <c>ConflictException</c> con código de negocio. Es importante: así el
    /// caso de uso puede distinguir «este pago ya estaba procesado» de un fallo
    /// cualquiera sin conocer los códigos de error de PostgreSQL.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
