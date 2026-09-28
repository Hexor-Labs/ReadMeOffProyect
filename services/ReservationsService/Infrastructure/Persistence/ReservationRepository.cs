using HubNegocios.ReservationsService.Domain.Entities;
using HubNegocios.ReservationsService.Domain.Ports;
using HubNegocios.SharedKernel.Http;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

namespace HubNegocios.ReservationsService.Infrastructure.Persistence;

/// <summary>
/// Adaptador de salida: implementa el puerto del dominio con EF Core y
/// PostgreSQL.
///
/// Todo lo que este servicio sabe de bases de datos empieza y acaba aquí. Es
/// también el único sitio donde aparece SQL a mano, y aparece por una razón
/// concreta: EF Core no expone el bloqueo pesimista.
/// </summary>
public sealed class ReservationRepository(ReservationsDbContext context) : IReservationRepository
{
    /*
      SELECT ... FOR UPDATE sobre la franja.

      Va en SQL crudo porque EF Core no tiene forma de expresar un bloqueo de
      fila: no hay ningún método de LINQ que se traduzca a FOR UPDATE. Y ese
      bloqueo es justo lo que impide vender dos veces la misma mesa, así que no
      es opcional.

      Los valores van como PARÁMETROS ({0} y {1}), nunca concatenados. Aquí los
      dos son Guid y un Guid no puede inyectar nada, pero la costumbre de
      concatenar es la que un día se aplica a un parámetro que sí viene de un
      formulario. En SQL crudo, la regla es una sin excepciones.

      El tenant va explícito en el WHERE además del filtro global de EF y de la
      política de RLS. Triplicado a propósito: el filtro global cubre las
      lecturas LINQ, RLS cubre lo que se escape por SQL, y esta condición hace
      que el bloqueo se tome sobre la fila correcta incluso si alguien tocara lo
      otro.
    */
    private const string LockTimeSlotSql = """
        SELECT * FROM time_slots
        WHERE id = {0} AND tenant_id = {1}
        FOR UPDATE
        """;

    private const string LockReservationSql = """
        SELECT * FROM reservations
        WHERE id = {0} AND tenant_id = {1}
        FOR UPDATE
        """;

    public async Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        new EfTransactionScope(
            await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false),
            context);

    public async Task<TimeSlot?> LockTimeSlotForUpdateAsync(
        Guid timeSlotId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        /*
          ToListAsync y no FirstOrDefaultAsync: FirstOrDefault compondría un
          LIMIT sobre la consulta cruda, y componer sobre SQL con cláusula de
          bloqueo es terreno donde el SQL final ya no se controla. La fila es
          única por clave primaria, así que la lista trae cero o un elemento.

          El resultado viene rastreado —no se pide AsNoTracking— porque la razón
          de bloquear es precisamente modificar el cupo y guardarlo.
        */
        var rows = await context.TimeSlots
            .FromSqlRaw(LockTimeSlotSql, timeSlotId, tenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<Reservation?> LockReservationForUpdateAsync(
        Guid reservationId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var rows = await context.Reservations
            .FromSqlRaw(LockReservationSql, reservationId, tenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Count == 0 ? null : rows[0];
    }

    public Task<Reservation?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        context.Reservations
            // Sin rastreo: solo se mira si existe y se devuelven sus datos.
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);

    public Task<bool> ConfirmationCodeExistsAsync(
        string confirmationCode,
        CancellationToken cancellationToken = default) =>
        context.Reservations
            .AsNoTracking()
            .AnyAsync(r => r.ConfirmationCode == confirmationCode, cancellationToken);

    public void AddTimeSlots(IEnumerable<TimeSlot> slots) => context.TimeSlots.AddRange(slots);

    public void AddReservation(Reservation reservation) => context.Reservations.Add(reservation);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres
            && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            /*
              Aquí se traduce el choque de un índice único a un error de negocio.

              La traducción vive en el adaptador y no en el caso de uso porque es
              lo único que permite que la aplicación reaccione a «esa orden ya
              estaba reservada» sin conocer los códigos de error de PostgreSQL.
              Sin esto, el consumidor de eventos tendría que atrapar
              DbUpdateException y mirar SqlState, y el dominio dejaría de ser
              independiente de la base de datos.
            */
            throw postgres.ConstraintName switch
            {
                "ux_reservations_order" => new ConflictException(
                    ReservationErrorCodes.OrderAlreadyReserved,
                    "Esa orden ya tenía una reserva."),

                "ux_reservations_confirmation_code" => new ConflictException(
                    ReservationErrorCodes.ConfirmationCodeTaken,
                    "El código de confirmación generado ya estaba en uso."),

                // Cualquier otro índice único es un caso que nadie previó: se
                // deja subir tal cual en vez de disfrazarlo de error de negocio.
                _ => ex,
            };
        }
    }

    /// <summary>
    /// La transacción de EF Core vista como el puerto del dominio.
    ///
    /// Hace una cosa más que delegar, y es importante: al deshacer, limpia el
    /// rastreador de cambios. Sin eso, los cambios de la transacción abortada
    /// —la reserva insertada, el cupo descontado— siguen marcados como
    /// pendientes en el contexto, y el siguiente <c>SaveChanges</c> de esa misma
    /// petición los reenviaría a la base de datos ya fuera de la transacción que
    /// se acaba de deshacer. Es exactamente lo que pasaría en el consumidor de
    /// <c>order.created</c> al guardar el evento de rechazo después de un
    /// rechazo por falta de cupo.
    /// </summary>
    private sealed class EfTransactionScope(IDbContextTransaction transaction, ReservationsDbContext context)
        : ITransactionScope
    {
        private bool _finished;

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _finished = true;
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;

            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            context.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            // Cerrar sin confirmar deshace. Es lo que convierte un «await using»
            // alrededor del caso de uso en una garantía y no en una costumbre.
            await RollbackAsync().ConfigureAwait(false);
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }
}
