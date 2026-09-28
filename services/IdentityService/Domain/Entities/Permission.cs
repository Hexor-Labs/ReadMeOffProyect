using HubNegocios.SharedKernel.Auditing;
using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.IdentityService.Domain.Entities;

/// <summary>
/// Un permiso con nombre: <c>orders.read</c>, <c>reservations.cancel</c>.
///
/// Es del tenant y no del sistema porque cada vertical tiene sus propias
/// acciones, y un catálogo global obligaría a que el permiso de cancelar una
/// reserva existiera también en la ferretería que no reserva nada.
/// </summary>
public sealed class Permission : ITenantOwned, IAuditable
{
    public const int MaxCodeLength = 100;

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>
    /// El código con el que lo piden los demás servicios. Normalizado a
    /// minúsculas: es un identificador que viaja por HTTP entre servicios, y un
    /// permiso que funciona o no según cómo lo escriba quien llama es un fallo
    /// de autorización esperando a pasar.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    private Permission()
    {
    }

    public static Permission Create(Guid id, Guid tenantId, string code, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return new Permission
        {
            Id = id,
            TenantId = tenantId,
            Code = Normalize(code),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        };
    }

    /// <summary>La forma canónica de un código. Se usa también al consultar, para que la búsqueda y el dato guardado coincidan.</summary>
    public static string Normalize(string code) => code.Trim().ToLowerInvariant();
}
