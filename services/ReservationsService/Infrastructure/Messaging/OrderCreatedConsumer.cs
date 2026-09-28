using System.Text.Json;

using HubNegocios.ReservationsService.Application.UseCases;
using HubNegocios.SharedKernel.Http;

using Microsoft.Extensions.Logging;

namespace HubNegocios.ReservationsService.Infrastructure.Messaging;

/// <summary>
/// Cuerpo de <c>order.created</c> tal y como viaja en la outbox: JSON con las
/// claves en camelCase, que es lo que escribe <c>OutboxWriter</c>.
///
/// Todo es anulable porque esto es lo que llega de fuera, no lo que debería
/// llegar. Un campo que falte tiene que dar un error claro en la frontera, no
/// un <c>Guid.Empty</c> silencioso tres capas más adentro.
/// </summary>
internal sealed record OrderCreatedPayload(
    Guid? OrderId,
    string? OrderType,
    Guid? TimeSlotId,
    int? PartySize,
    string? CustomerName,
    string? CustomerPhone,
    string? CustomerEmail,
    string? SpecialRequests);

/// <summary>
/// Adaptador de entrada del bus de eventos: convierte un mensaje en una llamada
/// al caso de uso.
///
/// Aquí termina el JSON y empieza el dominio. El caso de uso recibe un
/// <see cref="OrderCreatedEvent"/> con tipos ya validados y no sabe que existe
/// un broker, lo cual es lo que permite probarlo sin levantar ninguno.
///
/// Todavía no hay suscripción real: el hub publica con
/// <c>LoggingEventBusPublisher</c> mientras no se conecte RabbitMQ o Kafka. El
/// día que exista el adaptador de entrada del broker, se limitará a llamar a
/// <see cref="ConsumeAsync"/> con lo que saque de la cola, y ni el caso de uso
/// ni esta traducción cambian.
/// </summary>
public sealed class OrderCreatedConsumer(
    ConsumeOrderCreatedEventHandler handler,
    ILogger<OrderCreatedConsumer> logger)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Nombre del evento que este adaptador atiende.</summary>
    public const string EventType = "order.created";

    /// <param name="tenantId">Tenant del evento. Viene del sobre, no del cuerpo.</param>
    /// <param name="correlationId">Correlación de la petición que originó la orden.</param>
    /// <param name="payloadJson">Cuerpo del evento.</param>
    public async Task<ConsumeOrderCreatedResult> ConsumeAsync(
        Guid tenantId,
        Guid correlationId,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        OrderCreatedPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<OrderCreatedPayload>(payloadJson, PayloadOptions);
        }
        catch (JsonException ex)
        {
            /*
              Un cuerpo ilegible no se arregla reintentando: se traduce a error de
              negocio para que suba como 422 y no como 500, y para que el
              publicador no lo reintente cinco veces antes de darlo por muerto.
            */
            logger.LogError(ex, "El cuerpo de order.created no es un JSON válido");

            throw new DomainException(
                "RESERVATION_EVENT_MALFORMED",
                "El cuerpo del evento order.created no se pudo interpretar.");
        }

        if (payload is null)
        {
            throw new DomainException(
                "RESERVATION_EVENT_MALFORMED",
                "El evento order.created llegó sin cuerpo.");
        }

        var errors = new Dictionary<string, string[]>();

        if (payload.OrderId is null || payload.OrderId == Guid.Empty)
        {
            errors[nameof(payload.OrderId)] = ["Falta el identificador de la orden."];
        }

        if (string.IsNullOrWhiteSpace(payload.OrderType))
        {
            errors[nameof(payload.OrderType)] = ["Falta el tipo de orden."];
        }

        if (payload.TimeSlotId is null || payload.TimeSlotId == Guid.Empty)
        {
            errors[nameof(payload.TimeSlotId)] = ["Falta la franja horaria a reservar."];
        }

        if (payload.PartySize is null || payload.PartySize <= 0)
        {
            errors[nameof(payload.PartySize)] = ["El número de comensales tiene que ser mayor que cero."];
        }

        if (string.IsNullOrWhiteSpace(payload.CustomerName))
        {
            errors[nameof(payload.CustomerName)] = ["Falta el nombre del cliente."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return await handler.HandleAsync(
            new OrderCreatedEvent(
                tenantId,
                payload.OrderId!.Value,
                payload.OrderType!,
                payload.TimeSlotId!.Value,
                payload.PartySize!.Value,
                payload.CustomerName!,
                payload.CustomerPhone,
                payload.CustomerEmail,
                payload.SpecialRequests,
                correlationId),
            cancellationToken).ConfigureAwait(false);
    }
}
