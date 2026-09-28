using System.Text.Json;

using HubNegocios.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace HubNegocios.SharedKernel.Outbox;

/// <summary>
/// Puerto que usan los casos de uso para emitir eventos.
///
/// No publica nada: deja la fila en la outbox, dentro de la transacción que el
/// caso de uso ya tiene abierta. Quien publica de verdad es
/// <see cref="OutboxPublisherService"/>, después y por separado.
/// </summary>
public interface IOutboxWriter
{
    /// <summary>Encola un evento del tenant en curso. Se persiste con el <c>SaveChanges</c> del caso de uso.</summary>
    void Enqueue<TPayload>(Guid aggregateId, string aggregateType, string eventType, TPayload payload);

    /// <summary>
    /// Encola un evento de un tenant concreto, sin mirar el contexto.
    ///
    /// Existe por tenant-service: cuando se da de alta un tenant todavía no hay
    /// ninguno en contexto —el tenant es justo lo que se está creando— y el
    /// evento <c>tenant.created</c> tiene que salir igual. Fuera de ese caso y
    /// de los trabajos de plataforma, usa la sobrecarga sin tenant: pasarlo a
    /// mano es volver a abrir la puerta a equivocarse de cliente.
    /// </summary>
    void EnqueueFor<TPayload>(
        Guid tenantId,
        Guid aggregateId,
        string aggregateType,
        string eventType,
        TPayload payload);
}

/// <summary>
/// Implementación sobre el <see cref="DbContext"/> del servicio.
///
/// Recibe el DbContext y no una conexión suya: es justo lo que garantiza que
/// el evento y el cambio de negocio compartan transacción. Si esta clase
/// abriera su propia conexión, el patrón Outbox dejaría de servir para lo
/// único que sirve.
/// </summary>
public sealed class OutboxWriter(DbContext context, TimeProvider clock) : IOutboxWriter
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public void Enqueue<TPayload>(Guid aggregateId, string aggregateType, string eventType, TPayload payload) =>
        Write(TenantContext.Current.RequireTenantId(), aggregateId, aggregateType, eventType, payload);

    public void EnqueueFor<TPayload>(
        Guid tenantId,
        Guid aggregateId,
        string aggregateType,
        string eventType,
        TPayload payload)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("El tenant del evento no puede ser vacío.", nameof(tenantId));
        }

        Write(tenantId, aggregateId, aggregateType, eventType, payload);
    }

    private void Write<TPayload>(
        Guid tenantId,
        Guid aggregateId,
        string aggregateType,
        string eventType,
        TPayload payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        var identity = TenantContext.Current;

        var outboxEvent = OutboxEvent.Create(
            tenantId: tenantId,
            aggregateId: aggregateId,
            aggregateType: aggregateType,
            eventType: eventType,
            payloadJson: JsonSerializer.Serialize(payload, PayloadOptions),
            correlationId: identity.CorrelationId,
            createdAt: clock.GetUtcNow().UtcDateTime);

        context.Set<OutboxEvent>().Add(outboxEvent);
    }
}
