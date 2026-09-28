using HubNegocios.SharedKernel.Auditing;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.OrderService.Domain.Entities;

public enum OrderStatus
{
    Pending,
    Confirmed,
    Paid,
    Fulfilled,
    Cancelled,
}

public enum OrderType
{
    Purchase,
    Reservation,
    Booking,
}

/// <summary>
/// Una orden: lo que un cliente pidió a un negocio.
///
/// Es la pieza central del hub. Casi todos los capability services reaccionan a
/// ella: reservations-service consume <c>order.created</c> cuando el tipo es
/// reserva, inventory-service reserva stock, notification-service manda el
/// correo. Por eso el estado de la orden tiene que ser difícil de romper: aquí
/// no hay <c>set</c> públicos y cada transición comprueba desde dónde viene.
/// </summary>
public sealed class Order : ITenantOwned, IAuditable
{
    private readonly List<OrderItem> _items = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>Quién pidió. Es un usuario de identity-service, no una tabla local.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Correlativo legible para el cliente: «ORD-2026-000123».</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    public OrderType Type { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;

    public decimal TotalAmount { get; private set; }
    public string Currency { get; private set; } = "COP";

    public string? Notes { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    private Order()
    {
    }

    public static Order Place(
        Guid id,
        Guid customerId,
        string orderNumber,
        OrderType type,
        string currency,
        string? notes,
        IEnumerable<OrderItem> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);
        ArgumentNullException.ThrowIfNull(items);

        var order = new Order
        {
            Id = id,
            CustomerId = customerId,
            OrderNumber = orderNumber,
            Type = type,
            Currency = currency,
            Notes = notes?.Trim(),
            Status = OrderStatus.Pending,
        };

        order._items.AddRange(items);

        if (order._items.Count == 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["items"] = ["Una orden sin líneas no es una orden."],
            });
        }

        /*
          El total se calcula aquí, a partir de las líneas, y nunca se acepta
          del cliente. Si llegara en el cuerpo de la petición, cualquiera podría
          pedir tres botellas y mandar un total de mil pesos. El precio unitario
          de cada línea es el que catalog-service confirmó al validar.
        */
        order.TotalAmount = order._items.Sum(item => item.LineTotal);

        return order;
    }

    public OrderStatus Confirm() => TransitionTo(OrderStatus.Confirmed, from: [OrderStatus.Pending]);

    public OrderStatus MarkPaid() => TransitionTo(OrderStatus.Paid, from: [OrderStatus.Confirmed]);

    public OrderStatus Fulfill() => TransitionTo(OrderStatus.Fulfilled, from: [OrderStatus.Paid]);

    /// <summary>
    /// Cancela la orden. Solo desde <c>Pending</c> o <c>Confirmed</c>: una vez
    /// pagada, cancelar deja de ser un cambio de estado y pasa a ser una
    /// devolución, con su movimiento de dinero y su rastro contable. Son dos
    /// operaciones distintas y mezclarlas descuadra la contabilidad.
    /// </summary>
    public OrderStatus Cancel() =>
        TransitionTo(OrderStatus.Cancelled, from: [OrderStatus.Pending, OrderStatus.Confirmed]);

    private OrderStatus TransitionTo(OrderStatus target, OrderStatus[] from)
    {
        if (Status == target)
        {
            // Repetir la misma transición no es un error: los eventos se
            // entregan «al menos una vez» y el segundo intento debe ser inocuo.
            return Status;
        }

        if (!from.Contains(Status))
        {
            throw new ConflictException(
                "ORDER_INVALID_TRANSITION",
                $"Una orden en estado {Status} no puede pasar a {target}.");
        }

        var previous = Status;
        Status = target;
        return previous;
    }
}
