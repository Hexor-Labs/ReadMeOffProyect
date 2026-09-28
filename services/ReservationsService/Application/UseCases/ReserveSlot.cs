using HubNegocios.ReservationsService.Domain.Entities;
using HubNegocios.ReservationsService.Domain.Ports;
using HubNegocios.ReservationsService.Domain.ValueObjects;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace HubNegocios.ReservationsService.Application.UseCases;

public sealed record ReserveSlotCommand(
    Guid TimeSlotId,
    Guid OrderId,
    int PartySize,
    string CustomerName,
    string? CustomerPhone,
    string? CustomerEmail,
    string? SpecialRequests);

public sealed record ReserveSlotResult(
    Guid ReservationId,
    string ConfirmationCode,
    Guid TimeSlotId,
    int RemainingCapacity);

/// <summary>
/// Ocupa plazas de una franja y confirma la reserva. Es el caso de uso crítico
/// del servicio.
///
/// Todo el diseño de aquí gira alrededor de una sola cosa: que dos reservas
/// simultáneas de la misma franja no puedan vender el mismo cupo dos veces. Por
/// qué la solución obvia no vale está explicado abajo, junto al bloqueo.
/// </summary>
public sealed class ReserveSlotHandler(
    IReservationRepository repository,
    IOutboxWriter outbox,
    TimeProvider clock,
    ILogger<ReserveSlotHandler> logger)
{
    /// <summary>
    /// Intentos de generación del código antes de rendirse. Con un millón de
    /// combinaciones, cinco choques seguidos no son mala suerte: son señal de
    /// que la tabla está saturada o de que el generador dejó de ser aleatorio.
    /// </summary>
    private const int MaxCodeAttempts = 5;

    public async Task<ReserveSlotResult> HandleAsync(
        ReserveSlotCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = TenantContext.Current.RequireTenantId();

        /*
          Transacción explícita, y no el SaveChanges implícito que basta en el
          resto de casos de uso del hub. Aquí hay dos cosas que tienen que ser
          una sola: leer-y-bloquear la franja, y descontar el cupo escribiendo
          la reserva. El bloqueo de fila solo vive mientras vive la transacción,
          así que sin transacción explícita no hay bloqueo que valga: se
          liberaría al acabar la lectura, que es justo antes del momento
          peligroso.

          Cerrar el ámbito sin confirmar deshace. Por eso cualquier excepción de
          aquí abajo —franja llena, franja bloqueada, choque de código— sale con
          la transacción abortada y sin cupo descontado a medias.
        */
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        /*
          EL PUNTO CRÍTICO DEL SERVICIO.

          Esto es un SELECT ... FOR UPDATE: bloquea la fila de la franja hasta
          que la transacción termine.

          La versión ingenua de este caso de uso sería leer la franja y hacer
          «if (AvailableCapacity > 0) { descontar }». NO BASTA, y el motivo es
          que entre el if y la escritura cabe otra transacción entera:

              Transacción A                Transacción B
              lee cupo = 1                 .
              .                            lee cupo = 1
              comprueba 1 >= 1  ok         .
              .                            comprueba 1 >= 1  ok
              escribe cupo = 0             .
              confirma                     escribe cupo = 0
              .                            confirma

          Las dos leyeron el mismo 1 y las dos creyeron tener derecho a él.
          Resultado: dos reservas para una sola mesa. Y el problema no sale en
          ninguna prueba ni en ningún log: sale en la puerta del restaurante un
          sábado a las nueve, con dos familias y una mesa.

          Con FOR UPDATE, B se queda esperando en la lectura hasta que A
          confirma; entonces lee cupo = 0 y se le rechaza limpiamente. El
          bloqueo convierte una carrera en una fila india.

          Se eligió bloqueo pesimista y no optimista —reintentar sobre una
          columna de versión— porque en el minuto en que se abre la reserva de
          un horario popular la colisión es la norma y no la excepción: con
          optimista casi todos los intentos reintentarían, y el orden de
          servicio acabaría dependiendo de quién reintenta más rápido.
        */
        var slot = await repository
            .LockTimeSlotForUpdateAsync(command.TimeSlotId, tenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                "RESERVATION_SLOT_NOT_FOUND",
                "La franja horaria no existe.");

        // Ahora sí: el cupo que se acaba de leer no lo puede cambiar nadie hasta
        // que esta transacción termine. La regla la valida la entidad.
        slot.Reserve(command.PartySize);

        var confirmationCode = await GenerateUniqueCodeAsync(cancellationToken).ConfigureAwait(false);

        var reservation = Reservation.Confirm(
            Guid.NewGuid(),
            tenantId,
            command.OrderId,
            slot.Id,
            command.PartySize,
            command.CustomerName,
            command.CustomerPhone,
            command.CustomerEmail,
            command.SpecialRequests,
            confirmationCode);

        repository.AddReservation(reservation);

        /*
          El evento se encola dentro de la misma transacción, como una fila más.
          Publicarlo aquí a mano abriría la puerta a avisar de una reserva que
          después no llegó a existir —o a no avisar de una que sí—, que es
          justamente lo que el patrón Outbox evita.
        */
        outbox.Enqueue(
            reservation.Id,
            nameof(Reservation),
            "reservation.confirmed",
            new
            {
                reservationId = reservation.Id,
                orderId = reservation.OrderId,
                timeSlotId = slot.Id,
                itemId = slot.ItemId,
                slotStart = slot.SlotStart,
                slotEnd = slot.SlotEnd,
                partySize = reservation.PartySize,
                confirmationCode = reservation.ConfirmationCode,
                customerName = reservation.CustomerName,
                confirmedAt = clock.GetUtcNow().UtcDateTime,
            });

        // Un solo SaveChanges: el descuento de cupo, la reserva y el evento
        // entran juntos o no entra ninguno.
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Reserva {ReservationId} confirmada en la franja {TimeSlotId} para {PartySize} personas; quedan {Remaining} plazas",
            reservation.Id,
            slot.Id,
            reservation.PartySize,
            slot.AvailableCapacity);

        return new ReserveSlotResult(
            reservation.Id,
            reservation.ConfirmationCode,
            slot.Id,
            slot.AvailableCapacity);
    }

    /// <summary>
    /// Un código que todavía no exista en el tenant.
    ///
    /// La comprobación previa no es la garantía —entre consultar y escribir cabe
    /// otra transacción—; eso lo garantiza el índice único de la tabla. Esto
    /// solo evita que el caso normal acabe en un error 500. Con cuatro
    /// caracteres sobre 32 símbolos el choque es rarísimo, pero «rarísimo» a
    /// escala de miles de reservas al mes ocurre, y ocurrir significa que un
    /// cliente no pudo reservar por un motivo que nadie sabría explicar.
    /// </summary>
    private async Task<ConfirmationCode> GenerateUniqueCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxCodeAttempts; attempt++)
        {
            var candidate = ConfirmationCode.Generate();

            var taken = await repository
                .ConfirmationCodeExistsAsync(candidate.Value, cancellationToken)
                .ConfigureAwait(false);

            if (!taken)
            {
                return candidate;
            }

            logger.LogWarning(
                "El código de confirmación {Code} ya existía; se reintenta (intento {Attempt} de {Max})",
                candidate.Value,
                attempt,
                MaxCodeAttempts);
        }

        throw new ConflictException(
            "RESERVATION_CODE_UNAVAILABLE",
            "No se pudo generar un código de confirmación libre. Inténtalo de nuevo.");
    }
}
