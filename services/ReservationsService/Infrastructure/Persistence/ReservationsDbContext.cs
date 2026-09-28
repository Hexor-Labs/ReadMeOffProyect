using HubNegocios.ReservationsService.Domain.Entities;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HubNegocios.ReservationsService.Infrastructure.Persistence;

/// <summary>
/// Base de datos de reservations-service (<c>reservations_db</c>).
///
/// Cada servicio tiene la suya y nadie más la toca: si otro servicio necesita
/// saber de una reserva, lo pregunta por API o se entera por evento. Un JOIN
/// entre bases de dos servicios es el atajo que convierte diez microservicios en
/// un monolito distribuido, que es lo peor de los dos mundos.
/// </summary>
public sealed class ReservationsDbContext(DbContextOptions<ReservationsDbContext> options) : DbContext(options)
{
    public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReservationsDbContext).Assembly);
        modelBuilder.ApplyOutbox();

        /*
          Aquí sí se filtra por tenant, al contrario que en tenant-service: las
          franjas y las reservas pertenecen a un negocio concreto y ninguna
          consulta LINQ debe poder ver las de otro.

          Ojo con el alcance: el filtro cubre las lecturas con LINQ, no el SQL
          crudo. El SELECT ... FOR UPDATE del repositorio lleva su propia
          condición de tenant escrita a mano, y por debajo de todo está RLS.
        */
        modelBuilder.ApplyTenantFilters();

        base.OnModelCreating(modelBuilder);
    }
}
