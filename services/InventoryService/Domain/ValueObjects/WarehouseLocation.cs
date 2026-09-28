using System.Text.RegularExpressions;

using HubNegocios.SharedKernel.Http;

namespace HubNegocios.InventoryService.Domain.ValueObjects;

/// <summary>
/// Dónde está físicamente la mercancía: <c>BOD-A/EST-3/NIV-2</c>.
///
/// Es un tipo propio y no un <c>string</c> porque esta cadena la teclea una
/// persona con un lector en la mano, y su único uso es que otra persona
/// encuentre la caja. Normalizar a mayúsculas y recortar espacios en un solo
/// sitio evita el problema práctico de tener <c>bod-a</c> y <c>BOD-A </c> como
/// dos ubicaciones distintas que para el almacenero son la misma.
///
/// Es opcional a propósito: un negocio que vende servicios tiene stock —cupos,
/// bonos— y no tiene estantería donde ponerlo. Obligar a inventar una ubicación
/// acabaría en una columna llena de <c>N/A</c>.
/// </summary>
public readonly partial record struct WarehouseLocation
{
    public const int MaxLength = 60;

    public string Value { get; }

    private WarehouseLocation(string value) => Value = value;

    /// <summary>
    /// Normaliza y valida lo que llega de fuera. Devuelve <c>null</c> cuando no
    /// viene ubicación, que es un caso legítimo y no un error.
    /// </summary>
    public static WarehouseLocation? Create(string? raw)
    {
        var value = raw?.Trim().ToUpperInvariant() ?? string.Empty;

        if (value.Length == 0)
        {
            return null;
        }

        if (value.Length > MaxLength)
        {
            throw new DomainException(
                "STOCK_LOCATION_INVALID",
                $"La ubicación no puede pasar de {MaxLength} caracteres.");
        }

        if (!Pattern().IsMatch(value))
        {
            throw new DomainException(
                "STOCK_LOCATION_INVALID",
                "La ubicación solo admite letras, dígitos, guiones, puntos y barras, y no puede empezar ni terminar en separador.");
        }

        return new WarehouseLocation(value);
    }

    public override string ToString() => Value;

    public static implicit operator string(WarehouseLocation location) => location.Value;

    [GeneratedRegex("^[A-Z0-9]([A-Z0-9./_-]*[A-Z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
