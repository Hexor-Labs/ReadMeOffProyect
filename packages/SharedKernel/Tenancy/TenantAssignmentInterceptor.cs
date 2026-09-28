using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HubNegocios.SharedKernel.Tenancy;

/// <summary>
/// Pone el tenant a las entidades nuevas y comprueba que nadie escriba en el
/// tenant de otro.
///
/// La parte de asignar es comodidad. La de comprobar es seguridad: los filtros
/// globales de EF Core protegen las LECTURAS, pero no dicen nada sobre las
/// escrituras. Sin esta comprobación, un caso de uso que reciba un id de otro
/// tenant y guarde el objeto lo escribiría sin protestar; RLS lo rechazaría en
/// la base de datos, pero con un error de Postgres opaco en vez de uno que
/// explique qué pasó.
/// </summary>
public sealed class TenantAssignmentInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var current = TenantContext.Current.TenantId;

        foreach (var entry in context.ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var property = entry.Property(nameof(ITenantOwned.TenantId));

            if (entry.State == EntityState.Added && IsUnset(property))
            {
                property.CurrentValue = current ?? throw new InvalidOperationException(
                    $"Se intenta guardar {entry.Entity.GetType().Name} sin tenant en el " +
                    "contexto de ejecución. Abre un TenantContext.BeginScope antes de guardar.");
                continue;
            }

            if (current is not null && !Equals(property.CurrentValue, current))
            {
                throw new InvalidOperationException(
                    $"Se intenta escribir {entry.Entity.GetType().Name} del tenant " +
                    $"{property.CurrentValue} desde el tenant {current}. Operación cancelada.");
            }
        }
    }

    private static bool IsUnset(PropertyEntry property) =>
        property.CurrentValue is not Guid value || value == Guid.Empty;
}
