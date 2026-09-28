namespace HubNegocios.SharedKernel.Tenancy;

/// <summary>
/// Marca una entidad que pertenece a un tenant.
///
/// Implementarla es lo que hace que <c>ModelBuilderExtensions.ApplyTenantFilter</c>
/// le ponga el filtro global automáticamente. Una entidad de negocio que no
/// implemente esta interfaz queda sin filtro y sin la comprobación de escritura:
/// revisa dos veces antes de dejar una fuera.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
