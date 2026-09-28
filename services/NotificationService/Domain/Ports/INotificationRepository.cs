using HubNegocios.NotificationService.Domain.Entities;

namespace HubNegocios.NotificationService.Domain.Ports;

/// <summary>
/// Acceso a las plantillas y al log del tenant en curso.
///
/// Ninguna firma recibe el tenant: lo aplican el filtro global de EF Core y las
/// políticas de RLS a partir del contexto de ejecución. Pasarlo a mano aquí
/// sería volver a abrir la puerta a consultar el tenant equivocado —y en un
/// consumidor de eventos, que atiende eventos de todos los tenants a lo largo del
/// día, ese error sería especialmente fácil de cometer—.
/// </summary>
public interface INotificationRepository
{
    /// <summary>
    /// La plantilla activa de un evento. Una como máximo: lo garantiza el índice
    /// único parcial de la tabla, porque con dos activas «la plantilla del
    /// evento» dependería del orden en que la base devolviera las filas.
    /// </summary>
    Task<NotificationTemplate?> GetActiveTemplateForEventAsync(
        string eventTrigger,
        CancellationToken cancellationToken = default);

    Task<NotificationTemplate?> GetTemplateByIdAsync(Guid templateId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationTemplate>> ListTemplatesAsync(
        bool onlyActive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Si ya hay otra plantilla activa para ese evento. Sirve para dar un error
    /// entendible antes de que lo dé el índice único con un mensaje de Postgres.
    /// </summary>
    Task<bool> ActiveTemplateExistsAsync(
        string eventTrigger,
        Guid exceptTemplateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Si el evento ya se atendió. Es la primera barrera de la idempotencia; la
    /// que de verdad garantiza el «una sola vez» es el índice único de la tabla.
    /// </summary>
    Task<bool> LogExistsForEventAsync(string eventId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<NotificationLog> Entries, int Total)> GetLogAsync(
        NotificationStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    void AddTemplate(NotificationTemplate template);

    void AddLog(NotificationLog log);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
