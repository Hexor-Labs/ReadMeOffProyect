using System.Linq.Expressions;
using System.Reflection;

using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace HubNegocios.SharedKernel.Persistence;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Pone el filtro global de tenant a toda entidad que implemente
    /// <see cref="ITenantOwned"/>.
    ///
    /// Se hace por reflexión y no entidad por entidad a propósito: escrito a
    /// mano, el filtro se olvida justo en la entidad que se añade con prisa un
    /// viernes. Así, implementar la interfaz es lo único que hay que recordar.
    ///
    /// Sobre cómo se construye el filtro: compara contra
    /// <c>TenantContext.Current.TenantId</c>, que es un acceso a un miembro
    /// ESTÁTICO. Esto importa más de lo que parece. El patrón habitual —guardar
    /// el tenant en un campo del DbContext y comparar contra él— captura la
    /// instancia del contexto dentro del modelo, y EF Core cachea el modelo por
    /// tipo de contexto: la segunda petición reutiliza un modelo que sigue
    /// apuntando al contexto de la primera, y acaba filtrando por el tenant
    /// equivocado. Sin instancia capturada no existe ese riesgo, y EF evalúa la
    /// expresión en cada ejecución.
    ///
    /// Recuerda que esto solo cubre las LECTURAS hechas con LINQ. Las escrituras
    /// las vigila <see cref="TenantAssignmentInterceptor"/>, y por debajo de
    /// todo está RLS de Postgres, que es lo único que sigue en pie si alguien
    /// usa <c>IgnoreQueryFilters()</c> o SQL a pelo.
    /// </summary>
    public static ModelBuilder ApplyTenantFilters(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var currentTenantId = CurrentTenantIdExpression();

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (!typeof(ITenantOwned).IsAssignableFrom(clrType))
            {
                continue;
            }

            /*
              La outbox se queda fuera. La lee un servicio de fondo que publica
              los eventos de TODOS los tenants y que, por definición, corre sin
              tenant en contexto: con filtro no vería ni una fila y los eventos
              no saldrían nunca.
            */
            if (clrType == typeof(OutboxEvent))
            {
                continue;
            }

            var parameter = Expression.Parameter(clrType, "e");

            var body = Expression.Equal(
                Expression.Convert(Expression.Property(parameter, nameof(ITenantOwned.TenantId)), typeof(Guid?)),
                currentTenantId);

            modelBuilder.Entity(clrType).HasQueryFilter(Expression.Lambda(body, parameter));
        }

        return modelBuilder;
    }

    /// <summary>Aplica el mapeo de la tabla de outbox, igual en los diez servicios.</summary>
    public static ModelBuilder ApplyOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new OutboxEventConfiguration());
        return modelBuilder;
    }

    private static MemberExpression CurrentTenantIdExpression()
    {
        var current = typeof(TenantContext).GetProperty(
            nameof(TenantContext.Current),
            BindingFlags.Public | BindingFlags.Static)!;

        var tenantId = typeof(TenantIdentity).GetProperty(nameof(TenantIdentity.TenantId))!;

        return Expression.Property(Expression.Property(null, current), tenantId);
    }
}
