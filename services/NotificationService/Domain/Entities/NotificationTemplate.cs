using System.Text.RegularExpressions;

using HubNegocios.NotificationService.Domain.ValueObjects;
using HubNegocios.SharedKernel.Auditing;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.NotificationService.Domain.Entities;

/// <summary>Canal por el que sale la notificación.</summary>
public enum NotificationType
{
    Email,
    Sms,
    Push,
}

/// <summary>
/// La plantilla que un negocio configura para un evento concreto.
///
/// Es lo único que este servicio deja editar: el contenido de lo que se manda
/// es del negocio, y el cuándo lo decide el evento. Un texto por tenant y no
/// uno global es lo que permite que una peluquería y un restaurante usen el
/// mismo <c>order.created</c> y escriban cosas distintas.
/// </summary>
public sealed partial class NotificationTemplate : ITenantOwned, IAuditable
{
    /// <summary>Tope del cuerpo. Un SMS son 160 caracteres; un correo razonable, mucho menos que esto.</summary>
    public const int MaxBodyLength = 4000;

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>
    /// Evento que dispara la plantilla, con el mismo nombre con el que viaja por
    /// el bus: <c>order.created</c>, <c>reservation.confirmed</c>. Se guarda tal
    /// cual, sin traducir a un enum, porque el catálogo de eventos del hub crece
    /// cada vez que se añade un servicio y un enum obligaría a desplegar este
    /// servicio para poder escuchar un evento nuevo.
    /// </summary>
    public string EventTrigger { get; private set; } = string.Empty;

    /// <summary>Asunto del correo, o título de la notificación en los otros canales.</summary>
    public string Subject { get; private set; } = string.Empty;

    /// <summary>Cuerpo con marcadores tipo <c>{{customer_name}}</c>.</summary>
    public string BodyTemplate { get; private set; } = string.Empty;

    /// <summary>
    /// Solo la plantilla activa se usa. Desactivar en vez de borrar deja el
    /// historial del log apuntando a algo que todavía existe: una fila que
    /// referencia una plantilla borrada no se puede investigar.
    /// </summary>
    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    private NotificationTemplate()
    {
    }

    public static NotificationTemplate Create(
        Guid id,
        NotificationType type,
        string eventTrigger,
        string subject,
        string bodyTemplate,
        bool isActive)
    {
        var trigger = Normalize(eventTrigger);

        if (!TriggerPattern().IsMatch(trigger))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["eventTrigger"] = ["El evento se nombra en punto y en minúsculas, como order.created."],
            });
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["subject"] = ["Una plantilla sin asunto llega al cliente sin nada en la cabecera."],
            });
        }

        if (string.IsNullOrWhiteSpace(bodyTemplate) || bodyTemplate.Length > MaxBodyLength)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["bodyTemplate"] = [$"El cuerpo es obligatorio y no puede pasar de {MaxBodyLength} caracteres."],
            });
        }

        return new NotificationTemplate
        {
            Id = id,
            Type = type,
            EventTrigger = trigger,
            Subject = subject.Trim(),
            BodyTemplate = bodyTemplate.Trim(),
            IsActive = isActive,
        };
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>
    /// Sustituye los marcadores con los datos del evento. La plantilla es de
    /// quien sabe qué hay que decir; cómo se rellena es cosa del dominio.
    /// </summary>
    public RenderedMessage Render(IReadOnlyDictionary<string, string> values) =>
        RenderedMessage.From(Subject, BodyTemplate, values);

    /// <summary>Normaliza el nombre del evento para que <c>Order.Created </c> y <c>order.created</c> sean el mismo.</summary>
    public static string Normalize(string? eventTrigger) =>
        eventTrigger?.Trim().ToLowerInvariant() ?? string.Empty;

    [GeneratedRegex(@"^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TriggerPattern();
}
