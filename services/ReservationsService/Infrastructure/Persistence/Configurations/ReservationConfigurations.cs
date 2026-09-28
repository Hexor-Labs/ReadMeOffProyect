using HubNegocios.ReservationsService.Domain.Entities;
using HubNegocios.ReservationsService.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HubNegocios.ReservationsService.Infrastructure.Persistence.Configurations;

public sealed class TimeSlotConfiguration : IEntityTypeConfiguration<TimeSlot>
{
    public void Configure(EntityTypeBuilder<TimeSlot> builder)
    {
        builder.ToTable("time_slots");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(s => s.ItemId).HasColumnName("item_id").IsRequired();

        builder.Property(s => s.SlotStart).HasColumnName("slot_start").IsRequired();
        builder.Property(s => s.SlotEnd).HasColumnName("slot_end").IsRequired();

        builder.Property(s => s.TotalCapacity).HasColumnName("total_capacity").IsRequired();
        builder.Property(s => s.AvailableCapacity).HasColumnName("available_capacity").IsRequired();

        builder.Property(s => s.IsBlocked).HasColumnName("is_blocked").HasDefaultValue(false).IsRequired();
        builder.Property(s => s.ReasonIfBlocked).HasColumnName("reason_if_blocked").HasMaxLength(300);

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.CreatedBy).HasColumnName("created_by");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");

        /*
          La consulta del cliente es siempre la misma: «qué hay libre en este
          recurso entre estas dos fechas». El índice sigue ese orden —tenant,
          recurso, inicio— porque un índice solo sirve desde la izquierda: al
          revés, buscar por recurso obligaría a recorrerlo entero.
        */
        builder
            .HasIndex(s => new { s.TenantId, s.ItemId, s.SlotStart })
            .HasDatabaseName("ix_time_slots_tenant_item_inicio");

        /*
          La restricción CHECK que impide available_capacity < 0 se añade en la
          migración con SQL a mano, no aquí. Es deliberado: el bloqueo pesimista
          de ReserveSlotHandler es la defensa real, y el CHECK es la red por si
          algún día alguien escribe por otro camino —un script de corrección, un
          UPDATE a mano— sin pasar por el caso de uso.
        */
    }
}

public sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(r => r.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(r => r.TimeSlotId).HasColumnName("time_slot_id").IsRequired();

        builder.Property(r => r.PartySize).HasColumnName("party_size").IsRequired();

        builder.Property(r => r.CustomerName).HasColumnName("customer_name").HasMaxLength(200).IsRequired();
        builder.Property(r => r.CustomerPhone).HasColumnName("customer_phone").HasMaxLength(30);
        builder.Property(r => r.CustomerEmail).HasColumnName("customer_email").HasMaxLength(320);
        builder.Property(r => r.SpecialRequests).HasColumnName("special_requests").HasMaxLength(1000);

        /*
          El enum se guarda como texto, no como número. Cuesta unos bytes más y
          ahorra el problema real: un 2 en una columna no dice nada cuando
          alguien investiga una incidencia a las tres de la mañana, y reordenar
          los valores del enum en C# reinterpreta en silencio todas las filas ya
          guardadas.
        */
        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.ConfirmationCode)
            .HasColumnName("confirmation_code")
            .HasMaxLength(ConfirmationCode.MaxLength)
            .IsRequired();

        builder.Property(r => r.ConfirmationSentAt).HasColumnName("confirmation_sent_at");
        builder.Property(r => r.ReminderSentAt).HasColumnName("reminder_sent_at");

        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by");

        /*
          Este índice es el que hace idempotente al consumidor de order.created.
          La outbox entrega «al menos una vez», así que el evento puede llegar
          dos veces; sin este índice, la segunda entrega crearía una reserva
          duplicada y descontaría cupo otra vez por la misma orden. Con él, la
          segunda inserción choca y el consumidor puede tratarla como «ya estaba
          reservado».

          Único global y no por tenant: un order_id es un Guid que identifica una
          orden en todo el hub, y una orden pertenece a un solo negocio.
        */
        builder.HasIndex(r => r.OrderId).IsUnique().HasDatabaseName("ux_reservations_order");

        /*
          El código de confirmación es único DENTRO del tenant y no globalmente.
          Es lo que corresponde: el cliente lo presenta en un negocio concreto, y
          exigir unicidad global gastaría el espacio de códigos entre todos los
          negocios del hub para nada.
        */
        builder
            .HasIndex(r => new { r.TenantId, r.ConfirmationCode })
            .IsUnique()
            .HasDatabaseName("ux_reservations_confirmation_code");

        builder
            .HasIndex(r => new { r.TenantId, r.TimeSlotId })
            .HasDatabaseName("ix_reservations_tenant_franja");

        /*
          Restrict y no Cascade: borrar una franja que tiene reservas
          confirmadas no puede llevarse las reservas por delante en silencio.
          Quien quiera cerrar un horario bloquea la franja, que es una operación
          reversible y que deja rastro.
        */
        builder
            .HasOne<TimeSlot>()
            .WithMany()
            .HasForeignKey(r => r.TimeSlotId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
