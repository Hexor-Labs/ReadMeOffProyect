using HubNegocios.InventoryService.Domain.ValueObjects;
using HubNegocios.SharedKernel.Auditing;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.InventoryService.Domain.Entities;

/// <summary>
/// El saldo de un producto en el almacén de un negocio.
///
/// Las dos cantidades son el corazón del servicio y conviene tenerlas claras
/// antes de leer cualquier caso de uso:
///
/// <list type="bullet">
///   <item><description>
///     <see cref="QuantityAvailable"/> es lo que QUEDA POR VENDER. Es el número
///     que decide si una orden entra o se rechaza.
///   </description></item>
///   <item><description>
///     <see cref="QuantityReserved"/> es lo APARTADO PERO NO COBRADO: unidades
///     que ya tienen dueño provisional y que todavía están en la estantería.
///   </description></item>
/// </list>
///
/// Son dos columnas y no una porque el pago ocurre después de la orden. Con una
/// sola cantidad habría que elegir entre descontar al crear la orden —y perder
/// la unidad si el pago falla— o descontar al cobrar, y entonces dos clientes
/// pueden pagar la última unidad. Separando disponible de reservado, apartar es
/// reversible y descontar es definitivo.
///
/// El total que hay realmente en la estantería es
/// <see cref="QuantityOnHand"/> = disponible + reservado. Reservar no lo cambia
/// (la unidad sigue ahí, solo que ya no se puede vender otra vez); confirmar sí,
/// porque ahí la unidad sale por la puerta.
/// </summary>
public sealed class StockItem : ITenantOwned, IAuditable
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>
    /// Referencia al item de catalog-service. Sin clave foránea: es otra base de
    /// datos, y un JOIN entre las tablas de dos servicios es el atajo que los
    /// convierte en un monolito repartido.
    /// </summary>
    public Guid ItemId { get; private set; }

    public int QuantityAvailable { get; private set; }
    public int QuantityReserved { get; private set; }

    /// <summary>Por debajo de aquí hay que reponer. Cero desactiva el aviso.</summary>
    public int ReorderThreshold { get; private set; }

    public string? WarehouseLocation { get; private set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    /// <summary>Lo que hay de verdad en la estantería. Calculado, no columna.</summary>
    public int QuantityOnHand => QuantityAvailable + QuantityReserved;

    /// <summary>Si toca reponer. Con umbral cero nunca avisa.</summary>
    public bool IsBelowThreshold => ReorderThreshold > 0 && QuantityAvailable < ReorderThreshold;

    private StockItem()
    {
    }

    public static StockItem Create(
        Guid id,
        Guid itemId,
        int quantityAvailable,
        int reorderThreshold,
        string? warehouseLocation)
    {
        if (quantityAvailable < 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["quantityAvailable"] = ["La cantidad inicial no puede ser negativa."],
            });
        }

        if (reorderThreshold < 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["reorderThreshold"] = ["El umbral de reposición no puede ser negativo."],
            });
        }

        return new StockItem
        {
            Id = id,
            ItemId = itemId,
            QuantityAvailable = quantityAvailable,
            QuantityReserved = 0,
            ReorderThreshold = reorderThreshold,
            WarehouseLocation = ValueObjects.WarehouseLocation.Create(warehouseLocation)?.Value,
        };
    }

    /// <summary>
    /// Aparta unidades: las saca de lo vendible y las pasa a reservado. No
    /// descuenta nada del almacén, así que se puede deshacer si el pago falla.
    /// </summary>
    /// <returns>Lo disponible ANTES del cambio, para el asiento del libro mayor.</returns>
    public int Reserve(int quantity)
    {
        RequirePositive(quantity);

        if (QuantityAvailable < quantity)
        {
            throw new ConflictException(
                "INVENTORY_INSUFFICIENT_STOCK",
                $"Solo quedan {QuantityAvailable} unidades disponibles y se piden {quantity}.");
        }

        var previous = QuantityAvailable;

        QuantityAvailable -= quantity;
        QuantityReserved += quantity;

        return previous;
    }

    /// <summary>
    /// Convierte una reserva en descuento definitivo: las unidades salen del
    /// almacén.
    ///
    /// Solo toca lo reservado. Lo disponible ya bajó al apartar, y volver a
    /// bajarlo aquí descontaría dos veces la misma venta.
    /// </summary>
    /// <returns>Lo disponible ANTES del cambio. No cambia: ver el comentario.</returns>
    public int ConfirmDeduction(int quantity)
    {
        RequirePositive(quantity);

        if (QuantityReserved < quantity)
        {
            /*
              Llegar aquí significa que se intenta cobrar más de lo que se
              apartó. Casi siempre es un evento que se procesa sin que existiera
              la reserva —order.paid antes que order.created, o una reserva
              liberada por cancelación y pagada después—. Se rechaza en vez de
              dejar el reservado en negativo: un saldo negativo no se detecta
              hasta que alguien intenta cuadrar el almacén meses después.
            */
            throw new ConflictException(
                "INVENTORY_RESERVATION_MISMATCH",
                $"Se intenta descontar {quantity} unidades pero solo hay {QuantityReserved} reservadas.");
        }

        var previous = QuantityAvailable;

        QuantityReserved -= quantity;

        return previous;
    }

    /// <summary>
    /// Devuelve a lo vendible unas unidades apartadas. Es lo que pasa cuando la
    /// orden se cancela antes de pagarse.
    /// </summary>
    /// <returns>Lo disponible ANTES del cambio.</returns>
    public int ReleaseReserved(int quantity)
    {
        RequirePositive(quantity);

        if (QuantityReserved < quantity)
        {
            throw new ConflictException(
                "INVENTORY_RESERVATION_MISMATCH",
                $"Se intenta liberar {quantity} unidades pero solo hay {QuantityReserved} reservadas.");
        }

        var previous = QuantityAvailable;

        QuantityReserved -= quantity;
        QuantityAvailable += quantity;

        return previous;
    }

    private static void RequirePositive(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["quantity"] = ["La cantidad tiene que ser mayor que cero."],
            });
        }
    }
}
