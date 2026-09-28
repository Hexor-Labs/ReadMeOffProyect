using System.Text.RegularExpressions;

using HubNegocios.SharedKernel.Http;

namespace HubNegocios.CatalogService.Domain.ValueObjects;

/// <summary>
/// El código con el que el negocio identifica su producto: <c>CAFE-LATTE-01</c>.
///
/// Es un tipo propio y no un <c>string</c> porque el SKU acaba en sitios donde
/// un espacio o una tilde duelen: etiquetas impresas, ficheros de importación,
/// integraciones con cajas registradoras. Normalizar a mayúsculas en un único
/// lugar evita además el problema práctico de tener <c>cafe-01</c> y
/// <c>CAFE-01</c> como dos productos distintos que el dueño cree que son uno.
/// </summary>
public readonly partial record struct Sku
{
    public const int MaxLength = 64;
    private const int MinLength = 2;

    public string Value { get; }

    private Sku(string value) => Value = value;

    public static Sku Create(string? raw)
    {
        var value = raw?.Trim().ToUpperInvariant() ?? string.Empty;

        if (value.Length is < MinLength or > MaxLength)
        {
            throw new DomainException(
                "ITEM_SKU_INVALID",
                $"El SKU debe tener entre {MinLength} y {MaxLength} caracteres.");
        }

        if (!Pattern().IsMatch(value))
        {
            throw new DomainException(
                "ITEM_SKU_INVALID",
                "El SKU solo admite letras, dígitos, guiones y puntos, y no puede empezar ni terminar en separador.");
        }

        return new Sku(value);
    }

    public override string ToString() => Value;

    public static implicit operator string(Sku sku) => sku.Value;

    [GeneratedRegex("^[A-Z0-9]([A-Z0-9._-]*[A-Z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
