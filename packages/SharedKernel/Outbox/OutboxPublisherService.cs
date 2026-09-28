using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HubNegocios.SharedKernel.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Cada cuánto se mira la tabla.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Cuántos eventos por vuelta. Acota el tiempo de cada ciclo.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Intentos antes de dar el evento por perdido.</summary>
    public int MaxRetries { get; set; } = 5;
}

/// <summary>
/// Lee la outbox y publica lo pendiente.
///
/// Corre en todas las instancias del servicio, así que dos instancias pueden
/// coger el mismo lote a la vez. Se resuelve con <c>FOR UPDATE SKIP LOCKED</c>:
/// cada instancia bloquea las filas que se lleva y las demás pasan de largo en
/// vez de esperar. Sin el <c>SKIP LOCKED</c> las instancias se turnarían —
/// correcto, pero sin ganar nada por escalar.
///
/// La entrega es «al menos una vez»: si el proceso muere entre publicar y
/// marcar la fila, el evento se reenvía. Los consumidores deben ser
/// idempotentes. La alternativa —marcar antes de publicar— convierte el
/// problema en «como mucho una vez», es decir, en perder eventos, que es
/// bastante peor.
/// </summary>
public sealed class OutboxPublisherService<TContext>(
    IServiceScopeFactory scopeFactory,
    IEventBusPublisher publisher,
    TimeProvider clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxPublisherService<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Publicador de outbox en marcha: cada {Interval}, lotes de {BatchSize}, {MaxRetries} reintentos",
            _options.PollInterval,
            _options.BatchSize,
            _options.MaxRetries);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un fallo de una vuelta no puede tumbar el servicio entero:
                // se registra y se vuelve a intentar en el siguiente ciclo.
                logger.LogError(ex, "Fallo el ciclo del publicador de outbox; se reintenta en {Interval}", _options.PollInterval);
            }

            try
            {
                await Task.Delay(_options.PollInterval, clock, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Publicador de outbox detenido");
    }

    private async Task PublishPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        /*
          FOR UPDATE SKIP LOCKED necesita SQL: EF Core no lo expone. Los valores
          van como parámetros, no interpolados.
        */
        var pending = await context.Set<OutboxEvent>()
            .FromSqlRaw(
                """
                SELECT * FROM outbox_events
                WHERE published = false
                  AND dead_lettered_at IS NULL
                  AND retry_count < {0}
                ORDER BY id
                LIMIT {1}
                FOR UPDATE SKIP LOCKED
                """,
                _options.MaxRetries,
                _options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pending.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var publicados = 0;

        foreach (var entry in pending)
        {
            var message = new OutboxMessage(
                entry.Id,
                entry.TenantId,
                entry.AggregateId,
                entry.AggregateType,
                entry.EventType,
                entry.EventPayloadJson,
                entry.CorrelationId,
                entry.CreatedAt);

            try
            {
                await publisher.PublishAsync(message, cancellationToken).ConfigureAwait(false);
                entry.MarkPublished(now);
                publicados++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                entry.MarkFailed(now, ex.ToString(), _options.MaxRetries);

                logger.LogWarning(
                    ex,
                    "No se pudo publicar el evento {EventId} ({EventType}); intento {RetryCount} de {MaxRetries}",
                    entry.Id,
                    entry.EventType,
                    entry.RetryCount,
                    _options.MaxRetries);

                if (entry.DeadLetteredAt is not null)
                {
                    logger.LogError(
                        "Evento {EventId} ({EventType}) del tenant {TenantId} agotó los reintentos y queda apartado para revisión manual",
                        entry.Id,
                        entry.EventType,
                        entry.TenantId);
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        if (publicados > 0)
        {
            logger.LogInformation("Publicados {Publicados} de {Total} eventos pendientes", publicados, pending.Count);
        }
    }
}
