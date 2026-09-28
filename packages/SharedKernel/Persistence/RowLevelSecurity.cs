using Microsoft.EntityFrameworkCore.Migrations;

namespace HubNegocios.SharedKernel.Persistence;

/// <summary>
/// Activa Row-Level Security sobre una tabla desde una migración.
///
/// EF Core no sabe nada de RLS, así que el SQL va a mano. Se centraliza aquí
/// porque la política tiene dos detalles que, escritos diez veces, se
/// equivocan al menos una:
///
/// 1. <c>NULLIF(current_setting('app.tenant_id', true), '')::uuid</c>. El
///    segundo argumento de <c>current_setting</c> hace que devuelva NULL en vez
///    de reventar cuando el ajuste no existe, y el <c>NULLIF</c> convierte la
///    cadena vacía —lo que deja el interceptor cuando no hay tenant— también en
///    NULL. Comparar contra NULL no devuelve filas: sin tenant, no se ve nada.
///    Escrito sin esas dos defensas, una conexión sin tenant aborta la consulta
///    con un error de casteo, o peor, alguien «arregla» el error quitando la
///    política.
///
/// 2. <c>FORCE ROW LEVEL SECURITY</c> se aplica o no según quién deba poder
///    saltársela. El dueño de la tabla ignora RLS por defecto; eso es justo lo
///    que permite que las migraciones y el publicador de outbox funcionen. La
///    aplicación debe conectarse con un rol distinto y sin privilegios de
///    dueño: si el servicio se conecta como dueño de la tabla, RLS no protege
///    absolutamente nada.
/// </summary>
public static class RowLevelSecurity
{
    /// <summary>Activa el aislamiento por tenant en una tabla.</summary>
    public static void EnableTenantIsolation(this MigrationBuilder migrationBuilder, string table, string tenantColumn = "tenant_id")
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");

        migrationBuilder.Sql($"""
            CREATE POLICY tenant_isolation ON {table}
              USING ({tenantColumn} = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
              WITH CHECK ({tenantColumn} = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """);
    }

    public static void DisableTenantIsolation(this MigrationBuilder migrationBuilder, string table)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {table};");
        migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
    }
}
