using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Tenancy;
using HubNegocios.TenantService.Domain.Entities;
using HubNegocios.TenantService.Domain.Ports;

using Microsoft.Extensions.Logging;

namespace HubNegocios.TenantService.Application.UseCases;

public sealed record SuspendTenantCommand(Guid TenantId, string? Reason);

/// <summary>
/// Suspende un tenant y avisa al resto del sistema.
///
/// El evento <c>tenant.suspended</c> no es informativo: los demás servicios lo
/// escuchan para dejar de atender a ese tenant. Por eso importa que salga en la
/// misma transacción que el cambio de estado — si se suspendiera aquí y el
/// aviso se perdiera, el tenant seguiría operando en los otros nueve servicios
/// como si nada.
/// </summary>
public sealed class SuspendTenantHandler(
    ITenantRepository repository,
    IOutboxWriter outbox,
    TimeProvider clock,
    ILogger<SuspendTenantHandler> logger)
{
    public async Task HandleAsync(SuspendTenantCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenant = await repository.GetByIdAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("TENANT_NOT_FOUND", "El tenant no existe.");

        if (tenant.Status == TenantStatus.Suspended)
        {
            // Idempotente a propósito: suspender lo ya suspendido no es un
            // error, y tratarlo como tal obliga a quien llama a consultar el
            // estado antes de actuar, que es una carrera en sí misma.
            logger.LogInformation("El tenant {TenantId} ya estaba suspendido; no se hace nada", command.TenantId);
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var previous = tenant.Suspend();

        repository.AddStatusHistory(TenantStatusHistory.Record(
            Guid.NewGuid(),
            tenant.Id,
            previous,
            tenant.Status,
            TenantContext.Current.UserId,
            now,
            command.Reason));

        outbox.EnqueueFor(
            tenant.Id,
            tenant.Id,
            nameof(Tenant),
            "tenant.suspended",
            new
            {
                tenantId = tenant.Id,
                previousStatus = previous.ToString(),
                reason = command.Reason,
                suspendedAt = now,
            });

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogWarning(
            "Tenant {TenantId} suspendido (antes {PreviousStatus}). Motivo: {Reason}",
            tenant.Id,
            previous,
            command.Reason ?? "sin especificar");
    }
}
