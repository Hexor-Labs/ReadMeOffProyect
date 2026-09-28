using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HubNegocios.SharedKernel.Tenancy;

/// <summary>
/// Fija <c>app.tenant_id</c> en cada conexión que se abre, que es de donde
/// leen las políticas de Row-Level Security de Postgres.
///
/// Este es el cinturón de la seguridad multi-tenant; los filtros globales de
/// EF Core son los tirantes. Si alguien escribe una consulta con
/// <c>IgnoreQueryFilters()</c>, o entra por <c>FromSqlRaw</c>, o se equivoca en
/// un <c>Where</c>, RLS sigue tapando el agujero porque actúa dentro del motor
/// de la base de datos, no en el código de la aplicación.
///
/// Dos detalles que parecen menores y no lo son:
///
/// 1. El valor va como PARÁMETRO de <c>set_config</c>, nunca concatenado en el
///    SQL. Un Guid no puede inyectar nada, pero la costumbre de concatenar es
///    la que un día se aplica a algo que sí puede.
/// 2. Cuando no hay tenant se limpia el valor en vez de dejarlo como estaba.
///    Las conexiones vienen de un pool: si no se limpiara, una petición
///    anónima heredaría el tenant de la petición anterior que usó esa misma
///    conexión física. Ese es exactamente el fallo que permite leer datos de
///    otro cliente.
///
/// Las políticas deben escribirse tolerando el valor vacío:
/// <code>
/// CREATE POLICY tenant_isolation ON items
///   USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
/// </code>
/// Con <c>NULLIF</c>, sin tenant la comparación da NULL y no devuelve filas:
/// el estado por defecto es negar, no mostrar todo.
/// </summary>
public sealed class TenantConnectionInterceptor : DbConnectionInterceptor
{
    private const string SetTenantSql = "SELECT set_config('app.tenant_id', @tenant, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DbCommand CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = SetTenantSql;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";

        // Cadena vacía —y no NULL— cuando no hay tenant: set_config rechaza NULL.
        parameter.Value = TenantContext.Current.TenantId?.ToString() ?? string.Empty;

        command.Parameters.Add(parameter);
        return command;
    }
}
