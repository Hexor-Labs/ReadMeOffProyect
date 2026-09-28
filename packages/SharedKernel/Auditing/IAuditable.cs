namespace HubNegocios.SharedKernel.Auditing;

/// <summary>
/// Sello de auditoría. Lo rellena <see cref="AuditInterceptor"/> al guardar,
/// para que ningún caso de uso tenga que acordarse de poner las fechas.
/// </summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    Guid? UpdatedBy { get; set; }
}
