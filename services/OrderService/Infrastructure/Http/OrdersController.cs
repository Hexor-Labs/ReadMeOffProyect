using System.ComponentModel.DataAnnotations;

using HubNegocios.OrderService.Application.UseCases;
using HubNegocios.OrderService.Domain.Entities;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Tenancy;

using Microsoft.AspNetCore.Mvc;

namespace HubNegocios.OrderService.Infrastructure.Http;

public sealed record OrderLineDto(
    [property: Required] Guid ItemId,
    [property: Range(1, 1000)] int Quantity);

/// <summary>
/// Cuerpo de creación de orden.
///
/// Fíjate en lo que NO está: ni total, ni precio unitario, ni estado. El total
/// se calcula a partir de lo que confirme catalog-service, y el estado lo decide
/// el dominio. Aceptarlos aquí permitiría pedir tres botellas por mil pesos.
/// </summary>
public sealed record CreateOrderRequest(
    [property: Required] Guid CustomerId,
    [property: Required] OrderType Type,
    [property: StringLength(1000)] string? Notes,
    [property: Required, MinLength(1)] IReadOnlyList<OrderLineDto> Lines);

public sealed record CancelOrderRequest([property: StringLength(500)] string? Reason);

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(
    CreateOrderHandler createOrder,
    CancelOrderHandler cancelOrder,
    GetOrderHistoryHandler orderHistory) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CreateOrderResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await createOrder.HandleAsync(
            new CreateOrderCommand(
                request.CustomerId,
                request.Type,
                request.Notes,
                request.Lines.Select(line => new OrderLineRequest(line.ItemId, line.Quantity)).ToList()),
            cancellationToken).ConfigureAwait(false);

        return CreatedAtAction(nameof(GetHistoryAsync), new { customerId = request.CustomerId }, result);
    }

    [HttpPost("{orderId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelAsync(
        Guid orderId,
        [FromBody] CancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        await cancelOrder.HandleAsync(new CancelOrderCommand(orderId, request?.Reason), cancellationToken)
            .ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// Historial de órdenes de un cliente.
    ///
    /// Un cliente solo puede ver el suyo. Sin esta comprobación, cambiar el id
    /// de la URL enseñaría las compras de cualquier otro cliente del mismo
    /// negocio: el filtro de tenant aísla entre negocios, pero dentro de uno no
    /// distingue a un cliente de otro.
    /// </summary>
    [HttpGet("history/{customerId:guid}")]
    [ProducesResponseType<PagedResult<OrderSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistoryAsync(
        Guid customerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var identity = TenantContext.Current;
        var esPersonal = identity.UserId == customerId;
        var esDelNegocio = identity.Role is "Owner" or "Admin" or "Staff";

        if (!esPersonal && !esDelNegocio)
        {
            throw new NotFoundException("ORDER_HISTORY_NOT_FOUND", "No hay historial para ese cliente.");
        }

        var result = await orderHistory.HandleAsync(customerId, page, pageSize, cancellationToken)
            .ConfigureAwait(false);

        return Ok(result);
    }
}
