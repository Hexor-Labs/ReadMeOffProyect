using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.TenantService.Domain.Entities;
using HubNegocios.TenantService.Domain.Ports;
using HubNegocios.TenantService.Domain.ValueObjects;

using Microsoft.Extensions.Logging;

namespace HubNegocios.TenantService.Application.UseCases;

public sealed record CreateTenantCommand(
    string Name,
    string VerticalType,
    string Slug,
    string Email,
    string? Phone,
    string? Description);

public sealed record CreateTenantResult(Guid TenantId, string Slug, string Plan);

/// <summary>
/// Da de alta un negocio en el hub.
///
/// Es el primer eslabón de todo: hasta que este evento no sale, para el resto
/// del sistema el tenant no existe.
/// </summary>
public sealed class CreateTenantHandler(
    ITenantRepository repository,
    IOutboxWriter outbox,
    TimeProvider clock,
    ILogger<CreateTenantHandler> logger)
{
    public async Task<CreateTenantResult> HandleAsync(
        CreateTenantCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var slug = Slug.Create(command.Slug);

        /*
          Comprobar antes de insertar da un error legible, pero no es la
          garantía: entre esta consulta y el INSERT cabe otra petición con el
          mismo slug. Quien garantiza la unicidad es el índice único de la
          tabla; esto solo evita que el caso normal acabe en un error 500 con
          un mensaje de Postgres.
        */
        if (await repository.SlugExistsAsync(slug.Value, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException(
                "TENANT_SLUG_TAKEN",
                $"El slug «{slug.Value}» ya está en uso.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var tenantId = Guid.NewGuid();

        var tenant = Tenant.Register(
            tenantId,
            command.Name,
            command.VerticalType,
            slug,
            command.Email,
            command.Phone,
            command.Description);

        repository.Add(tenant);
        repository.AddSubscription(Subscription.StartTrial(Guid.NewGuid(), tenantId, now));

        /*
          El evento se encola aquí, no se publica. Se convierte en una fila más
          de la misma transacción: o se guardan el tenant, su suscripción y el
          evento, o no se guarda nada. Publicarlo en este punto abriría la
          puerta a avisar de un tenant que después no llegó a existir.

          Se usa EnqueueFor y no Enqueue porque todavía no hay tenant en el
          contexto de ejecución: el tenant es justo lo que se acaba de crear.
        */
        outbox.EnqueueFor(
            tenantId,
            tenantId,
            nameof(Tenant),
            "tenant.created",
            new
            {
                tenantId,
                name = tenant.Name,
                slug = tenant.Slug,
                verticalType = tenant.VerticalType,
                plan = tenant.Plan,
                createdAt = now,
            });

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Tenant {TenantId} creado con slug {Slug}", tenantId, tenant.Slug);

        return new CreateTenantResult(tenantId, tenant.Slug, tenant.Plan);
    }
}
