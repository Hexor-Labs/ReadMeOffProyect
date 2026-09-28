namespace HubNegocios.BillingService.Domain.Ports;

/// <summary>
/// Aviso de tenant-service de que una suscripción toca renovar. Es el cuerpo del
/// evento <c>subscription.renewal_due</c>.
/// </summary>
/// <param name="TenantId">
/// Viene en el evento porque el trabajo que lo consume corre sin tenant en
/// contexto: es el dato con el que abre su <c>TenantContext.BeginScope</c>.
/// </param>
public sealed record SubscriptionRenewalDue(
    Guid TenantId,
    Guid SubscriptionId,
    decimal Amount,
    string Currency,
    DateTime DueDate,
    Guid CorrelationId);

/// <summary>
/// Entrada de los avisos de renovación.
///
/// Está detrás de un puerto para que el día que se conecte RabbitMQ de verdad se
/// escriba una implementación nueva y no se toque el caso de uso. La entrega es
/// «al menos una vez», como en toda la plataforma: el mismo aviso puede llegar
/// dos veces y quien lo consume tiene que ser idempotente.
/// </summary>
public interface ISubscriptionRenewalSource
{
    /// <summary>Avisos pendientes, como mucho <paramref name="maxItems"/>.</summary>
    Task<IReadOnlyList<SubscriptionRenewalDue>> ReadPendingAsync(
        int maxItems,
        CancellationToken cancellationToken = default);
}
