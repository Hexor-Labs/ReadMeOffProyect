using HubNegocios.BillingService.Domain.Entities;

namespace HubNegocios.BillingService.Domain.Ports;

/// <summary>Lo mínimo que el barrido de plataforma necesita de una factura vencida.</summary>
public sealed record OverdueInvoiceCandidate(Guid InvoiceId, Guid TenantId, DateTime DueDate);

public interface IBillingRepository
{
    Task<Invoice?> GetInvoiceAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Invoice>> GetInvoicesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ¿Ya hay factura de este ciclo para esta suscripción?
    ///
    /// Es la primera barrera contra el doble cobro cuando el evento
    /// <c>subscription.renewal_due</c> se reentrega. La segunda es el índice
    /// único (tenant, suscripción, vencimiento), que es el que aguanta si dos
    /// instancias miran a la vez.
    /// </summary>
    Task<bool> ExistsInvoiceForCycleAsync(
        Guid subscriptionId,
        DateTime dueDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca una transacción ya registrada por su identificador en el proveedor.
    /// Es la comprobación de idempotencia del webhook.
    /// </summary>
    Task<PaymentTransaction?> FindTransactionAsync(
        PaymentProvider provider,
        string providerTransactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Facturas emitidas que pasaron de <paramref name="limit"/> sin cobrarse,
    /// de TODOS los tenants.
    ///
    /// Es la única consulta del servicio que cruza tenants, porque el trabajo
    /// que reclama impagos es de plataforma y no tiene un tenant en contexto.
    /// Devuelve solo identificadores a propósito: quien vaya a modificar esas
    /// facturas las recarga después dentro del ámbito del tenant que le
    /// corresponde, que es lo que mantiene en pie las dos protecciones —el
    /// filtro global de EF Core y RLS de Postgres— en la parte que escribe.
    /// </summary>
    Task<IReadOnlyList<OverdueInvoiceCandidate>> FindOverdueCandidatesAsync(
        DateTime limit,
        int maxRows,
        CancellationToken cancellationToken = default);

    void AddInvoice(Invoice invoice);

    void AddTransaction(PaymentTransaction transaction);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
