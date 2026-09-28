using HubNegocios.CatalogService.Domain.Entities;
using HubNegocios.CatalogService.Domain.Ports;
using HubNegocios.CatalogService.Domain.ValueObjects;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace HubNegocios.CatalogService.Application.UseCases;

public sealed record UpdateItemPriceCommand(Guid ItemId, decimal NewPrice, string? Reason);

/// <summary>
/// Cambia el precio de un item, deja el rastro en el historial y avisa al resto
/// del sistema.
///
/// Las tres cosas son una sola operación y por eso comparten transacción. Si el
/// precio cambiara sin historial, nadie podría explicar después por qué una
/// orden vieja se cobró a otro importe; y si el evento se perdiera, los read
/// models de los demás servicios seguirían ofreciendo el precio antiguo.
/// </summary>
public sealed class UpdateItemPriceHandler(
    IItemRepository repository,
    IOutboxWriter outbox,
    TimeProvider clock,
    ILogger<UpdateItemPriceHandler> logger)
{
    public async Task HandleAsync(UpdateItemPriceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await repository.GetByIdAsync(command.ItemId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("ITEM_NOT_FOUND", "El item no existe.");

        // La moneda no se toca aquí: cambiarla sería otra operación. Se
        // reutiliza la del item para que Money valide el importe igual que en el
        // alta.
        var nuevoPrecio = Money.Create(command.NewPrice, item.PriceCurrency);

        if (nuevoPrecio.Amount == item.PriceAmount)
        {
            // Idempotente a propósito: reenviar el mismo precio no es un error.
            // Tratarlo como tal obligaría a quien llama a consultar el precio
            // antes de actuar, que es una carrera en sí misma. Y, sobre todo,
            // no se ensucia el historial con filas que no cambian nada.
            logger.LogInformation(
                "El item {ItemId} ya estaba a {Precio}; no se hace nada",
                item.Id,
                nuevoPrecio.Amount);
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var anterior = item.ChangePrice(nuevoPrecio);

        repository.AddPriceHistory(ItemPriceHistory.Record(
            Guid.NewGuid(),
            item.Id,
            // El tenant se toma del item, no del contexto: es el mismo valor,
            // pero así la fila del historial no puede acabar en otro tenant ni
            // aunque alguien llame a este caso de uso desde un trabajo de fondo.
            item.TenantId,
            anterior,
            item.PriceAmount,
            TenantContext.Current.UserId,
            now,
            command.Reason));

        outbox.Enqueue(
            item.Id,
            nameof(Item),
            "item.price_changed",
            new
            {
                itemId = item.Id,
                sku = item.Sku,
                oldPrice = anterior,
                newPrice = item.PriceAmount,
                currency = item.PriceCurrency,
                reason = command.Reason,
                changedAt = now,
            });

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Precio del item {ItemId} cambiado de {PrecioAnterior} a {PrecioNuevo} {Moneda}. Motivo: {Motivo}",
            item.Id,
            anterior,
            item.PriceAmount,
            item.PriceCurrency,
            command.Reason ?? "sin especificar");
    }
}
