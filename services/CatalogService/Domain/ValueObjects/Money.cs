using HubNegocios.SharedKernel.Http;

namespace HubNegocios.CatalogService.Domain.ValueObjects;

/// <summary>
/// Un importe con su moneda.
///
/// Van juntos por una razón muy concreta: un <c>decimal</c> suelto invita a
/// comparar o sumar cantidades de monedas distintas sin que nada proteste, y
/// ese error no se ve hasta que alguien cobra 50 000 pesos como si fueran 50 000
/// dólares. Aquí la moneda viaja pegada al número y las operaciones entre
/// monedas distintas son imposibles de escribir por accidente.
///
/// El importe es <c>decimal</c>, nunca <c>double</c>: en coma flotante binaria
/// 0,1 no es 0,1 y los céntimos acaban sin cuadrar.
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
            throw new DomainException("ITEM_PRICE_NEGATIVE", "El precio no puede ser negativo.");
        }

        var code = currency?.Trim().ToUpperInvariant() ?? string.Empty;

        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
        {
            throw new DomainException(
                "ITEM_CURRENCY_INVALID",
                "La moneda debe ser un código ISO-4217 de tres letras, por ejemplo COP.");
        }

        /*
          Se redondea aquí y no al guardar. Si se dejara para la columna
          numeric(18,2), Postgres redondearía en silencio y el precio que
          devuelve una lectura posterior no sería el que se validó ni el que se
          publicó en el evento.
        */
        return new Money(Math.Round(amount, Decimals, MidpointRounding.ToEven), code);
    }

    public override string ToString() => $"{Amount} {Currency}";
}
