using HubNegocios.CatalogService.Domain.Entities;
using HubNegocios.CatalogService.Domain.Ports;
using HubNegocios.CatalogService.Domain.ValueObjects;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;

using Microsoft.Extensions.Logging;

namespace HubNegocios.CatalogService.Application.UseCases;

public sealed record CreateItemCommand(
    string Sku,
    string Name,
    string? Description,
    Guid? CategoryId,
    IReadOnlyCollection<string>? Tags,
    decimal PriceAmount,
    string PriceCurrency,
    AvailabilityMode AvailabilityMode,
    int? AvailableQuantity,
    string? ImageUrl,
    IReadOnlyCollection<string>? ImageUrls,
    string? AttributesJson);

public sealed record CreateItemResult(Guid ItemId, string Sku, decimal PriceAmount, string PriceCurrency);

/// <summary>
/// Añade un producto o servicio al catálogo del negocio.
/// </summary>
public sealed class CreateItemHandler(
    IItemRepository repository,
    IOutboxWriter outbox,
    ILogger<CreateItemHandler> logger)
{
    public async Task<CreateItemResult> HandleAsync(
        CreateItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sku = Sku.Create(command.Sku);
        var price = Money.Create(command.PriceAmount, command.PriceCurrency);

        /*
          Comprobar antes de insertar da un error legible, pero no es la
          garantía: entre esta consulta y el INSERT cabe otra petición con el
          mismo SKU. Quien garantiza la unicidad es el índice único
          (tenant_id, sku) de la tabla; esto solo evita que el caso normal acabe
          en un error 500 con un mensaje de Postgres.
        */
        if (await repository.SkuExistsAsync(sku.Value, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException(
                "ITEM_SKU_TAKEN",
                $"Ya existe un item con el SKU «{sku.Value}» en este negocio.");
        }

        var itemId = Guid.NewGuid();

        var item = Item.Create(
            itemId,
            sku,
            command.Name,
            command.Description,
            command.CategoryId,
            command.Tags,
            price,
            command.AvailabilityMode,
            command.AvailableQuantity,
            command.ImageUrl,
            command.ImageUrls,
            command.AttributesJson);

        repository.Add(item);

        /*
          El evento se encola, no se publica: se convierte en una fila más de la
          misma transacción, así que o se guardan el item y el evento, o no se
          guarda ninguno de los dos. Aquí se usa Enqueue —y no EnqueueFor— porque
          en este servicio siempre hay tenant en el contexto: lo puso el
          middleware al validar el token.
        */
        outbox.Enqueue(
            itemId,
            nameof(Item),
            "item.created",
            new
            {
                itemId,
                sku = item.Sku,
                name = item.Name,
                categoryId = item.CategoryId,
                priceAmount = item.PriceAmount,
                priceCurrency = item.PriceCurrency,
                availabilityMode = item.AvailabilityMode.ToString(),
                availableQuantity = item.AvailableQuantity,
            });

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Item {ItemId} creado con SKU {Sku}", itemId, item.Sku);

        return new CreateItemResult(itemId, item.Sku, item.PriceAmount, item.PriceCurrency);
    }
}
