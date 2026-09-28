using HubNegocios.BillingService.Domain.ValueObjects;
using HubNegocios.SharedKernel.Auditing;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.BillingService.Domain.Entities;

public enum InvoiceStatus
{
    /// <summary>
    /// Calculada pero todavía sin avisar al negocio. Existe para que ninguna
    /// factura pueda cobrarse antes de haberse emitido: sin este estado, un
    /// importe a medio calcular sería indistinguible de una deuda exigible.
    /// </summary>
    Draft,

    /// <summary>Emitida y pendiente de pago. Es el único estado que puede vencer.</summary>
    Sent,

    Paid,

    /// <summary>Vencida más allá del periodo de gracia. Lo marca el trabajo de fondo.</summary>
    Overdue,

    Cancelled,
}

/// <summary>
/// Lo que un negocio debe por su suscripción en un ciclo.
///
/// Es la pieza que mueve el dinero del hub, así que el estado tiene que ser
/// difícil de romper: no hay <c>set</c> públicos y cada transición comprueba
/// desde dónde viene. Un descuadre aquí no se ve hasta el cierre de mes.
///
/// <para>
/// Sobre <see cref="PaymentProviderId"/>: es un TOKEN del proveedor, nunca un
/// dato de tarjeta. En esta tabla no hay —ni puede haber— número de tarjeta,
/// titular, fecha de caducidad ni CVV. Guardar un PAN metería al proyecto en el
/// alcance completo de PCI-DSS (segmentación de red, cifrado en reposo con
/// rotación de claves, auditoría anual por un QSA), que es inviable para este
/// equipo y además innecesario: la tokenización vive en el proveedor y aquí solo
/// entra su referencia opaca.
/// </para>
/// </summary>
public sealed class Invoice : ITenantOwned, IAuditable
{
    /// <summary>
    /// Días que se dejan pasar tras el vencimiento antes de reclamar la
    /// suspensión del tenant.
    ///
    /// No es un número arbitrario: entre una transferencia que tarda y un cobro
    /// que el banco reintenta al día siguiente, cortar el acceso el mismo día
    /// del vencimiento deja sin servicio a negocios que van a pagar. Quince días
    /// es el plazo que el hub considera impago de verdad.
    /// </summary>
    public const int GraceDays = 15;

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>
    /// Suscripción de tenant-service que se factura. Sin clave ajena: es otra
    /// base de datos, y un JOIN entre bases de dos servicios es el atajo que
    /// convierte los microservicios en un monolito repartido.
    /// </summary>
    public Guid SubscriptionId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;

    public DateTime DueDate { get; private set; }

    public DateTime? PaidAt { get; private set; }

    /// <summary>
    /// Referencia opaca del proveedor al medio de pago con el que se cobró
    /// (<c>cus_...</c>, <c>pm_...</c>). Es un token, no una tarjeta: ver la nota
    /// de PCI-DSS en la documentación de la clase.
    /// </summary>
    public string? PaymentProviderId { get; private set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    private Invoice()
    {
    }

    /// <summary>Crea la factura del ciclo en borrador. El importe ya viene validado por <see cref="Money"/>.</summary>
    public static Invoice Issue(Guid id, Guid subscriptionId, Money amount, DateTime dueDate)
    {
        if (subscriptionId == Guid.Empty)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["subscriptionId"] = ["Una factura sin suscripción no se puede cobrar a nadie."],
            });
        }

        return new Invoice
        {
            Id = id,
            SubscriptionId = subscriptionId,
            Amount = amount.Amount,
            Currency = amount.Currency,
            DueDate = dueDate,
            Status = InvoiceStatus.Draft,
        };
    }

    /// <summary>Emite la factura: a partir de aquí es una deuda exigible.</summary>
    public InvoiceStatus Send() => TransitionTo(InvoiceStatus.Sent, from: [InvoiceStatus.Draft]);

    /// <summary>
    /// Da la factura por pagada con el resultado que confirmó el proveedor.
    ///
    /// Se admite también desde <see cref="InvoiceStatus.Overdue"/>: un negocio
    /// que paga tarde paga igual, y rechazar ese cobro dejaría el dinero
    /// ingresado sin factura que lo respalde.
    /// </summary>
    public InvoiceStatus MarkPaid(DateTime paidAt, string? paymentProviderId)
    {
        var previous = TransitionTo(InvoiceStatus.Paid, from: [InvoiceStatus.Sent, InvoiceStatus.Overdue]);

        if (previous == InvoiceStatus.Paid)
        {
            // Reintento del proveedor sobre algo ya cobrado: no se toca la fecha
            // de pago ni el token. Reescribirlos convertiría un reenvío inocuo
            // en una modificación del histórico contable.
            return previous;
        }

        PaidAt = paidAt;
        PaymentProviderId = paymentProviderId;

        return previous;
    }

    /// <summary>
    /// Marca la factura como vencida. Solo cuando de verdad pasó el periodo de
    /// gracia: es la comprobación que impide que un error de reloj o un trabajo
    /// mal programado reclamen una factura que todavía está en plazo.
    /// </summary>
    public InvoiceStatus MarkOverdue(DateTime now)
    {
        if (Status == InvoiceStatus.Overdue)
        {
            return Status;
        }

        if (!IsBeyondGracePeriod(now))
        {
            throw new ConflictException(
                "INVOICE_NOT_OVERDUE_YET",
                $"La factura vence el {DueDate:yyyy-MM-dd} y todavía está dentro de los {GraceDays} días de gracia.");
        }

        return TransitionTo(InvoiceStatus.Overdue, from: [InvoiceStatus.Sent]);
    }

    /// <summary>
    /// Anula la factura. No desde <see cref="InvoiceStatus.Paid"/>: una vez
    /// cobrada, anular deja de ser un cambio de estado y pasa a ser una
    /// devolución, con su movimiento de dinero y su rastro contable. Son dos
    /// operaciones distintas y mezclarlas descuadra la contabilidad.
    /// </summary>
    public InvoiceStatus Cancel() => TransitionTo(
        InvoiceStatus.Cancelled,
        from: [InvoiceStatus.Draft, InvoiceStatus.Sent, InvoiceStatus.Overdue]);

    /// <summary>¿Lleva más de <see cref="GraceDays"/> días vencida?</summary>
    public bool IsBeyondGracePeriod(DateTime now) => now > DueDate.AddDays(GraceDays);

    private InvoiceStatus TransitionTo(InvoiceStatus target, InvoiceStatus[] from)
    {
        if (Status == target)
        {
            // Repetir la misma transición no es un error: los eventos y los
            // webhooks se entregan «al menos una vez» y el segundo intento debe
            // ser inocuo.
            return Status;
        }

        if (!from.Contains(Status))
        {
            throw new ConflictException(
                "INVOICE_INVALID_TRANSITION",
                $"Una factura en estado {Status} no puede pasar a {target}.");
        }

        var previous = Status;
        Status = target;
        return previous;
    }
}
