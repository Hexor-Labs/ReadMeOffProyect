using HubNegocios.OrderService.Domain.Entities;
using HubNegocios.OrderService.Domain.Ports;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace HubNegocios.OrderService.Application.UseCases;

public sealed record CancelOrderCommand(Guid OrderId, string? Reason);

/// <summary>
/// Cancela una orden y avisa para que se deshaga lo que se apartó por ella.
///
/// El evento <c>order.cancelled</c> es el que devuelve el cupo en
/// reservations-service y libera el stock reservado en inventory-service. Si se
/// perdiera, esos recursos quedarían apartados para siempre sin que nadie
/// pudiera usarlos ni supiera por qué: por eso sale por la outbox y no por una
/// llamada directa.
/// </summary>
public sealed class CancelOrderHandler(
    IOrderRepository repository,
    IOutboxWriter outbox,
    TimeProvider clock,
    ILogger<CancelOrderHandler> logger)
{
    public async Task HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await repository.GetByIdAsync(command.OrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("ORDER_NOT_FOUND", "La orden no existe.");

        if (order.Status == OrderStatus.Cancelled)
        {
            logger.LogInformation("La orden {OrderId} ya estaba cancelada", order.Id);
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;

        // Lanza ConflictException si la orden ya está pagada: a partir de ahí
        // no es cancelar, es devolver, y eso mueve dinero.
        var previous = order.Cancel();

        repository.AddStatusHistory(OrderStatusHistory.Record(
            Guid.NewGuid(),
            order.Id,
            previous,
            order.Status,
            TenantContext.Current.UserId,
            now,
            command.Reason));

        outbox.Enqueue(
            order.Id,
            nameof(Order),
            "order.cancelled",
            new
            {
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                previousStatus = previous.ToString(),
                reason = command.Reason,
                cancelledAt = now,
            });

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Orden {OrderNumber} cancelada desde {EstadoAnterior}. Motivo: {Motivo}",
            order.OrderNumber,
            previous,
            command.Reason ?? "sin especificar");
    }
}

public sealed record OrderSummary(
    Guid Id,
    string OrderNumber,
    string Status,
    string Type,
    decimal Total,
    string Currency,
    DateTime CreatedAt,
    int LineCount);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

/// <summary>Historial de órdenes de un cliente, paginado.</summary>
public sealed class GetOrderHistoryHandler(IOrderRepository repository)
{
    /// <summary>
    /// Tope duro de tamaño de página. Sin él, una petición con
    /// <c>pageSize=1000000</c> se traería la tabla entera a memoria: es una
    /// forma trivial de tumbar el servicio desde fuera.
    /// </summary>
    public const int MaxPageSize = 100;

    public async Task<PagedResult<OrderSummary>> HandleAsync(
        Guid customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, MaxPageSize);

        var (orders, total) = await repository
            .GetHistoryAsync(customerId, page, pageSize, cancellationToken)
            .ConfigureAwait(false);

        var resumenes = orders.Select(order => new OrderSummary(
            order.Id,
            order.OrderNumber,
            order.Status.ToString(),
            order.Type.ToString(),
            order.TotalAmount,
            order.Currency,
            order.CreatedAt,
            order.Items.Count)).ToList();

        return new PagedResult<OrderSummary>(resumenes, page, pageSize, total);
    }
}
