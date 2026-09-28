using HubNegocios.ReservationsService.Domain.Entities;
using HubNegocios.ReservationsService.Domain.Ports;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace HubNegocios.ReservationsService.Application.UseCases;

/// <summary>
/// El evento <c>order.created</c> tal y como lo publica order-service, con lo
/// que este servicio necesita y nada más.
///
/// Es un contrato entre servicios, así que se copia, no se comparte: si
/// reservations-service importara la clase de order-service, un cambio en el
/// modelo interno de aquel rompería la compilación de este, y los diez
/// servicios volverían a ser uno solo repartido en diez repositorios.
/// </summary>
public sealed record OrderCreatedEvent(
    Guid TenantId,
    Guid OrderId,
    string OrderType,
    Guid TimeSlotId,
    int PartySize,
    string CustomerName,
    string? CustomerPhone,
    string? CustomerEmail,
    string? SpecialRequests,
    Guid CorrelationId);

public sealed record ConsumeOrderCreatedResult(
    bool Reserved,
    Guid? ReservationId,
    string? ConfirmationCode,
    string? RejectionCode);

/// <summary>
/// Consume <c>order.created</c> y convierte en reserva las órdenes de tipo
/// Reservation.
///
/// Emite <c>reservation.confirmed</c> cuando hay cupo —lo hace
/// <see cref="ReserveSlotHandler"/>, dentro de su transacción— y
/// <c>reservation.rejected</c> cuando ya no lo hay. Ese rechazo no es
/// informativo: order-service lo escucha para no cobrar una reserva que no
/// existe.
///
/// IDEMPOTENCIA. La outbox entrega «al menos una vez»: si el publicador muere
/// después de enviar y antes de marcar la fila, <c>order.created</c> llega dos
/// veces. Sin defensa, la segunda entrega crearía una segunda reserva y
/// descontaría cupo otra vez por la misma orden. Aquí hay dos barreras:
///
/// 1. La comprobación por <c>OrderId</c> de más abajo, que resuelve el caso
///    normal —las dos entregas separadas por segundos o minutos—.
/// 2. El índice único sobre <c>order_id</c>, que resuelve el caso que la
///    comprobación no puede: dos entregas procesándose EN PARALELO, donde ambas
///    consultan antes de que ninguna haya escrito. Ahí la segunda choca contra
///    el índice, el repositorio traduce el choque a
///    <see cref="ReservationErrorCodes.OrderAlreadyReserved"/> y este consumidor
///    lo trata como «ya estaba reservado» en vez de propagar un error.
///
/// La comprobación previa sola no basta, y el índice solo sin la comprobación
/// funcionaría pero dejaría el registro lleno de errores donde no hay ninguno.
/// </summary>
public sealed class ConsumeOrderCreatedEventHandler(
    IReservationRepository repository,
    ReserveSlotHandler reserveSlot,
    IOutboxWriter outbox,
    TimeProvider clock,
    ILogger<ConsumeOrderCreatedEventHandler> logger)
{
    /// <summary>Tipo de orden que le corresponde a este servicio.</summary>
    public const string ReservationOrderType = "Reservation";

    public async Task<ConsumeOrderCreatedResult> HandleAsync(
        OrderCreatedEvent message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!string.Equals(message.OrderType, ReservationOrderType, StringComparison.OrdinalIgnoreCase))
        {
            /*
              El bus reparte todos los order.created; la mayoría son de productos
              y no tienen nada que ver con reservas. Descartarlos aquí y no
              filtrarlos en la suscripción es a propósito: el filtro del broker
              es configuración que se puede desplegar mal, y una orden de
              producto procesada como reserva fallaría con un error confuso.
            */
            logger.LogDebug(
                "Se ignora la orden {OrderId} porque su tipo es {OrderType}",
                message.OrderId,
                message.OrderType);

            return new ConsumeOrderCreatedResult(false, null, null, null);
        }

        if (message.TenantId == Guid.Empty)
        {
            throw new DomainException(
                "RESERVATION_EVENT_TENANT_MISSING",
                "El evento order.created llegó sin tenant y no se puede procesar.");
        }

        /*
          Un consumidor de eventos no tiene petición HTTP, así que nadie ha
          fijado el tenant: hay que abrirlo a mano con el del evento. Sin esto,
          el filtro global no encontraría nada, RLS rechazaría la escritura y el
          interceptor de tenant reventaría al guardar.
        */
        using var scope = TenantContext.BeginScope(message.TenantId, message.CorrelationId);

        var existing = await repository
            .GetByOrderIdAsync(message.OrderId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            logger.LogInformation(
                "La orden {OrderId} ya tenía la reserva {ReservationId}; entrega repetida descartada",
                message.OrderId,
                existing.Id);

            return new ConsumeOrderCreatedResult(true, existing.Id, existing.ConfirmationCode, null);
        }

        try
        {
            var result = await reserveSlot.HandleAsync(
                new ReserveSlotCommand(
                    message.TimeSlotId,
                    message.OrderId,
                    message.PartySize,
                    message.CustomerName,
                    message.CustomerPhone,
                    message.CustomerEmail,
                    message.SpecialRequests),
                cancellationToken).ConfigureAwait(false);

            return new ConsumeOrderCreatedResult(true, result.ReservationId, result.ConfirmationCode, null);
        }
        catch (ConflictException ex) when (ex.Code == ReservationErrorCodes.OrderAlreadyReserved)
        {
            /*
              Segunda barrera de idempotencia: dos entregas en paralelo pasaron
              las dos la comprobación de arriba y el índice único paró a la
              segunda. No es un error, es el sistema funcionando; se recupera la
              reserva que sí entró y se responde con ella.
            */
            var reserved = await repository
                .GetByOrderIdAsync(message.OrderId, cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Entrega simultánea de la orden {OrderId}: el índice único evitó la reserva duplicada",
                message.OrderId);

            return new ConsumeOrderCreatedResult(true, reserved?.Id, reserved?.ConfirmationCode, null);
        }
        catch (ConflictException ex) when (ex.Code == ReservationErrorCodes.ConfirmationCodeTaken)
        {
            /*
              Choque del código de confirmación contra su índice único. Al
              contrario que la franja llena, esto SÍ se arregla reintentando: el
              siguiente intento genera otro código. Se propaga a propósito para
              que el publicador reentregue el evento en vez de rechazar una
              reserva que sí tenía cupo.
            */
            logger.LogWarning(
                ex,
                "Choque de código de confirmación al reservar la orden {OrderId}; se deja reintentar la entrega",
                message.OrderId);

            throw;
        }
        catch (DomainException ex)
        {
            /*
              Franja llena, bloqueada o inexistente, o datos del evento que no
              cuadran. Nada de esto se arregla reintentando, así que no se
              propaga la excepción —eso haría que el publicador reintentase
              hasta agotar los intentos y acabase en la cola de muertos— sino que
              se contesta con un rechazo explícito.
            */
            outbox.Enqueue(
                message.OrderId,
                nameof(Reservation),
                "reservation.rejected",
                new
                {
                    orderId = message.OrderId,
                    timeSlotId = message.TimeSlotId,
                    partySize = message.PartySize,
                    reasonCode = ex.Code,
                    reason = ex.Message,
                    rejectedAt = clock.GetUtcNow().UtcDateTime,
                });

            // Guardado aparte: la transacción de ReserveSlot ya se deshizo, y con
            // ella todo lo que llevaba dentro. Este SaveChanges escribe solo el
            // evento de rechazo.
            await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogWarning(
                "Reserva rechazada para la orden {OrderId} en la franja {TimeSlotId}: {Code} — {Message}",
                message.OrderId,
                message.TimeSlotId,
                ex.Code,
                ex.Message);

            return new ConsumeOrderCreatedResult(false, null, null, ex.Code);
        }
    }
}
