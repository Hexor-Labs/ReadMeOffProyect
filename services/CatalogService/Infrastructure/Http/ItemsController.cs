using System.ComponentModel.DataAnnotations;

using HubNegocios.CatalogService.Application.UseCases;
using HubNegocios.CatalogService.Domain.Entities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HubNegocios.CatalogService.Infrastructure.Http;

/// <summary>
/// Cuerpo de alta de item.
///
/// Es un DTO y no la entidad de dominio. La diferencia no es ceremonia: si el
/// endpoint aceptara un <c>Item</c>, cualquiera podría mandar
/// <c>"isDeleted": false</c> o su propio <c>"tenantId"</c> en el JSON y el
/// binder los asignaría sin preguntar. Aquí solo entra lo que el cliente tiene
/// derecho a decidir.
/// </summary>
public sealed record CreateItemRequest(
    [property: Required, StringLength(64, MinimumLength = 2)] string Sku,
    [property: Required, StringLength(200, MinimumLength = 2)] string Name,
    [property: StringLength(2000)] string? Description,
    Guid? CategoryId,
    IReadOnlyCollection<string>? Tags,
    [property: Range(0, 9999999999999999.99)] decimal PriceAmount,
    [property: Required, StringLength(3, MinimumLength = 3)] string PriceCurrency,
    AvailabilityMode AvailabilityMode,
    [property: Range(0, int.MaxValue)] int? AvailableQuantity,
    [property: Url, StringLength(500)] string? ImageUrl,
    IReadOnlyCollection<string>? ImageUrls,
    string? AttributesJson);

public sealed record UpdateItemPriceRequest(
    [property: Range(0, 9999999999999999.99)] decimal NewPrice,
    [property: StringLength(500)] string? Reason);

public sealed record UpdateItemAvailabilityRequest(
    AvailabilityMode AvailabilityMode,
    [property: Range(0, int.MaxValue)] int? AvailableQuantity);

/// <summary>Cuerpo de la consulta por lote que hace order-service.</summary>
public sealed record GetItemsByIdsRequest([property: Required] IReadOnlyCollection<Guid> ItemIds);

[ApiController]
[Route("api/items")]
public sealed class ItemsController(
    CreateItemHandler createItem,
    UpdateItemPriceHandler updateItemPrice,
    UpdateItemAvailabilityHandler updateItemAvailability,
    SoftDeleteItemHandler softDeleteItem,
    SearchItemsHandler searchItems,
    GetItemsByIdsHandler getItemsByIds) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType<CreateItemResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateItemRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await createItem.HandleAsync(
            new CreateItemCommand(
                request.Sku,
                request.Name,
                request.Description,
                request.CategoryId,
                request.Tags,
                request.PriceAmount,
                request.PriceCurrency,
                request.AvailabilityMode,
                request.AvailableQuantity,
                request.ImageUrl,
                request.ImageUrls,
                request.AttributesJson),
            cancellationToken).ConfigureAwait(false);

        // Sin cabecera Location: la especificación de este servicio no incluye
        // un GET por id, y apuntar a una ruta que no existe es peor que no
        // apuntar a ninguna.
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPatch("{itemId:guid}/price")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePriceAsync(
        Guid itemId,
        [FromBody] UpdateItemPriceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await updateItemPrice.HandleAsync(
            new UpdateItemPriceCommand(itemId, request.NewPrice, request.Reason),
            cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    [HttpPatch("{itemId:guid}/availability")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAvailabilityAsync(
        Guid itemId,
        [FromBody] UpdateItemAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await updateItemAvailability.HandleAsync(
            new UpdateItemAvailabilityCommand(itemId, request.AvailabilityMode, request.AvailableQuantity),
            cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// Retira el item del catálogo. Es un borrado lógico: la fila se queda.
    /// </summary>
    [HttpDelete("{itemId:guid}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SoftDeleteAsync(
        Guid itemId,
        [FromQuery, StringLength(500)] string? reason,
        CancellationToken cancellationToken)
    {
        await softDeleteItem.HandleAsync(
            new SoftDeleteItemCommand(itemId, reason),
            cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// Busca en el catálogo del negocio. El tamaño de página se recorta al tope
    /// del servicio, así que la respuesta dice con qué tamaño se sirvió de
    /// verdad.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<SearchItemsResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] Guid? categoryId,
        [FromQuery] string[]? tags,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var result = await searchItems.HandleAsync(
            new SearchItemsQuery(categoryId, tags, minPrice, maxPrice, page, pageSize),
            cancellationToken).ConfigureAwait(false);

        return Ok(result);
    }

    /// <summary>
    /// Devuelve varios items por id. La llama order-service mientras crea una
    /// orden, para comprobar que las líneas existen y quedarse con la foto del
    /// precio y los atributos.
    ///
    /// Es POST y no GET aunque sea una lectura: doscientos UUID en la cadena de
    /// consulta chocan con los límites de longitud de URL de proxies y
    /// servidores, y además acabarían en los logs de acceso de todo el camino.
    /// </summary>
    [HttpPost("by-ids")]
    [ProducesResponseType<GetItemsByIdsResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetByIdsAsync(
        [FromBody] GetItemsByIdsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await getItemsByIds
            .HandleAsync(request.ItemIds, cancellationToken)
            .ConfigureAwait(false);

        return Ok(result);
    }
}
