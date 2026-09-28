using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.IdentityService.Domain.Entities;

/// <summary>
/// Qué permisos tiene cada rol dentro de un tenant.
///
/// La concesión es por rol y no por usuario a propósito: un permiso suelto
/// pegado a una persona es invisible en cuanto hay veinte empleados, y nadie
/// sabe explicar por qué aquel camarero podía cerrar la caja. Con la tabla por
/// rol, la respuesta a «quién puede hacer esto» es una consulta y no una
/// arqueología.
///
/// La clave es <c>(tenant_id, role, permission_id)</c>: el mismo rol puede tener
/// permisos distintos en negocios distintos, que es justo lo que permite que el
/// <c>Staff</c> de un hotel no herede lo que puede hacer el de un restaurante.
/// </summary>
public sealed class RolePermission : ITenantOwned
{
    public Guid TenantId { get; private set; }

    public UserRole Role { get; private set; }

    public Guid PermissionId { get; private set; }

    private RolePermission()
    {
    }

    public static RolePermission Grant(Guid tenantId, UserRole role, Guid permissionId) => new()
    {
        TenantId = tenantId,
        Role = role,
        PermissionId = permissionId,
    };
}
