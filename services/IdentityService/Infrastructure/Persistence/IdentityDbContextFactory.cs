using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HubNegocios.IdentityService.Infrastructure.Persistence;

/// <summary>
/// Cómo construye el contexto la herramienta de migraciones.
///
/// Sin esto, <c>dotnet ef</c> arranca el <c>Program.cs</c> entero para encontrar
/// el contexto, y ese arranque exige clave de firma del JWT y cadena de conexión
/// real. Generar una migración no necesita ninguna de las dos: no se conecta a
/// nada, solo lee el modelo. Esta fábrica le da lo mínimo.
///
/// La cadena de aquí es de diseño, no de ejecución. Se puede apuntar a otra con
/// la variable <c>IDENTITY_DB_CONNECTION</c>.
/// </summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("IDENTITY_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=identity_db;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new IdentityDbContext(options);
    }
}
