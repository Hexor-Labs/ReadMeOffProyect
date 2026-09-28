using HubNegocios.CatalogService.Domain.Entities;
using HubNegocios.CatalogService.Domain.Ports;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;

using Microsoft.Extensions.Logging;

namespace HubNegocios.CatalogService.Application.UseCases;

public sealed record UpdateItemAvailabilityCommand(
    Guid ItemId,
    AvailabilityMode AvailabilityMode,
    int? AvailableQuantity);

/// <summary>
/// Ajusta cómo y cuánto hay disponible de un item.
///
/// El evento que sale de aquí no es informativo: order-service decide con él si
/// puede seguir aceptando pedidos de este item sin volver a preguntar. Si el
/// cambio se guardara y el aviso se perdiera, se venderían plazas que ya no
/// existen.
/// </summary>
public sealed class UpdateItemAvailabilityHandler(
    IItemRepository repository,
    IOutboxWriter outbox,
    ILogger<UpdateItemAvailabilityHandler> logger)
{
    public async Task HandleAsync(
        UpdateItemAvailabilityCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await repository.GetByIdAsync(command.ItemId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("ITEM_NOT_FOUND", "El item no existe.");

        item.ChangeAvailability(command.AvailabilityMode, command.AvailableQuantity);

        outbox.Enqueue(
            item.Id,
            nameof(Item),
            "item.availability_changed",
            new
            {
                itemId = item.Id,
                sku = item.Sku,
                availabilityMode = item.AvailabilityMode.ToString(),
                availableQuantity = item.AvailableQuantity,
            });

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Disponibilidad del item {ItemId} ahora es {Modo} con {Cantidad} unidades",
            item.Id,
            item.AvailabilityMode,
            item.AvailableQuantity?.ToString() ?? "ilimitadas");
    }
}
