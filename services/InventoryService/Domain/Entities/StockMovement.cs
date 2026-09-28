using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.InventoryService.Domain.Entities;

public enum MovementType
{
    /// <summary>Entrada de mercancía comprada al proveedor.</summary>
    Purchase,

    /// <summary>Salida por venta: apartar unidades para una orden y consolidarlas al cobrar.</summary>
    Sale,

    /// <summary>Corrección manual tras un conteo físico.</summary>
    Adjustment,

    /// <summary>Vuelta de unidades a lo vendible: devolución del cliente u orden cancelada.</summary>
    Return,
}

/// <summary>
/// Un asiento del libro mayor del almacén.
///
/// Cada operación sobre un <see cref="StockItem"/> deja una fila aquí, y esa es
/// la diferencia entre un inventario que se puede auditar y un número que un día
/// no cuadra sin que nadie sepa por qué. El saldo actual del
/// <see cref="StockItem"/> se puede reconstruir sumando estos asientos; si no
/// coincide, es que alguien escribió por fuera.
///
/// <see cref="PreviousQuantity"/> y <see cref="NewQuantity"/> son el saldo de
/// <see cref="StockItem.QuantityAvailable"/> antes y después. Se guarda el SALDO
/// y no solo el delta a propósito: con el saldo, el
/// <see cref="NewQuantity"/> de un asiento tiene que ser el
/// <see cref="PreviousQuantity"/> del siguiente del mismo item, y cualquier
/// salto en esa cadena señala exactamente dónde se perdió la pista. Con solo el
/// delta, el descuadre se ve pero no se localiza.
///
/// El asiento de una venta CONSOLIDADA —cuando llega <c>order.paid</c>— lleva
/// <see cref="PreviousQuantity"/> igual a <see cref="NewQuantity"/>, y eso es
/// correcto: lo vendible ya bajó cuando se apartó la unidad, y aquí lo que
/// cambia es lo reservado. La fila existe igualmente porque el hecho «esta venta
/// se cobró» es justo el que hay que poder demostrar.
///
/// Los asientos no se modifican ni se borran nunca. Un error se corrige con otro
/// asiento, como en la contabilidad de verdad.
/// </summary>
public sealed class StockMovement : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid StockItemId { get; private set; }

    public MovementType MovementType { get; private set; }

    /// <summary>Unidades que mueve el asiento. Siempre positivo: el sentido lo da el tipo.</summary>
    public int Quantity { get; private set; }

    public int PreviousQuantity { get; private set; }
    public int NewQuantity { get; private set; }

    /// <summary>Por qué. Lo que se lee cuando hay que explicar un descuadre.</summary>
    public string? Reason { get; private set; }

    /// <summary>
    /// Quién lo provocó. Nulo cuando lo provoca un evento y no una persona: el
    /// consumidor de <c>order.paid</c> corre sin usuario en contexto.
    /// </summary>
    public Guid? PerformedBy { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private StockMovement()
    {
    }

    public static StockMovement Record(
        Guid id,
        Guid stockItemId,
        MovementType movementType,
        int quantity,
        int previousQuantity,
        int newQuantity,
        string? reason,
        Guid? performedBy,
        DateTime createdAt)
    {
        if (quantity <= 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["quantity"] = ["Un asiento de cero unidades no es un movimiento."],
            });
        }

        return new StockMovement
        {
            Id = id,
            StockItemId = stockItemId,
            MovementType = movementType,
            Quantity = quantity,
            PreviousQuantity = previousQuantity,
            NewQuantity = newQuantity,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            PerformedBy = performedBy,
            CreatedAt = createdAt,
        };
    }
}
