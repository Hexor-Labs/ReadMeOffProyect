using HubNegocios.SharedKernel.Auditing;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.CheckoutService.Domain.Entities;

public enum CartStatus
{
    Active,
    Abandoned,
    Converted,
}

/// <summary>
/// El carrito de un cliente: lo que tiene elegido pero todavía no ha pagado.
///
/// A diferencia de una orden, un carrito es un borrador y se espera que casi
/// todos mueran sin convertirse. De ahí las dos cosas que lo definen:
/// <see cref="ExpiresAt"/>, porque un carrito abierto para siempre no es un
/// carrito sino basura acumulada, y el paso a <see cref="CartStatus.Converted"/>,
/// que es de ida y no tiene vuelta: una vez hay orden, el carrito deja de ser
/// editable porque editarlo cambiaría algo que ya se cobró.
/// </summary>
public sealed class Cart : ITenantOwned, IAuditable
{
    private readonly List<CartItem> _items = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>Quién compra. Es un usuario de identity-service, no una tabla local.</summary>
    public Guid CustomerId { get; private set; }

    public CartStatus Status { get; private set; } = CartStatus.Active;

    /// <summary>
    /// Cuándo se da el carrito por abandonado. Lo renueva cada cambio: mientras
    /// el cliente siga tocándolo sigue vivo, y solo se apaga si deja de volver.
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// La orden que salió de este carrito, o nulo si todavía no se convirtió.
    ///
    /// No está en la especificación de la entidad y está aquí a propósito: es la
    /// pieza que hace idempotente el checkout. Guardar el id de la orden EN el
    /// carrito, en la misma transacción que lo marca convertido, convierte el
    /// «no crear dos órdenes» en una consulta trivial —¿ya tiene orden?— en vez
    /// de en una tabla aparte de claves de idempotencia que habría que limpiar.
    /// </summary>
    public Guid? ConvertedOrderId { get; private set; }

    /// <summary>Correlativo legible de esa orden, para poder devolverlo en el reintento.</summary>
    public string? ConvertedOrderNumber { get; private set; }

    public IReadOnlyCollection<CartItem> Items => _items;

    /// <summary>
    /// Suma de las líneas con los precios GUARDADOS al añadirlas.
    ///
    /// Es lo que se le muestra al cliente, no necesariamente lo que acabará
    /// pagando: quien fija el precio de verdad es catalog-service a través de
    /// order-service cuando se crea la orden. Ver <c>CheckoutHandler</c>.
    /// </summary>
    public decimal Subtotal => _items.Sum(item => item.LineTotal);

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    private Cart()
    {
    }

    public static Cart Start(Guid id, Guid customerId, DateTime expiresAt) => new()
    {
        Id = id,
        CustomerId = customerId,
        Status = CartStatus.Active,
        ExpiresAt = expiresAt,
    };

    /// <summary>
    /// Añade un item, o suma cantidad si ya estaba.
    ///
    /// Si ya estaba NO se actualiza el precio guardado: el cliente vio ese
    /// precio cuando lo metió y la segunda unidad no tiene por qué costarle
    /// distinto a mitad de la compra.
    /// </summary>
    public CartItem AddItem(Guid lineId, Guid itemId, int quantity, decimal unitPriceSnapshot, DateTime expiresAt)
    {
        RequireEditable();

        var existente = _items.Find(item => item.ItemId == itemId);

        if (existente is not null)
        {
            existente.Increase(quantity);
            Renew(expiresAt);
            return existente;
        }

        var linea = CartItem.Create(lineId, Id, itemId, quantity, unitPriceSnapshot);
        _items.Add(linea);
        Renew(expiresAt);

        return linea;
    }

    public void RemoveItem(Guid itemId, DateTime expiresAt)
    {
        RequireEditable();

        var linea = _items.Find(item => item.ItemId == itemId)
            ?? throw new NotFoundException("CART_ITEM_NOT_FOUND", "Ese producto no está en el carrito.");

        _items.Remove(linea);
        Renew(expiresAt);
    }

    public void UpdateQuantity(Guid itemId, int quantity, DateTime expiresAt)
    {
        RequireEditable();

        var linea = _items.Find(item => item.ItemId == itemId)
            ?? throw new NotFoundException("CART_ITEM_NOT_FOUND", "Ese producto no está en el carrito.");

        linea.ChangeQuantity(quantity);
        Renew(expiresAt);
    }

    /// <summary>
    /// Da el carrito por abandonado. Repetirlo no es un error: el barrido corre
    /// cada pocos minutos y puede volver a ver la misma fila.
    /// </summary>
    /// <returns><c>true</c> si esta llamada es la que lo abandonó.</returns>
    public bool Abandon()
    {
        if (Status != CartStatus.Active)
        {
            return false;
        }

        Status = CartStatus.Abandoned;
        return true;
    }

    /// <summary>
    /// Marca el carrito como convertido en una orden y lo vacía.
    ///
    /// Es IDEMPOTENTE, y ahí está la mitad de la respuesta al doble clic: si el
    /// carrito ya tiene orden, devuelve la que tiene y no cambia nada. La otra
    /// mitad está en <c>CheckoutHandler</c>, que manda a order-service una clave
    /// de idempotencia derivada del carrito para que tampoco se duplique cuando
    /// las dos peticiones corren a la vez y ninguna ha llegado a guardar.
    /// </summary>
    /// <returns>La orden que ya existía, o nulo si esta llamada es la que convierte.</returns>
    public Guid? Convert(Guid orderId, string orderNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);

        if (Status == CartStatus.Converted)
        {
            return ConvertedOrderId;
        }

        if (_items.Count == 0)
        {
            throw new ConflictException("CART_EMPTY", "No se puede pagar un carrito vacío.");
        }

        Status = CartStatus.Converted;
        ConvertedOrderId = orderId;
        ConvertedOrderNumber = orderNumber;

        /*
          Se vacía al convertir. Las líneas ya están copiadas en la orden, que es
          el documento que manda a partir de ahora; dejarlas aquí sería tener el
          mismo dato en dos sitios y que el día que alguien «corrija» el carrito
          la orden diga otra cosa.
        */
        _items.Clear();

        return null;
    }

    /// <summary>Alarga la vida del carrito. Cada interacción del cliente cuenta.</summary>
    private void Renew(DateTime expiresAt) => ExpiresAt = expiresAt;

    private void RequireEditable()
    {
        if (Status == CartStatus.Converted)
        {
            throw new ConflictException(
                "CART_ALREADY_CONVERTED",
                "Este carrito ya se pagó. Empieza uno nuevo.");
        }

        /*
          Un carrito abandonado SÍ se puede seguir editando, y es deliberado:
          «abandonado» es un estado de marketing, no un candado. El correo de
          recuperación que manda notification-service devuelve al cliente a este
          mismo carrito, y sería absurdo que al volver se lo encontrara
          bloqueado. Al tocarlo vuelve a estar activo y se renueva su expiración.
        */
        if (Status == CartStatus.Abandoned)
        {
            Status = CartStatus.Active;
        }
    }
}
