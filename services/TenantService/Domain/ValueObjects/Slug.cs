using System.Text.RegularExpressions;

using HubNegocios.SharedKernel.Http;

namespace HubNegocios.TenantService.Domain.ValueObjects;

/// <summary>
/// El identificador del tenant en la URL: <c>mi-restaurante</c> en
/// <c>mi-restaurante.hub.com</c>.
///
/// Es un tipo propio y no un <c>string</c> porque acaba siendo parte de un
/// nombre de dominio, y las reglas de un subdominio no son opinión: minúsculas,
/// letras, dígitos y guiones, sin empezar ni terminar en guion, y como mucho 63
/// caracteres, que es el límite de una etiqueta DNS. Validarlo en un solo sitio
/// evita descubrir en producción que alguien registró un slug que no se puede
/// convertir en dominio.
/// </summary>
public readonly partial record struct Slug
{
    public const int MaxLength = 63;
    private const int MinLength = 3;

    public string Value { get; }

    private Slug(string value) => Value = value;

    public static Slug Create(string? raw)
    {
        var value = raw?.Trim().ToLowerInvariant() ?? string.Empty;

        if (value.Length is < MinLength or > MaxLength)
        {
            throw new DomainException(
                "TENANT_SLUG_INVALID",
                $"El slug debe tener entre {MinLength} y {MaxLength} caracteres.");
        }

        if (!Pattern().IsMatch(value))
        {
            throw new DomainException(
                "TENANT_SLUG_INVALID",
                "El slug solo admite minúsculas, dígitos y guiones, y no puede empezar ni terminar en guion.");
        }

        return new Slug(value);
    }

    public override string ToString() => Value;

    public static implicit operator string(Slug slug) => slug.Value;

    [GeneratedRegex("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
