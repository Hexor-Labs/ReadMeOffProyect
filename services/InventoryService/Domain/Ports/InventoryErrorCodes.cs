namespace HubNegocios.InventoryService.Domain.Ports;

/// <summary>
/// Los códigos de error que cruzan la frontera entre el adaptador de
/// persistencia y la aplicación.
///
/// El resto de códigos viven donde se lanzan, pero estos no pueden: los produce
/// el repositorio al traducir una violación de índice único y los interpreta el
/// caso de uso para decidir si un evento ya estaba procesado. Escritos como
/// cadenas sueltas en dos archivos, el día que alguien corrija uno el otro deja
/// de reconocerlo y la idempotencia se pierde sin que falle ninguna compilación.
/// </summary>
public static class InventoryErrorCodes
{
    /// <summary>Choca el índice único de (tenant, orden): ese pago ya se descontó.</summary>
    public const string PaymentAlreadyProcessed = "INVENTORY_PAYMENT_ALREADY_PROCESSED";

    /// <summary>No hay saldo de ese producto en el almacén de este negocio.</summary>
    public const string StockItemNotFound = "INVENTORY_STOCK_ITEM_NOT_FOUND";

    /// <summary>No quedan unidades suficientes para apartar lo que se pide.</summary>
    public const string InsufficientStock = "INVENTORY_INSUFFICIENT_STOCK";

    /// <summary>Se intenta descontar o liberar más de lo que hay reservado.</summary>
    public const string ReservationMismatch = "INVENTORY_RESERVATION_MISMATCH";
}
