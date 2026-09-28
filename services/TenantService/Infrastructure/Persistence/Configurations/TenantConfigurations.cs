using HubNegocios.TenantService.Domain.Entities;
using HubNegocios.TenantService.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HubNegocios.TenantService.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(t => t.VerticalType).HasColumnName("vertical_type").HasMaxLength(50).IsRequired();
        builder.Property(t => t.Slug).HasColumnName("slug").HasMaxLength(Slug.MaxLength).IsRequired();
        builder.Property(t => t.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        builder.Property(t => t.Phone).HasColumnName("phone").HasMaxLength(30);
        builder.Property(t => t.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(t => t.LogoUrl).HasColumnName("logo_url").HasMaxLength(500);

        // jsonb, no text: permite consultar dentro del JSON desde SQL sin
        // tener que leer y deserializar la fila entera en la aplicación.
        builder.Property(t => t.BrandingJson).HasColumnName("branding").HasColumnType("jsonb").IsRequired();
        builder.Property(t => t.FeaturesJson).HasColumnName("features").HasColumnType("jsonb").IsRequired();

        /*
          El enum se guarda como texto, no como número. Cuesta unos bytes más y
          ahorra el problema real: un 2 en una columna no dice nada cuando
          alguien investiga una incidencia a las tres de la mañana, y reordenar
          los valores del enum en C# reinterpreta en silencio todas las filas
          ya guardadas.
        */
        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.IsWhiteLabel).HasColumnName("is_white_label").HasDefaultValue(false).IsRequired();
        builder.Property(t => t.CustomDomain).HasColumnName("custom_domain").HasMaxLength(253);
        builder.Property(t => t.Plan).HasColumnName("plan").HasMaxLength(50).IsRequired();

        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        builder.Property(t => t.CreatedBy).HasColumnName("created_by");
        builder.Property(t => t.UpdatedBy).HasColumnName("updated_by");

        // La unicidad de verdad vive aquí, no en la comprobación previa del
        // caso de uso: entre consultar y escribir cabe otra petición.
        builder.HasIndex(t => t.Slug).IsUnique().HasDatabaseName("ux_tenants_slug");

        // Único pero admitiendo varios NULL: casi ningún tenant trae dominio
        // propio, y sin el filtro el segundo NULL chocaría con el primero.
        builder.HasIndex(t => t.CustomDomain)
            .IsUnique()
            .HasFilter("custom_domain IS NOT NULL")
            .HasDatabaseName("ux_tenants_custom_domain");
    }
}

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(s => s.PlanId).HasColumnName("plan_id").HasMaxLength(50).IsRequired();

        // numeric(18,2): dinero exacto. Nunca float ni double.
        builder.Property(s => s.MonthlyCost).HasColumnName("monthly_cost").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(s => s.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();

        builder.Property(s => s.BillingCycleStart).HasColumnName("billing_cycle_start").IsRequired();
        builder.Property(s => s.BillingCycleEnd).HasColumnName("billing_cycle_end").IsRequired();
        builder.Property(s => s.RenewalDate).HasColumnName("renewal_date").IsRequired();

        builder.Property(s => s.ApiCallsUsed).HasColumnName("api_calls_used").HasDefaultValue(0L).IsRequired();
        builder.Property(s => s.ApiCallsLimit).HasColumnName("api_calls_limit").IsRequired();

        builder.Property(s => s.PaymentMethod).HasColumnName("payment_method").HasMaxLength(50);
        builder.Property(s => s.PaymentStatus)
            .HasColumnName("payment_status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(s => s.TenantId).HasDatabaseName("ix_subscriptions_tenant");

        // Cascada: si se borra el tenant, su suscripción no tiene sentido sola.
        builder
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(s => s.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TenantStatusHistoryConfiguration : IEntityTypeConfiguration<TenantStatusHistory>
{
    public void Configure(EntityTypeBuilder<TenantStatusHistory> builder)
    {
        builder.ToTable("tenant_status_history");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.Id).HasColumnName("id");
        builder.Property(h => h.TenantId).HasColumnName("tenant_id").IsRequired();

        builder.Property(h => h.OldValue).HasColumnName("old_value").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(h => h.NewValue).HasColumnName("new_value").HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(h => h.ChangedBy).HasColumnName("changed_by");
        builder.Property(h => h.ChangedAt).HasColumnName("changed_at").IsRequired();
        builder.Property(h => h.Reason).HasColumnName("reason").HasMaxLength(500);

        builder.HasIndex(h => new { h.TenantId, h.ChangedAt }).HasDatabaseName("ix_tenant_status_history_tenant_fecha");

        // Sin cascada: el historial sobrevive al tenant. Es justo cuando más
        // falta hace saber qué pasó.
        builder
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(h => h.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
