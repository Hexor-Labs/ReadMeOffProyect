using HubNegocios.IdentityService.Domain.ValueObjects;
using HubNegocios.SharedKernel.Auditing;
using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.IdentityService.Domain.Entities;

/// <summary>
/// Rol del usuario dentro de UN negocio.
///
/// El rol es del par (usuario, tenant), no de la persona: la misma dirección de
/// correo puede ser <c>Owner</c> de su restaurante y <c>Customer</c> del hotel
/// de al lado. Por eso vive en la fila de <see cref="User"/>, que ya está
/// acotada por tenant, y no en una tabla de personas global.
/// </summary>
public enum UserRole
{
    Owner,
    Admin,
    Staff,
    Customer,
}

/// <summary>
/// Una cuenta de acceso dentro de un tenant.
///
/// A diferencia de <c>Tenant</c> en tenant-service, esta entidad SÍ implementa
/// <see cref="ITenantOwned"/>: aquí el tenant es el filtro de todo. Eso es lo
/// que hace que <c>ApplyTenantFilters()</c> le ponga el filtro global y que
/// ninguna consulta de este servicio pueda ver usuarios de otro negocio por
/// olvidar un <c>Where</c>.
///
/// El dominio no sabe hashear ni firmar nada: recibe el hash ya calculado. Meter
/// BCrypt aquí ataría la entidad a un paquete de infraestructura y obligaría a
/// los tests de dominio a pagar el coste de un hash real —que, siendo de factor
/// 12, es justo el coste que se busca al hashear y el que no se quiere en una
/// suite de pruebas—.
/// </summary>
public sealed class User : ITenantOwned, IAuditable
{
    /// <summary>
    /// Intentos fallidos consecutivos que se toleran antes de bloquear.
    ///
    /// El bloqueo temporal es lo que convierte una contraseña débil en algo que
    /// no se puede adivinar a base de fuerza bruta: cinco intentos cada quince
    /// minutos son 480 al día, nada frente a un diccionario.
    /// </summary>
    public const int MaxLoginAttempts = 5;

    /// <summary>Cuánto dura el bloqueo. Temporal a propósito: un bloqueo permanente convierte el ataque en una denegación de servicio contra el dueño de la cuenta.</summary>
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string FullName { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    /// <summary>
    /// Hash de la contraseña, nunca la contraseña. No sale de aquí hacia ningún
    /// log, respuesta ni evento.
    /// </summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>
    /// Cuándo se fijó la contraseña vigente. Es lo que permite responder «¿esta
    /// cuenta seguía usando la contraseña de antes de la fuga?» sin adivinar.
    /// </summary>
    public DateTime PasswordLastChanged { get; private set; }

    /// <summary>Identificador de la cuenta en el proveedor externo, si la cuenta vino de uno.</summary>
    public string? ExternalId { get; private set; }

    /// <summary>Nombre del proveedor externo (<c>google</c>, <c>facebook</c>…).</summary>
    public string? ExternalProvider { get; private set; }

    public string? AvatarUrl { get; private set; }

    public UserRole Role { get; private set; } = UserRole.Customer;

    public bool IsActive { get; private set; } = true;

    public DateTime? LastLogin { get; private set; }

    public int LoginAttempts { get; private set; }

    public DateTime? LockedUntil { get; private set; }

    /// <summary>Cuándo aceptó los términos. Nulo significa que no consta: es un dato legal, no un booleano.</summary>
    public DateTime? TermsAcceptedAt { get; private set; }

    public DateTime? PrivacyPolicyAcceptedAt { get; private set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    private User()
    {
    }

    /// <param name="tenantId">
    /// Se pasa explícito en vez de dejar que lo rellene
    /// <c>TenantAssignmentInterceptor</c> al guardar: el caso de uso necesita el
    /// tenant antes de guardar, para el evento de la outbox, y una entidad a
    /// medio construir con <c>TenantId = Guid.Empty</c> es un estado que no
    /// merece existir ni un instante.
    /// </param>
    public static User Register(
        Guid id,
        Guid tenantId,
        Email email,
        string fullName,
        string? phone,
        string passwordHash,
        UserRole role,
        DateTime now,
        bool acceptsTerms,
        bool acceptsPrivacyPolicy,
        string? avatarUrl = null,
        string? externalProvider = null,
        string? externalId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        return new User
        {
            Id = id,
            TenantId = tenantId,
            Email = email.Value,
            FullName = fullName.Trim(),
            Phone = Limpiar(phone),
            PasswordHash = passwordHash,
            PasswordLastChanged = now,
            Role = role,
            IsActive = true,
            LoginAttempts = 0,
            AvatarUrl = Limpiar(avatarUrl),
            ExternalProvider = Limpiar(externalProvider)?.ToLowerInvariant(),
            ExternalId = Limpiar(externalId),
            TermsAcceptedAt = acceptsTerms ? now : null,
            PrivacyPolicyAcceptedAt = acceptsPrivacyPolicy ? now : null,
        };
    }

    /// <summary>¿Está la cuenta bloqueada en este instante?</summary>
    public bool IsLockedAt(DateTime now) => LockedUntil is not null && LockedUntil > now;

    /// <summary>
    /// Anota un intento fallido y bloquea al llegar al límite.
    ///
    /// Si el bloqueo anterior ya venció se reinicia la cuenta de intentos antes
    /// de sumar. Sin ese reinicio, un contador que se quedó en cinco haría que el
    /// primer error tras cumplir el castigo volviera a bloquear la cuenta: quien
    /// se equivoca una vez al teclear no merece otros quince minutos.
    /// </summary>
    public void RegisterFailedLogin(DateTime now)
    {
        if (LockedUntil is not null && LockedUntil <= now)
        {
            LoginAttempts = 0;
            LockedUntil = null;
        }

        LoginAttempts++;

        if (LoginAttempts >= MaxLoginAttempts)
        {
            LockedUntil = now + LockDuration;
        }
    }

    /// <summary>
    /// Anota una entrada correcta y limpia el castigo.
    ///
    /// Reiniciar el contador al acertar es lo que evita que los errores sueltos
    /// de meses distintos se acumulen hasta bloquear a alguien que nunca sufrió
    /// un ataque.
    /// </summary>
    public void RegisterSuccessfulLogin(DateTime now)
    {
        LoginAttempts = 0;
        LockedUntil = null;
        LastLogin = now;
    }

    /// <summary>Cambia el rol. Devuelve el anterior para que el caso de uso pueda registrarlo.</summary>
    public UserRole ChangeRole(UserRole role)
    {
        var previous = Role;
        Role = role;
        return previous;
    }

    private static string? Limpiar(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
