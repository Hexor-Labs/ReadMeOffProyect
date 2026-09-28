using HubNegocios.SharedKernel.Auditing;

namespace HubNegocios.TenantService.Domain.Entities;

public enum PaymentStatus
{
    Pending,
    Paid,
    Failed,
    Cancelled,
}

/// <summary>
/// El plan contratado por un tenant y su ciclo de facturación.
///
/// Guarda el consumo de API (<see cref="ApiCallsUsed"/>) contra el límite del
/// plan. Eso lo usa el gateway para cortar a quien se pase, y billing-service
/// para cobrar los excesos.
///
/// Igual que <see cref="Tenant"/>, no implementa <c>ITenantOwned</c>: en este
/// servicio el tenant es el sujeto, no el filtro.
/// </summary>
public sealed class Subscription : IAuditable
{
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string PlanId { get; private set; } = Tenant.DefaultPlan;

    /// <summary>
    /// Coste mensual. <c>decimal</c>, nunca <c>double</c>: en coma flotante
    /// binaria, 0,1 no es 0,1, y los céntimos acaban sin cuadrar.
    /// </summary>
    public decimal MonthlyCost { get; private set; }

    public string Currency { get; private set; } = "COP";

    public DateTime BillingCycleStart { get; private set; }
    public DateTime BillingCycleEnd { get; private set; }
    public DateTime RenewalDate { get; private set; }

    public long ApiCallsUsed { get; private set; }
    public long ApiCallsLimit { get; private set; }

    public string? PaymentMethod { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.Pending;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    private Subscription()
    {
    }

    public static Subscription StartTrial(Guid id, Guid tenantId, DateTime from) => new()
    {
        Id = id,
        TenantId = tenantId,
        PlanId = Tenant.DefaultPlan,
        MonthlyCost = 0m,
        BillingCycleStart = from,
        BillingCycleEnd = from.AddMonths(1),
        RenewalDate = from.AddMonths(1),
        ApiCallsUsed = 0,
        ApiCallsLimit = 10_000,
        PaymentStatus = PaymentStatus.Pending,
    };

    public void RegisterApiCalls(long calls)
    {
        if (calls < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(calls), "El consumo no puede ser negativo.");
        }

        ApiCallsUsed += calls;
    }

    public bool HasExceededQuota => ApiCallsLimit > 0 && ApiCallsUsed > ApiCallsLimit;

    public void MarkPaid(string method)
    {
        PaymentMethod = method;
        PaymentStatus = PaymentStatus.Paid;
    }
}
