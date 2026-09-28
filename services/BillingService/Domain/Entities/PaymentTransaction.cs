using HubNegocios.BillingService.Domain.ValueObjects;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.BillingService.Domain.Entities;

/// <summary>Pasarelas con las que trabaja el hub. Cada una tiene su propio secreto de firma.</summary>
public enum PaymentProvider
{
    Stripe,
    Wompi,
    MercadoPago,
}

/// <summary>
/// Resultado que el proveedor comunica de un intento de cobro.
///
/// Solo dos valores, y a propósito: son los dos únicos que cambian algo por
/// aquí. Los estados intermedios de cada pasarela (<c>processing</c>,
/// <c>pending</c>, <c>requires_action</c>) se ignoran sin guardar nada, porque
/// registrarlos gastaría la clave de idempotencia —el identificador de la
/// transacción en el proveedor— antes de saber si el cobro salió bien.
/// </summary>
public enum PaymentStatus
{
    Succeeded,
    Failed,
}

/// <summary>
/// El intento de cobro tal y como lo contó el proveedor.
///
/// <para>
/// ESTE SERVICIO NO ALMACENA NÚMEROS DE TARJETA. Lo único que entra de un medio
/// de pago es <see cref="ProviderTransactionId"/>, que es un token opaco
/// (<c>ch_...</c>, <c>pi_...</c>) generado y custodiado por el proveedor. No hay
/// columna para el PAN, ni para el titular, ni para la caducidad, ni para el
/// CVV, y no debe añadirse ninguna: en cuanto un PAN toca nuestra base de datos,
/// el proyecto entra en el alcance completo de PCI-DSS —red segmentada, cifrado
/// en reposo con rotación de claves, registro de accesos, auditoría anual de un
/// QSA— que este equipo no puede sostener. Delegar la tokenización en el
/// proveedor no es una comodidad: es lo que mantiene el cumplimiento en el nivel
/// de autoevaluación SAQ-A.
/// </para>
///
/// <para>
/// No guarda moneda: una transacción liquida siempre una factura concreta y la
/// moneda es la de esa factura. Duplicarla aquí solo abriría la puerta a que las
/// dos no coincidan.
/// </para>
/// </summary>
public sealed class PaymentTransaction : ITenantOwned
{
    /// <summary>Tope del token del proveedor. Suficiente para los tres y lejos de lo que ocupa un PAN.</summary>
    public const int MaxProviderTransactionIdLength = 128;

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid InvoiceId { get; private set; }

    public PaymentProvider Provider { get; private set; }

    /// <summary>
    /// Identificador de la transacción en el proveedor. Es además la CLAVE DE
    /// IDEMPOTENCIA del webhook: lleva un índice único, así que un reenvío del
    /// mismo cobro no puede registrarse dos veces ni por carrera entre dos
    /// instancias.
    /// </summary>
    public string ProviderTransactionId { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTime ProcessedAt { get; private set; }

    private PaymentTransaction()
    {
    }

    public static PaymentTransaction Record(
        Guid id,
        Guid invoiceId,
        PaymentProvider provider,
        string providerTransactionId,
        Money amount,
        PaymentStatus status,
        DateTime processedAt)
    {
        var token = providerTransactionId?.Trim();

        if (string.IsNullOrEmpty(token) || token.Length > MaxProviderTransactionIdLength)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["providerTransactionId"] =
                    [$"El identificador del proveedor es obligatorio y no puede pasar de {MaxProviderTransactionIdLength} caracteres."],
            });
        }

        return new PaymentTransaction
        {
            Id = id,
            InvoiceId = invoiceId,
            Provider = provider,
            ProviderTransactionId = token,
            Amount = amount.Amount,
            Status = status,
            ProcessedAt = processedAt,
        };
    }
}
