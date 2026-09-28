using System.Diagnostics;

namespace HubNegocios.SharedKernel.Tenancy;

/// <summary>
/// Identidad de quien ejecuta la operación en curso: de qué tenant es, quién
/// es el usuario y con qué correlación viaja la petición.
/// </summary>
/// <param name="TenantId">
/// Nulo solo en dos casos legítimos: rutas anónimas (health checks, landing
/// pública) y el propio tenant-service, cuya tabla <c>tenants</c> ES el tenant.
/// </param>
public sealed record TenantIdentity(Guid? TenantId, Guid? UserId, string? Role, Guid CorrelationId)
{
    /// <summary>Para trabajos de fondo que aún no han resuelto un tenant.</summary>
    public static TenantIdentity None { get; } = new(null, null, null, Guid.Empty);

    /// <summary>
    /// El tenant, o una excepción si no hay. Úsalo donde el código no tiene
    /// sentido sin tenant: es preferible fallar aquí que escribir una fila
    /// con <c>TenantId = Guid.Empty</c> que nadie volverá a encontrar.
    /// </summary>
    public Guid RequireTenantId() =>
        TenantId ?? throw new InvalidOperationException(
            "No hay tenant en el contexto de ejecución. Si es una ruta anónima marca el " +
            "endpoint como tal; si es un consumidor de eventos, abre un TenantContext.Scope " +
            "con el tenant del evento antes de tocar la base de datos.");
}

/// <summary>
/// El tenant vigente, guardado en el contexto de ejecución.
///
/// La regla que impone este diseño —y que el documento técnico llama «no
/// negociable»— es que el tenant se resuelve UNA vez en el borde y de ahí en
/// adelante viaja solo. Nunca es un parámetro que cada desarrollador tenga
/// que acordarse de pasar, porque el día que a alguien se le olvide, la
/// consulta devolverá datos de otro cliente.
///
/// Se apoya en <see cref="AsyncLocal{T}"/>, que sigue al flujo asíncrono: lo
/// que el middleware fija al empezar la petición sigue ahí después de
/// cualquier <c>await</c>, y no se filtra a peticiones vecinas.
/// </summary>
public static class TenantContext
{
    private static readonly AsyncLocal<TenantIdentity?> Ambient = new();

    /// <summary>Identidad vigente; <see cref="TenantIdentity.None"/> si nadie la fijó.</summary>
    public static TenantIdentity Current => Ambient.Value ?? TenantIdentity.None;

    /// <summary>
    /// Fija la identidad mientras viva el objeto devuelto. Pensado para
    /// <c>using</c>: en el borde HTTP y al consumir un evento, que son los dos
    /// únicos sitios donde un tenant entra al sistema.
    /// </summary>
    public static IDisposable BeginScope(TenantIdentity identity)
    {
        var previous = Ambient.Value;
        Ambient.Value = identity;
        return new Scope(previous);
    }

    /// <summary>Atajo para consumidores de eventos, que solo traen tenant y correlación.</summary>
    public static IDisposable BeginScope(Guid tenantId, Guid correlationId) =>
        BeginScope(new TenantIdentity(tenantId, null, null, correlationId));

    private sealed class Scope(TenantIdentity? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Ambient.Value = previous;
        }
    }

    /// <summary>
    /// Correlación vigente para los logs. Si la petición no trajo una, se usa
    /// la de <see cref="Activity"/>, que es la que ya propaga OpenTelemetry.
    /// </summary>
    public static string TraceId =>
        Activity.Current?.TraceId.ToString()
        ?? (Current.CorrelationId == Guid.Empty ? "sin-trace" : Current.CorrelationId.ToString());
}
