using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HubNegocios.CatalogService.Infrastructure.Persistence;

/// <summary>
/// Cómo construye el contexto la herramienta de migraciones.
///
/// Sin esto, <c>dotnet ef</c> arranca el <c>Program.cs</c> entero para encontrar
/// el contexto — y ese arranque exige clave de firma del JWT y cadena de
/// conexión real. Generar una migración no necesita ninguna de las dos: no se
/// conecta a nada, solo lee el modelo. Esta fábrica le da lo mínimo.
///
/// La cadena de aquí es de diseño, no de ejecución. Se puede apuntar a otra con
/// la variable <c>CATALOG_DB_CONNECTION</c>.
/// </summary>
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CATALOG_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=catalog_db;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new CatalogDbContext(options);
    }
}
