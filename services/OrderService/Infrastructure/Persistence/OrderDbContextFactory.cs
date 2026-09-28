using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HubNegocios.OrderService.Infrastructure.Persistence;

/// <summary>
/// Cómo construye el contexto la herramienta de migraciones. Sin esto,
/// <c>dotnet ef</c> arrancaría el Program.cs entero, que exige clave de firma y
/// conexión real; generar una migración no necesita ninguna de las dos.
/// </summary>
public sealed class OrderDbContextFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ORDER_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=order_db;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new OrderDbContext(options);
    }
}
