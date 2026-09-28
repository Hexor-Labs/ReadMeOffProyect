using HubNegocios.IdentityService.Domain.Entities;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HubNegocios.IdentityService.Infrastructure.Persistence;

/// <summary>
/// Base de datos de identity-service (<c>identity_db</c>).
///
/// Suya y de nadie más: si otro servicio necesita saber el rol de alguien, lo
/// pregunta por API —para eso está <c>CheckPermission</c>— o se entera por
/// evento. Un JOIN contra esta base desde otro servicio convertiría el hub en un
/// monolito distribuido, y además pondría la tabla de credenciales al alcance de
/// diez cadenas de conexión en vez de una.
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        modelBuilder.ApplyOutbox();

        /*
          Aquí SÍ se llama a ApplyTenantFilters(), al contrario que en
          tenant-service. Es la diferencia de fondo: allí el tenant era la fila,
          aquí el tenant es el filtro. Todas las entidades de este servicio
          implementan ITenantOwned, así que todas quedan acotadas al tenant en
          curso sin que ningún repositorio tenga que acordarse de filtrar.

          Sobre esta tabla en particular importa el doble. Un olvido en un Where de
          `users` no devuelve datos de más de un cliente: devuelve credenciales y
          correos de todos los clientes del hub.
        */
        modelBuilder.ApplyTenantFilters();

        base.OnModelCreating(modelBuilder);
    }
}
