using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.InventoryService.Domain.Entities;

/// <summary>
/// Marca de que el pago de una orden ya se descontó del almacén.
///
/// Existe por una sola razón: la outbox entrega «al menos una vez». Si el
/// publicador muere entre enviar <c>order.paid</c> y marcar la fila como
/// publicada, el evento se reenvía; y descontar dos veces la misma venta deja el
/// almacén corto de unidades que nunca salieron, que es un error que solo se
/// descubre en el conteo físico y ya no se puede reconstruir.
///
/// La garantía NO es la consulta previa del caso de uso —entre consultar y
/// escribir cabe otra transacción, igual que en cualquier otra carrera—, sino el
/// ÍNDICE ÚNICO sobre (tenant, orden). La consulta solo evita que el caso normal
/// acabe en un error; quien de verdad impide el doble descuento es la base de
/// datos, y el adaptador traduce la violación del índice a
/// <c>ConflictException</c> con
/// <see cref="Ports.InventoryErrorCodes.PaymentAlreadyProcessed"/> para que el
/// consumidor sepa que puede dar el evento por bueno.
/// </summary>
public sealed class ProcessedOrderPayment : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>La orden pagada. Es la clave de idempotencia dentro del tenant.</summary>
    public Guid OrderId { get; private set; }

    public DateTime ProcessedAt { get; private set; }

    private ProcessedOrderPayment()
    {
    }

    public static ProcessedOrderPayment Create(Guid id, Guid orderId, DateTime processedAt) => new()
    {
        Id = id,
        OrderId = orderId,
        ProcessedAt = processedAt,
    };
}
