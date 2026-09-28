using HubNegocios.OrderService.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HubNegocios.OrderService.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id).HasColumnName("id");
        builder.Property(o => o.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(o => o.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(o => o.OrderNumber).HasColumnName("order_number").HasMaxLength(30).IsRequired();

        builder.Property(o => o.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(o => o.TotalAmount).HasColumnName("total_amount").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(o => o.Notes).HasColumnName("notes").HasMaxLength(1000);

        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at");
        builder.Property(o => o.CreatedBy).HasColumnName("created_by");
        builder.Property(o => o.UpdatedBy).HasColumnName("updated_by");

        /*
          Las líneas se cargan siempre con la orden. Una orden sin sus líneas no
          es nada útil, así que en vez de dejar que cada consulta se acuerde del
          Include —y que la que se olvide provoque un N+1 o un total a cero— se
          declara aquí una sola vez.
        */
        builder.Navigation(o => o.Items).AutoInclude();

        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // El acceso a la colección va por el campo, no por la propiedad de solo
        // lectura: así la entidad puede exponer IReadOnlyCollection sin que EF
        // necesite un setter público que rompería el encapsulamiento.
        builder.Metadata.FindNavigation(nameof(Order.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Único por tenant, no globalmente: dos negocios pueden tener cada uno
        // su ORD-2026-000001 y ninguno tiene por qué saber del otro.
        builder.HasIndex(o => new { o.TenantId, o.OrderNumber })
            .IsUnique()
            .HasDatabaseName("ux_orders_tenant_numero");

        // Índice del historial del cliente: es la consulta paginada del caso de
        // uso, ordenada por fecha descendente.
        builder.HasIndex(o => new { o.TenantId, o.CustomerId, o.CreatedAt })
            .HasDatabaseName("ix_orders_tenant_cliente_fecha")
            .IsDescending(false, false, true);
    }
}

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(i => i.OrderId).HasColumnName("order_id").IsRequired();

        // Sin clave foránea a catalog: es otra base de datos. Un JOIN entre
        // bases de dos servicios es el atajo que convierte los microservicios
        // en un monolito repartido.
        builder.Property(i => i.ItemId).HasColumnName("item_id").IsRequired();

        builder.Property(i => i.ItemNameSnapshot).HasColumnName("item_name_snapshot").HasMaxLength(200).IsRequired();
        builder.Property(i => i.ItemSkuSnapshot).HasColumnName("item_sku_snapshot").HasMaxLength(60);
        builder.Property(i => i.Quantity).HasColumnName("quantity").IsRequired();
        builder.Property(i => i.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(i => i.AttributesJson).HasColumnName("attributes").HasColumnType("jsonb").IsRequired();

        // Calculada en C#, no columna: guardarla sería duplicar un dato que ya
        // está y abrir la posibilidad de que los dos no coincidan.
        builder.Ignore(i => i.LineTotal);

        builder.HasIndex(i => i.OrderId).HasDatabaseName("ix_order_items_orden");
    }
}

public sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("order_status_history");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.Id).HasColumnName("id");
        builder.Property(h => h.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(h => h.OrderId).HasColumnName("order_id").IsRequired();

        builder.Property(h => h.OldValue).HasColumnName("old_value").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(h => h.NewValue).HasColumnName("new_value").HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(h => h.ChangedBy).HasColumnName("changed_by");
        builder.Property(h => h.ChangedAt).HasColumnName("changed_at").IsRequired();
        builder.Property(h => h.Reason).HasColumnName("reason").HasMaxLength(500);

        builder.HasIndex(h => new { h.OrderId, h.ChangedAt }).HasDatabaseName("ix_order_status_history_orden_fecha");
    }
}
