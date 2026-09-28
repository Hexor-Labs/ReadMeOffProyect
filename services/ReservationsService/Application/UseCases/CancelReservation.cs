using HubNegocios.ReservationsService.Domain.Entities;
using HubNegocios.ReservationsService.Domain.Ports;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace HubNegocios.ReservationsService.Application.UseCases;

public sealed record CancelReservationCommand(Guid ReservationId, string? Reason);

public sealed record CancelReservationResult(Guid ReservationId, Guid TimeSlotId, int RemainingCapacity);

/// <summary>
/// Cancela una reserva y devuelve sus plazas al cupo de la franja.
///
/// Es la otra mitad del caso crítico: si al cancelar no se repone el cupo, la
/// mesa queda vacía y bloqueada para siempre, y eso no lo detecta nadie porque
/// no produce ningún error, solo pérdida de ingresos.
/// </summary>
public sealed class CancelReservationHandler(
    IReservationRepository repository,
    IOutboxWriter outbox,
    TimeProvider clock,
    ILogger<CancelReservationHandler> logger)
{
    public async Task<CancelReservationResult> HandleAsync(
        CancelReservationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = TenantContext.Current.RequireTenantId();

        /*
          Mismo patrón que al reservar, y por el mismo motivo: devolver plazas es
          otro leer-decidir-escribir sobre available_capacity. Sin bloqueo, una
          cancelación y una reserva simultáneas pueden perder una de las dos
          escrituras y dejar el contador mintiendo.

          El orden de los bloqueos —primero la reserva, después su franja— es
          siempre el mismo en todo el servicio. Eso es lo que evita el abrazo
          mortal: dos operaciones que tomasen los mismos dos bloqueos en orden
          contrario se esperarían la una a la otra para siempre.
        */
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var reservation = await repository
            .LockReservationForUpdateAsync(command.ReservationId, tenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("RESERVATION_NOT_FOUND", "La reserva no existe.");

        var slot = await repository
            .LockTimeSlotForUpdateAsync(reservation.TimeSlotId, tenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                "RESERVATION_SLOT_NOT_FOUND",
                "La franja de la reserva no existe.");

        // Cancel lanza si ya estaba cancelada: cobrar dos veces el mismo
        // cupo de vuelta sería sobreaforo.
        var releasedSeats = reservation.Cancel();
        slot.Release(releasedSeats);

        outbox.Enqueue(
            reservation.Id,
            nameof(Reservation),
            "reservation.cancelled",
            new
            {
                reservationId = reservation.Id,
                orderId = reservation.OrderId,
                timeSlotId = slot.Id,
                releasedSeats,
                reason = command.Reason,
                cancelledAt = clock.GetUtcNow().UtcDateTime,
            });

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Reserva {ReservationId} cancelada; devueltas {Seats} plazas a la franja {TimeSlotId}, que queda con {Remaining}",
            reservation.Id,
            releasedSeats,
            slot.Id,
            slot.AvailableCapacity);

        return new CancelReservationResult(reservation.Id, slot.Id, slot.AvailableCapacity);
    }
}
