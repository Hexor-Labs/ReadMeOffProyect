using HubNegocios.IdentityService.Domain.Entities;
using HubNegocios.IdentityService.Domain.Ports;

using Microsoft.EntityFrameworkCore;

namespace HubNegocios.IdentityService.Infrastructure.Persistence;

/// <summary>
/// Adaptador de salida: implementa el puerto del dominio con EF Core.
///
/// Ninguna consulta de esta clase menciona el tenant, y no es un olvido: el
/// filtro global lo añade EF Core a todas. Escribirlo a mano además de tenerlo
/// global sería redundante y, peor, daría a entender que hay consultas que se
/// filtran y otras que no.
/// </summary>
public sealed class IdentityRepository(IdentityDbContext context) : IIdentityRepository
{
    public Task<User?> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        // Con rastreo: quien llama a esto es el login, y va a tocar el contador de
        // intentos o la fecha del último acceso de la fila que reciba.
        context.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
        context.Users.AsNoTracking().AnyAsync(u => u.Email == email, cancellationToken);

    public void AddUser(User user) => context.Users.Add(user);

    public Task<Session?> GetSessionByRefreshTokenHashAsync(
        string refreshTokenHash,
        CancellationToken cancellationToken = default) =>
        context.Sessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == refreshTokenHash, cancellationToken);

    public void AddSession(Session session) => context.Sessions.Add(session);

    public void RemoveSession(Session session) => context.Sessions.Remove(session);

    /// <summary>
    /// Una sola consulta con join en vez de dos viajes.
    ///
    /// Esto lo llaman los demás servicios en línea con sus propias peticiones, así
    /// que su latencia se suma a la de ellos: dos idas y venidas a Postgres por
    /// cada comprobación de permiso se notarían en todo el hub.
    /// </summary>
    public Task<bool> RoleHasPermissionAsync(
        UserRole role,
        string permissionCode,
        CancellationToken cancellationToken = default) =>
        context.RolePermissions
            .AsNoTracking()
            .Join(
                context.Permissions.AsNoTracking(),
                rolePermission => rolePermission.PermissionId,
                permission => permission.Id,
                (rolePermission, permission) => new { rolePermission.Role, permission.Code })
            .AnyAsync(x => x.Role == role && x.Code == permissionCode, cancellationToken);

    /// <summary>
    /// Un solo <c>SaveChanges</c> ya es atómico: EF Core envuelve todos los cambios
    /// pendientes en una transacción implícita. Por eso la sesión nueva, el borrado
    /// de la vieja y el evento de la outbox entran o no entran juntos, sin abrir la
    /// transacción a mano.
    /// </summary>
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
