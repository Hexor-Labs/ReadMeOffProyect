using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HubNegocios.TenantService.Infrastructure.Persistence;

/// <summary>
/// Cómo construye el contexto la herramienta de migraciones.
///
/// Sin esto, <c>dotnet ef</c> arranca el <c>Program.cs</c> entero para
/// encontrar el contexto — y ese arranque exige clave de firma del JWT y
/// cadena de conexión real. Generar una migración no necesita ninguna de las
/// dos: no se conecta a nada, solo lee el modelo. Esta fábrica le da lo mínimo.
///
/// La cadena de aquí es de diseño, no de ejecución. Se puede apuntar a otra con
/// la variable <c>TENANT_DB_CONNECTION</c>.
/// </summary>
public sealed class TenantDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TENANT_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=tenant_db;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TenantDbContext(options);
    }
}
