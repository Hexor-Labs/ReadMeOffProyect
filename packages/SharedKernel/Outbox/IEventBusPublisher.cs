using Microsoft.Extensions.Logging;

namespace HubNegocios.SharedKernel.Outbox;

/// <summary>
/// Salida hacia el bus de eventos.
///
/// Deliberadamente mínima: el día que se conecte RabbitMQ o Kafka de verdad,
/// se escribe una implementación nueva y no se toca ni un caso de uso. Por eso
/// el contrato habla de tipo de evento y cuerpo, y no de exchanges, colas,
/// particiones ni claves de reparto — conceptos que pertenecen al adaptador,
/// no al dominio.
/// </summary>
public interface IEventBusPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Lo que viaja al bus. El <c>CorrelationId</c> va siempre.</summary>
public sealed record OutboxMessage(
    long Id,
    Guid TenantId,
    Guid AggregateId,
    string AggregateType,
    string EventType,
    string PayloadJson,
    Guid CorrelationId,
    DateTime OccurredAt);

/// <summary>
/// Implementación provisional: registra el evento y da por publicado.
///
/// Sirve para desarrollar y probar el flujo completo sin levantar un broker.
/// Está registrada por defecto a propósito, para que arrancar un servicio
/// recién clonado no exija infraestructura; cuando exista el adaptador real
/// se sustituye en el <c>Program.cs</c> y nada más cambia.
/// </summary>
public sealed class LoggingEventBusPublisher(ILogger<LoggingEventBusPublisher> logger) : IEventBusPublisher
{
    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        logger.LogInformation(
            "Evento {EventType} del agregado {AggregateType}/{AggregateId} (tenant {TenantId}, correlación {CorrelationId}) publicado en el bus de pruebas",
            message.EventType,
            message.AggregateType,
            message.AggregateId,
            message.TenantId,
            message.CorrelationId);

        return Task.CompletedTask;
    }
}
