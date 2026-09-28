using HubNegocios.SharedKernel.Http;

namespace HubNegocios.BillingService.Domain.ValueObjects;

/// <summary>
/// Un importe con su moneda.
///
/// Van juntos porque un <c>decimal</c> suelto invita a sumar o comparar
/// cantidades de monedas distintas sin que nada proteste, y en un servicio de
/// cobros ese error se paga: cobrar 50 000 pesos como si fueran 50 000 dólares
/// no se descubre hasta que el cliente reclama.
///
/// El importe es <c>decimal</c>, nunca <c>double</c>: en coma flotante binaria
/// 0,1 no es 0,1 y los céntimos acaban sin cuadrar. La columna es
/// <c>numeric(18,2)</c>, que es su equivalente exacto en Postgres.
/// </summary>
public readonly record struct Money
{
    /// <summary>Dos decimales, que es lo que admite la columna <c>numeric(18,2)</c>.</summary>
    private const int Decimals = 2;

    public decimal Amount { get; }

    /// <summary>Código ISO-4217 en mayúsculas: COP, USD, EUR.</summary>
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string? currency)
    {
        if (amount < 0)
        {
            // También hay una restricción CHECK en la tabla. Las dos hacen
            // falta: esta explica el error en el idioma del negocio, la de la
            // base tapa cualquier camino que no pase por aquí.
            throw new DomainException("BILLING_AMOUNT_NEGATIVE", "El importe de una factura no puede ser negativo.");
        }

        var code = currency?.Trim().ToUpperInvariant() ?? string.Empty;

        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
        {
            throw new DomainException(
                "BILLING_CURRENCY_INVALID",
                "La moneda debe ser un código ISO-4217 de tres letras, por ejemplo COP.");
        }

        /*
          Se redondea aquí y no al guardar. Si se dejara para la columna
          numeric(18,2), Postgres redondearía en silencio y el importe que se
          cobró no sería el que se validó ni el que salió en el evento.
        */
        return new Money(Math.Round(amount, Decimals, MidpointRounding.ToEven), code);
    }

    public override string ToString() => $"{Amount} {Currency}";
}
