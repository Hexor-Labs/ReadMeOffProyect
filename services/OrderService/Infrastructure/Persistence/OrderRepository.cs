using HubNegocios.OrderService.Domain.Entities;
using HubNegocios.OrderService.Domain.Ports;

using Microsoft.EntityFrameworkCore;

namespace HubNegocios.OrderService.Infrastructure.Persistence;

public sealed class OrderRepository(OrderDbContext context) : IOrderRepository
{
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Order> Orders, int Total)> GetHistoryAsync(
        Guid customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.Orders.AsNoTracking().Where(o => o.CustomerId == customerId);

        // Se cuenta antes de paginar. Dos viajes a la base, pero es la única
        // forma de decirle al cliente cuántas páginas hay.
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (orders, total);
    }

    /// <summary>
    /// Siguiente correlativo del año para este tenant.
    ///
    /// Cuenta las órdenes del año y suma uno. Es suficiente para un correlativo
    /// legible y no para un identificador: dos órdenes creadas en el mismo
    /// instante pueden pedir el mismo número. Quien impide que se guarden las
    /// dos es el índice único (tenant_id, order_number); la segunda falla y se
    /// reintenta.
    ///
    /// Si el volumen hace que esa colisión deje de ser rara, lo que toca es una
    /// secuencia de Postgres por tenant y año, no un bloqueo aquí.
    /// </summary>
    public async Task<int> NextSequenceAsync(int year, CancellationToken cancellationToken = default)
    {
        var desde = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var hasta = desde.AddYears(1);

        var count = await context.Orders
            .AsNoTracking()
            .Where(o => o.CreatedAt >= desde && o.CreatedAt < hasta)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        return count + 1;
    }

    public void Add(Order order) => context.Orders.Add(order);

    public void AddStatusHistory(OrderStatusHistory history) => context.OrderStatusHistories.Add(history);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
