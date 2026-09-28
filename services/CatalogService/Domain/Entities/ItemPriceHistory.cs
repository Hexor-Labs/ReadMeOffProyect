using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.CatalogService.Domain.Entities;

/// <summary>
/// Rastro de cada cambio de precio de un item.
///
/// La columna <c>price_amount</c> del item dice a cuánto se vende ahora; la
/// pregunta que llega un martes por la tarde es «¿por qué este pedido de marzo
/// se cobró a otro precio y quién lo cambió?». Esa respuesta no está en la fila
/// actual, y sin historial no está en ninguna parte.
///
/// Solo se inserta. Nada actualiza ni borra estas filas: un historial que se
/// puede editar no sirve como historial.
/// </summary>
public sealed class ItemPriceHistory : ITenantOwned
{
    public Guid Id { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid TenantId { get; private set; }

    public decimal OldPrice { get; private set; }

    public decimal NewPrice { get; private set; }

    public Guid? ChangedBy { get; private set; }

    public DateTime ChangedAt { get; private set; }

    public string? Reason { get; private set; }

    private ItemPriceHistory()
    {
    }

    public static ItemPriceHistory Record(
        Guid id,
        Guid itemId,
        Guid tenantId,
        decimal oldPrice,
        decimal newPrice,
        Guid? changedBy,
        DateTime changedAt,
        string? reason) => new()
        {
            Id = id,
            ItemId = itemId,
            TenantId = tenantId,
            OldPrice = oldPrice,
            NewPrice = newPrice,
            ChangedBy = changedBy,
            ChangedAt = changedAt,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        };
}
