using HubNegocios.IdentityService.Domain.Entities;

namespace HubNegocios.IdentityService.Domain.Ports;

/// <summary>
/// Puerto de salida hacia el almacenamiento.
///
/// Un solo puerto para usuarios, sesiones y permisos, y no uno por entidad, por
/// una razón concreta: hay un único <c>SaveChangesAsync</c>. Con tres
/// repositorios sobre el mismo <c>DbContext</c>, cada uno con su método de
/// guardar, no habría forma de leer en el código qué entra en la misma
/// transacción — y en el canje de refresh token, borrar la sesión vieja y crear
/// la nueva tienen que ir juntas o se queda un token de más o ninguno.
///
/// Habla de entidades, no de <c>IQueryable</c> ni <c>DbSet</c>: en cuanto asoma
/// un tipo de EF Core, los casos de uso dejan de poder probarse sin base de
/// datos.
///
/// Todas las lecturas están ya acotadas al tenant en curso por el filtro global
/// del <c>DbContext</c>, así que ningún método recibe el tenant como parámetro.
/// Eso es deliberado: un parámetro <c>tenantId</c> aquí sería algo que alguien
/// puede pasar mal.
/// </summary>
public interface IIdentityRepository
{
    Task<User?> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Busca por correo dentro del tenant en curso.</summary>
    Task<User?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    void AddUser(User user);

    /// <summary>Localiza la sesión por el hash del refresh token. Nunca por el token.</summary>
    Task<Session?> GetSessionByRefreshTokenHashAsync(
        string refreshTokenHash,
        CancellationToken cancellationToken = default);

    void AddSession(Session session);

    /// <summary>Invalidar una sesión es quitarla: no hay estado intermedio.</summary>
    void RemoveSession(Session session);

    Task<bool> RoleHasPermissionAsync(
        UserRole role,
        string permissionCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirma lo pendiente. Sin <c>Update</c>: las entidades que salen de aquí
    /// están rastreadas, así que basta con cambiarlas y guardar.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
