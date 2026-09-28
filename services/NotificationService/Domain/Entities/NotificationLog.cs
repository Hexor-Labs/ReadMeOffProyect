using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.NotificationService.Domain.Entities;

/// <summary>En qué acabó el intento de envío.</summary>
public enum NotificationStatus
{
    Pending,
    Sent,
    Failed,
}

/// <summary>
/// Una línea por evento atendido: qué se intentó mandar, a quién y cómo acabó.
///
/// Esta tabla hace dos trabajos y conviene no perder de vista el segundo.
///
/// 1. Es el rastro que permite responder «¿le llegó el correo al cliente?».
/// 2. Es EL MECANISMO DE IDEMPOTENCIA del servicio. Los eventos se entregan «al
///    menos una vez», así que <c>order.created</c> puede llegar dos veces; con
///    el índice único sobre (tenant_id, event_id) la segunda inserción no cabe
///    en la tabla, y el segundo correo no sale. La fila se escribe ANTES de
///    enviar, justo para eso: ver <c>SendNotificationHandler</c>.
///
/// Lo que NO se guarda aquí es el contenido enviado. Un log de notificaciones
/// con el cuerpo de cada mensaje acaba siendo una copia de los datos personales
/// de todos los clientes del hub, en una tabla que consulta más gente y que se
/// respalda a más sitios que la original.
/// </summary>
public sealed class NotificationLog : ITenantOwned
{
    /// <summary>Tope del mensaje de error, igual que en la outbox: las primeras líneas ya dicen qué pasó.</summary>
    public const int MaxErrorLength = 1000;

    /// <summary>Longitud de la clave de deduplicación en la columna.</summary>
    public const int MaxEventIdLength = 200;

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>
    /// Identidad del evento de origen, y clave de la deduplicación.
    ///
    /// Es texto y no el número de la outbox porque ese número solo es único
    /// dentro del servicio que lo emitió: el evento 42 de order-service y el 42
    /// de reservations-service son dos eventos distintos. Con el tipo delante, el
    /// par vuelve a ser único en todo el hub.
    /// </summary>
    public string EventId { get; private set; } = string.Empty;

    /// <summary>
    /// Destinatario, si el evento lo traía. Que sea nulo es uno de los motivos
    /// por los que una notificación acaba en <see cref="NotificationStatus.Failed"/>.
    /// </summary>
    public Guid? RecipientId { get; private set; }

    /// <summary>Canal usado. Nulo cuando no hubo plantilla activa y por tanto no había canal que elegir.</summary>
    public NotificationType? Channel { get; private set; }

    /// <summary>Plantilla aplicada. Nula por el mismo motivo que <see cref="Channel"/>.</summary>
    public Guid? TemplateId { get; private set; }

    public NotificationStatus Status { get; private set; }

    /// <summary>Solo se rellena cuando el canal confirmó el envío.</summary>
    public DateTime? SentAt { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Cuándo se atendió el evento. No es la auditoría de <c>IAuditable</c>: la
    /// fila nace antes del envío, así que sin esta fecha una fila que acabó en
    /// <see cref="NotificationStatus.Failed"/> —que nunca tiene
    /// <see cref="SentAt"/>— no tendría ninguna fecha por la que ordenarla.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    private NotificationLog()
    {
    }

    /// <summary>
    /// Aparta el evento antes de intentar el envío.
    ///
    /// Nace en <see cref="NotificationStatus.Pending"/> y se guarda de inmediato:
    /// es esa escritura, y no una comprobación en memoria, la que impide que dos
    /// entregas del mismo evento manden dos mensajes.
    /// </summary>
    public static NotificationLog Claim(
        Guid id,
        string eventId,
        Guid? recipientId,
        NotificationType? channel,
        Guid? templateId,
        DateTime createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);

        return new NotificationLog
        {
            Id = id,
            EventId = eventId,
            RecipientId = recipientId,
            Channel = channel,
            TemplateId = templateId,
            Status = NotificationStatus.Pending,
            CreatedAt = createdAt,
        };
    }

    public void MarkSent(DateTime at)
    {
        Status = NotificationStatus.Sent;
        SentAt = at;
        ErrorMessage = null;
    }

    public void MarkFailed(string error)
    {
        ArgumentNullException.ThrowIfNull(error);

        Status = NotificationStatus.Failed;
        SentAt = null;
        ErrorMessage = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
    }

    /// <summary>Clave de deduplicación de un evento del bus: tipo y número de origen.</summary>
    public static string EventIdFor(string eventType, long sourceEventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        return $"{eventType}#{sourceEventId}";
    }
}
