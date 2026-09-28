using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.SharedKernel.Outbox;

/// <summary>
/// Un evento pendiente de publicar, guardado en la misma transacción que el
/// cambio de negocio que lo produjo.
///
/// Este es el corazón del patrón Outbox y la razón de que exista: sin él, un
/// caso de uso tendría que guardar en la base y publicar en el bus como dos
/// operaciones separadas, y no hay forma de hacer esas dos cosas
/// atómicamente. Si el proceso muere entre una y otra, o se confirmó la venta
/// pero nadie se enteró, o se avisó de una venta que no existe. Escribiendo el
/// evento como una fila más de la misma transacción, o pasan las dos cosas o
/// no pasa ninguna.
///
/// El precio es que la entrega es «al menos una vez»: si el publicador muere
/// después de enviar pero antes de marcar la fila, el evento se reenvía. Los
/// consumidores tienen que ser idempotentes — para eso llevan
/// <see cref="Id"/> y <see cref="CorrelationId"/>.
/// </summary>
public sealed class OutboxEvent : ITenantOwned
{
    /// <summary>Autoincremental: da además el orden real de los hechos.</summary>
    public long Id { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>Id de la entidad que cambió (la orden, la reserva…).</summary>
    public Guid AggregateId { get; private set; }

    /// <summary>Tipo de esa entidad: "Order", "Reservation"…</summary>
    public string AggregateType { get; private set; } = string.Empty;

    /// <summary>Nombre del evento en punto: "order.created", "tenant.suspended".</summary>
    public string EventType { get; private set; } = string.Empty;

    /// <summary>Cuerpo del evento. Columna jsonb, consultable desde SQL.</summary>
    public string EventPayloadJson { get; private set; } = "{}";

    /// <summary>Hilo que une esta operación con la petición que la originó.</summary>
    public Guid CorrelationId { get; private set; }

    public bool Published { get; private set; }
    public DateTime? PublishedAt { get; private set; }

    public int RetryCount { get; private set; }
    public DateTime? LastRetryAt { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>
    /// Cuándo se dio por perdido. Es la cola de mensajes muertos de los pobres:
    /// el evento deja de reintentarse pero no se borra, así que se puede
    /// investigar y reenviar a mano. Perder eventos en silencio es peor que
    /// dejarlos parados.
    /// </summary>
    public DateTime? DeadLetteredAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private OutboxEvent()
    {
    }

    public static OutboxEvent Create(
        Guid tenantId,
        Guid aggregateId,
        string aggregateType,
        string eventType,
        string payloadJson,
        Guid correlationId,
        DateTime createdAt) => new()
        {
            TenantId = tenantId,
            AggregateId = aggregateId,
            AggregateType = aggregateType,
            EventType = eventType,
            EventPayloadJson = payloadJson,
            CorrelationId = correlationId,
            CreatedAt = createdAt,
            Published = false,
            RetryCount = 0,
        };

    public void MarkPublished(DateTime at)
    {
        Published = true;
        PublishedAt = at;
        LastError = null;
    }

    public void MarkFailed(DateTime at, string error, int maxRetries)
    {
        RetryCount++;
        LastRetryAt = at;

        // Recortado: un error de Postgres con traza entera llena la tabla y no
        // aporta más que las primeras líneas.
        LastError = error.Length > 1000 ? error[..1000] : error;

        if (RetryCount >= maxRetries)
        {
            DeadLetteredAt = at;
        }
    }
}
