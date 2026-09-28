using HubNegocios.ReservationsService.Domain.Entities;
using HubNegocios.ReservationsService.Domain.Ports;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace HubNegocios.ReservationsService.Application.UseCases;

public sealed record CheckInReservationCommand(Guid ReservationId);

public sealed record CheckInReservationResult(Guid ReservationId, string Status);

/// <summary>
/// Registra que el cliente llegó.
///
/// Lo usa el personal de sala, normalmente con el código de confirmación en la
/// mano y mucha prisa. De ahí dos decisiones: la operación es idempotente —dos
/// pulsaciones no son un error— y no toca el cupo de la franja, porque la plaza
/// ya estaba ocupada desde que se confirmó la reserva.
/// </summary>
public sealed class CheckInReservationHandler(
    IReservationRepository repository,
    IOutboxWriter outbox,
    TimeProvider clock,
    ILogger<CheckInReservationHandler> logger)
{
    public async Task<CheckInReservationResult> HandleAsync(
        CheckInReservationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = TenantContext.Current.RequireTenantId();

        /*
          Transacción con bloqueo de la reserva, aunque solo se cambie una
          columna. El motivo es la cancelación: llegada y cancelación compiten
          por la misma fila y por el cupo de la franja. Sin bloqueo, una llegada
          y una cancelación simultáneas pueden acabar con la reserva cancelada,
          el cliente sentado y la plaza devuelta al cupo para que la ocupe otro.
        */
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var reservation = await repository
            .LockReservationForUpdateAsync(command.ReservationId, tenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("RESERVATION_NOT_FOUND", "La reserva no existe.");

        var alreadyCheckedIn = reservation.Status == ReservationStatus.CheckedIn;

        reservation.CheckIn();

        if (!alreadyCheckedIn)
        {
            // Solo se avisa del cambio real. Reemitir el evento en la segunda
            // pulsación haría que los consumidores contasen dos llegadas.
            outbox.Enqueue(
                reservation.Id,
                nameof(Reservation),
                "reservation.checked_in",
                new
                {
                    reservationId = reservation.Id,
                    orderId = reservation.OrderId,
                    timeSlotId = reservation.TimeSlotId,
                    partySize = reservation.PartySize,
                    checkedInAt = clock.GetUtcNow().UtcDateTime,
                });
        }

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Llegada registrada para la reserva {ReservationId} (ya estaba registrada: {Repetida})",
            reservation.Id,
            alreadyCheckedIn);

        return new CheckInReservationResult(reservation.Id, reservation.Status.ToString());
    }
}
