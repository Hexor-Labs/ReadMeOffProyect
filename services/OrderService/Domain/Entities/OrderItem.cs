using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.OrderService.Domain.Entities;

/// <summary>
/// Una línea de la orden.
///
/// Guarda una FOTO del producto en el momento de la compra: nombre, precio
/// unitario y atributos. No una referencia que se lea después de
/// catalog-service.
///
/// La razón es contable. Si mañana el negocio sube el precio de la botella, la
/// factura de ayer tiene que seguir diciendo lo que el cliente pagó ayer.
/// Leyendo el precio en vivo, cambiar una etiqueta reescribiría el histórico de
/// ventas, y eso además de incorrecto es ilegal en la mayoría de sitios.
///
/// Es duplicación de datos a propósito, y es la forma correcta de que dos
/// servicios no compartan tabla.
/// </summary>
public sealed class OrderItem : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }

    /// <summary>Referencia al item de catalog-service. Sin clave foránea: es otra base.</summary>
    public Guid ItemId { get; private set; }

    public string ItemNameSnapshot { get; private set; } = string.Empty;
    public string? ItemSkuSnapshot { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    /// <summary>Atributos del item al comprar: talla, color, hora del turno…</summary>
    public string AttributesJson { get; private set; } = "{}";

    public decimal LineTotal => UnitPrice * Quantity;

    private OrderItem()
    {
    }

    public static OrderItem Create(
        Guid id,
        Guid itemId,
        string itemName,
        string? itemSku,
        int quantity,
        decimal unitPrice,
        string? attributesJson)
    {
        if (quantity <= 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["quantity"] = ["La cantidad debe ser mayor que cero."],
            });
        }

        if (unitPrice < 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["unitPrice"] = ["El precio no puede ser negativo."],
            });
        }

        return new OrderItem
        {
            Id = id,
            ItemId = itemId,
            ItemNameSnapshot = itemName,
            ItemSkuSnapshot = itemSku,
            Quantity = quantity,
            UnitPrice = unitPrice,
            AttributesJson = string.IsNullOrWhiteSpace(attributesJson) ? "{}" : attributesJson,
        };
    }
}

/// <summary>Rastro de cada cambio de estado de una orden.</summary>
public sealed class OrderStatusHistory : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }

    public OrderStatus OldValue { get; private set; }
    public OrderStatus NewValue { get; private set; }

    public Guid? ChangedBy { get; private set; }
    public DateTime ChangedAt { get; private set; }
    public string? Reason { get; private set; }

    private OrderStatusHistory()
    {
    }

    public static OrderStatusHistory Record(
        Guid id,
        Guid orderId,
        OrderStatus oldValue,
        OrderStatus newValue,
        Guid? changedBy,
        DateTime changedAt,
        string? reason) => new()
        {
            Id = id,
            OrderId = orderId,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedBy = changedBy,
            ChangedAt = changedAt,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        };
}
