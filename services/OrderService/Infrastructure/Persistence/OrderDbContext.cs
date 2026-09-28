using HubNegocios.OrderService.Domain.Entities;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HubNegocios.OrderService.Infrastructure.Persistence;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderDbContext).Assembly);
        modelBuilder.ApplyOutbox();

        // A diferencia de tenant-service, aquí el tenant sí es el filtro de
        // todo: una orden pertenece a un negocio concreto.
        modelBuilder.ApplyTenantFilters();

        base.OnModelCreating(modelBuilder);
    }
}
