using HubNegocios.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HubNegocios.SharedKernel.Auditing;

/// <summary>
/// Rellena <c>CreatedAt/UpdatedAt/CreatedBy/UpdatedBy</c> al guardar.
///
/// Va en un interceptor y no en cada caso de uso por lo de siempre: lo que hay
/// que recordar hacer en veinte sitios se olvida en el veintiuno, y una fecha
/// de auditoría que falta solo se descubre el día que hace falta para
/// investigar algo.
///
/// Todas las fechas en UTC. Guardar hora local en un sistema multi-tenant que
/// puede acabar sirviendo a varios husos es una deuda que se paga cara.
/// </summary>
/// <param name="clock">
/// Inyectado para poder fijar el tiempo en los tests. Un interceptor que llama
/// a <c>DateTime.UtcNow</c> directamente no se puede probar.
/// </param>
public sealed class AuditInterceptor(TimeProvider clock) : SaveChangesInterceptor
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

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var actor = TenantContext.Current.UserId;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = actor;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = actor;

                    // Que una actualización no pueda reescribir el origen.
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                    break;

                default:
                    break;
            }
        }
    }
}
