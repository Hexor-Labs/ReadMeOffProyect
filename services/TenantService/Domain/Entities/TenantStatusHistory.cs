namespace HubNegocios.TenantService.Domain.Entities;

/// <summary>
/// Rastro de cada cambio de estado de un tenant.
///
/// El Prompt Base lo pide para toda entidad cuyo estado importe, y tiene
/// sentido: el campo <c>Status</c> dice cómo está el tenant ahora, pero la
/// pregunta que llega un martes por la tarde es «¿por qué está suspendido y
/// quién lo hizo?». Esa respuesta no está en la fila actual, y sin historial no
/// está en ninguna parte.
///
/// Solo se inserta. Nada actualiza ni borra estas filas.
/// </summary>
public sealed class TenantStatusHistory
{
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public TenantStatus OldValue { get; private set; }
    public TenantStatus NewValue { get; private set; }

    public Guid? ChangedBy { get; private set; }
    public DateTime ChangedAt { get; private set; }

    public string? Reason { get; private set; }

    private TenantStatusHistory()
    {
    }

    public static TenantStatusHistory Record(
        Guid id,
        Guid tenantId,
        TenantStatus oldValue,
        TenantStatus newValue,
        Guid? changedBy,
        DateTime changedAt,
        string? reason) => new()
        {
            Id = id,
            TenantId = tenantId,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedBy = changedBy,
            ChangedAt = changedAt,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        };
}
