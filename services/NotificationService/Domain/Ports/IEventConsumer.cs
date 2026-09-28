using HubNegocios.SharedKernel.Outbox;

namespace HubNegocios.NotificationService.Domain.Ports;

/// <summary>
/// Entrada desde el bus de eventos: la cara opuesta de
/// <see cref="IEventBusPublisher"/> del SharedKernel.
///
/// Habla de <see cref="OutboxMessage"/> a propósito y no de un tipo propio: es
/// literalmente lo que el publicador deja en el bus, así que reutilizarlo es lo
/// que garantiza que las dos mitades del contrato no se separen con el tiempo. Y
/// como el publicador, este puerto no menciona colas, exchanges, particiones ni
/// grupos de consumo: eso es del adaptador.
///
/// POR QUÉ ESTE SERVICIO NO NECESITA OUTBOX PARA CONSUMIR. El patrón Outbox
/// existe para que un cambio de negocio y el aviso al resto del sistema entren
/// en la misma transacción. Aquí no hay cambio de negocio que proteger: este
/// servicio no confirma reservas, no descuenta stock y no mueve dinero, solo
/// manda mensajes y anota qué pasó. Lo único que hay que garantizar es que un
/// evento repetido no produzca dos mensajes, y eso lo resuelve la idempotencia
/// del log —índice único sobre (tenant_id, event_id)—, no una outbox. Montar una
/// aquí añadiría una tabla, un publicador y una transacción por evento sin tapar
/// ningún agujero.
///
/// Lo que sí queda pendiente es el adaptador real. Mientras no exista,
/// <c>InMemoryEventConsumer</c> hace de doble, igual que
/// <c>LoggingEventBusPublisher</c> hace de bus en el lado de la publicación.
/// </summary>
public interface IEventConsumer
{
    /// <summary>
    /// Trae hasta <paramref name="maxEvents"/> eventos sin confirmar. Devolver una
    /// lista vacía es normal y es lo que hace que el sondeo sea barato.
    /// </summary>
    Task<IReadOnlyList<OutboxMessage>> PullAsync(int maxEvents, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirma que el evento ya está atendido y que no hace falta reenviarlo.
    ///
    /// Se llama DESPUÉS de procesarlo, nunca antes: confirmar primero convierte
    /// la entrega en «como mucho una vez», es decir, en perder notificaciones
    /// cada vez que el proceso se caiga a medias.
    /// </summary>
    Task AcknowledgeAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
