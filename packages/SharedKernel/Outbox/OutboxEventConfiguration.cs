using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HubNegocios.SharedKernel.Outbox;

/// <summary>
/// Mapeo de la tabla <c>outbox_events</c>. Es idéntica en los diez servicios,
/// así que vive aquí y cada DbContext la aplica con
/// <c>ApplyConfiguration(new OutboxEventConfiguration())</c>.
/// </summary>
public sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("outbox_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.AggregateId).HasColumnName("aggregate_id").IsRequired();
        builder.Property(e => e.AggregateType).HasColumnName("aggregate_type").HasMaxLength(100).IsRequired();
        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(150).IsRequired();

        // jsonb nativo, no texto: así se puede consultar dentro del evento
        // desde SQL el día que haga falta investigar algo.
        builder.Property(e => e.EventPayloadJson).HasColumnName("event_payload").HasColumnType("jsonb").IsRequired();

        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id").IsRequired();
        builder.Property(e => e.Published).HasColumnName("published").HasDefaultValue(false).IsRequired();
        builder.Property(e => e.PublishedAt).HasColumnName("published_at");
        builder.Property(e => e.RetryCount).HasColumnName("retry_count").HasDefaultValue(0).IsRequired();
        builder.Property(e => e.LastRetryAt).HasColumnName("last_retry_at");
        builder.Property(e => e.LastError).HasColumnName("last_error").HasMaxLength(1000);
        builder.Property(e => e.DeadLetteredAt).HasColumnName("dead_lettered_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        /*
          El publicador solo busca una cosa: los pendientes vivos, en orden.
          Índice parcial —solo las filas que cumplen la condición— porque la
          tabla se llena de eventos ya publicados que nunca se vuelven a leer:
          indexarlos todos sería pagar por mantener un índice enorme del que se
          usa siempre la misma esquina.
        */
        builder
            .HasIndex(e => e.Id)
            .HasDatabaseName("ix_outbox_events_pendientes")
            .HasFilter("published = false AND dead_lettered_at IS NULL");

        builder.HasIndex(e => new { e.TenantId, e.AggregateId }).HasDatabaseName("ix_outbox_events_tenant_agregado");
    }
}
