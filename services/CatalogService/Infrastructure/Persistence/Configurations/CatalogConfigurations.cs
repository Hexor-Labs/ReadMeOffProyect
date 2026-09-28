using HubNegocios.CatalogService.Domain.Entities;
using HubNegocios.CatalogService.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HubNegocios.CatalogService.Infrastructure.Persistence.Configurations;

public sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("items");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(i => i.Sku).HasColumnName("sku").HasMaxLength(Sku.MaxLength).IsRequired();
        builder.Property(i => i.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(i => i.Description).HasColumnName("description").HasMaxLength(2000);
        builder.Property(i => i.CategoryId).HasColumnName("category_id");

        /*
          text[] nativo de Postgres, que es lo que Npgsql usa para un string[].
          La alternativa habitual —una cadena con comas— obliga a leer todas las
          filas y partirlas en la aplicación para filtrar por etiqueta, y no hay
          índice que ayude con eso.
        */
        builder.Property(i => i.Tags).HasColumnName("tags").HasColumnType("text[]").IsRequired();
        builder.Property(i => i.ImageUrls).HasColumnName("image_urls").HasColumnType("text[]").IsRequired();

        // numeric(18,2): dinero exacto. Nunca float ni double.
        builder.Property(i => i.PriceAmount).HasColumnName("price_amount").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(i => i.PriceCurrency).HasColumnName("price_currency").HasMaxLength(3).IsRequired();

        /*
          El enum se guarda como texto, no como número. Cuesta unos bytes más y
          ahorra el problema real: un 2 en una columna no dice nada cuando
          alguien investiga una incidencia a las tres de la mañana, y reordenar
          los valores del enum en C# reinterpreta en silencio todas las filas ya
          guardadas.
        */
        builder.Property(i => i.AvailabilityMode)
            .HasColumnName("availability_mode")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(i => i.AvailableQuantity).HasColumnName("available_quantity");
        builder.Property(i => i.ImageUrl).HasColumnName("image_url").HasMaxLength(500);

        // jsonb, no text: permite consultar dentro del JSON desde SQL sin tener
        // que leer y deserializar la fila entera en la aplicación.
        builder.Property(i => i.AttributesJson).HasColumnName("attributes").HasColumnType("jsonb").IsRequired();

        builder.Property(i => i.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(i => i.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false).IsRequired();

        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at");
        builder.Property(i => i.CreatedBy).HasColumnName("created_by");
        builder.Property(i => i.UpdatedBy).HasColumnName("updated_by");

        /*
          Aquí NO se llama a HasQueryFilter, aunque sería el sitio natural.

          EF Core solo admite un filtro global por entidad y el último gana:
          cualquier filtro puesto en esta clase lo pisaría después
          ApplyTenantFilters(). El filtro único que combina tenant y borrado
          lógico se aplica en CatalogDbContext.OnModelCreating, detrás de esa
          llamada, y está explicado allí.
        */

        /*
          El SKU es único DENTRO del tenant, no en toda la tabla: dos negocios
          distintos pueden vender su «CAFE-01» sin enterarse el uno del otro.

          El filtro por is_deleted es lo que permite que un negocio vuelva a dar
          de alta un producto que retiró: como la fila borrada no desaparece
          nunca, sin el filtro su SKU quedaría bloqueado para siempre.
        */
        builder.HasIndex(i => new { i.TenantId, i.Sku })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_items_tenant_sku");

        // Los dos filtros más frecuentes de la búsqueda. Van con tenant_id
        // delante porque toda consulta del servicio lleva el tenant puesto por
        // el filtro global: un índice que no empiece por ahí no se usa.
        builder.HasIndex(i => new { i.TenantId, i.CategoryId }).HasDatabaseName("ix_items_tenant_categoria");
        builder.HasIndex(i => new { i.TenantId, i.IsActive }).HasDatabaseName("ix_items_tenant_activo");

        // El índice GIN sobre tags va en la migración con SQL a mano: EF Core no
        // sabe declarar el método de acceso de un índice.
    }
}

public sealed class ItemPriceHistoryConfiguration : IEntityTypeConfiguration<ItemPriceHistory>
{
    public void Configure(EntityTypeBuilder<ItemPriceHistory> builder)
    {
        builder.ToTable("item_price_history");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.Id).HasColumnName("id");
        builder.Property(h => h.ItemId).HasColumnName("item_id").IsRequired();
        builder.Property(h => h.TenantId).HasColumnName("tenant_id").IsRequired();

        builder.Property(h => h.OldPrice).HasColumnName("old_price").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(h => h.NewPrice).HasColumnName("new_price").HasColumnType("numeric(18,2)").IsRequired();

        builder.Property(h => h.ChangedBy).HasColumnName("changed_by");
        builder.Property(h => h.ChangedAt).HasColumnName("changed_at").IsRequired();
        builder.Property(h => h.Reason).HasColumnName("reason").HasMaxLength(500);

        builder.HasIndex(h => new { h.TenantId, h.ItemId, h.ChangedAt })
            .HasDatabaseName("ix_item_price_history_tenant_item_fecha");

        // Sin cascada: el historial sobrevive al item. Da igual que el borrado
        // sea lógico —si alguien entra a la base a borrar la fila a mano, el
        // rastro de precios no debe irse con ella.
        builder
            .HasOne<Item>()
            .WithMany()
            .HasForeignKey(h => h.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
