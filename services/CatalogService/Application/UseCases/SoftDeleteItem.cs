using HubNegocios.CatalogService.Domain.Entities;
using HubNegocios.CatalogService.Domain.Ports;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace HubNegocios.CatalogService.Application.UseCases;

public sealed record SoftDeleteItemCommand(Guid ItemId, string? Reason);

/// <summary>
/// Retira un item del catálogo sin borrar la fila.
///
/// Nunca se ejecuta un DELETE: hay órdenes, facturas y estadísticas que apuntan
/// a items que el negocio dejó de vender, y borrar la fila las convertiría en
/// recibos sin producto. El único cambio es <c>is_deleted = true</c>, y a partir
/// de ese momento el filtro global del contexto hace que el item no aparezca en
/// ninguna consulta del servicio.
///
/// Consecuencia de ese filtro: repetir el borrado responde 404, porque para
/// quien pregunta el item ya no existe. Se acepta a cambio de la garantía que
/// de verdad importa —que la fila sigue ahí— y es el mismo criterio que sigue
/// tenant-service al responder igual a un tenant eliminado y a uno inexistente.
/// </summary>
public sealed class SoftDeleteItemHandler(
    IItemRepository repository,
    IOutboxWriter outbox,
    TimeProvider clock,
    ILogger<SoftDeleteItemHandler> logger)
{
    public async Task HandleAsync(SoftDeleteItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await repository.GetByIdAsync(command.ItemId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("ITEM_NOT_FOUND", "El item no existe.");

        item.SoftDelete();

        outbox.Enqueue(
            item.Id,
            nameof(Item),
            "item.deleted",
            new
            {
                itemId = item.Id,
                sku = item.Sku,
                reason = command.Reason,
                deletedAt = clock.GetUtcNow().UtcDateTime,
                deletedBy = TenantContext.Current.UserId,
            });

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Item {ItemId} marcado como borrado. Motivo: {Motivo}",
            item.Id,
            command.Reason ?? "sin especificar");
    }
}
