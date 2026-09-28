using System.Text.RegularExpressions;

using HubNegocios.SharedKernel.Http;

namespace HubNegocios.IdentityService.Domain.ValueObjects;

/// <summary>
/// El correo con el que un usuario entra al hub.
///
/// Es un tipo propio y no un <c>string</c> por la normalización, no por la
/// validación: el correo es la mitad de la clave única <c>(tenant_id, email)</c>
/// y, si cada sitio lo escribe como quiera, <c>Ana@bar.com</c> y
/// <c>ana@bar.com</c> pasan a ser dos cuentas distintas con la misma dirección.
/// Recortar y bajar a minúsculas en un único lugar es lo que hace que el índice
/// único signifique algo.
///
/// La validación es deliberadamente laxa: comprobar un correo contra la RFC 5322
/// es un ejercicio inútil —acepta cosas que ningún servidor entrega y rechaza
/// direcciones válidas—. La única prueba real de que un correo existe es enviarle
/// algo, así que aquí solo se descartan las formas que no pueden ser una
/// dirección.
/// </summary>
public readonly partial record struct Email
{
    /// <summary>Longitud máxima de una dirección según la RFC 5321 (64 de local + @ + 255 de dominio).</summary>
    public const int MaxLength = 320;

    public string Value { get; }

    private Email(string value) => Value = value;

    public static Email Create(string? raw)
    {
        var value = raw?.Trim().ToLowerInvariant() ?? string.Empty;

        if (value.Length is 0 or > MaxLength)
        {
            throw new DomainException(
                "USER_EMAIL_INVALID",
                $"El correo es obligatorio y no puede pasar de {MaxLength} caracteres.");
        }

        if (!Pattern().IsMatch(value))
        {
            throw new DomainException("USER_EMAIL_INVALID", "El correo no tiene una forma válida.");
        }

        return new Email(value);
    }

    public override string ToString() => Value;

    public static implicit operator string(Email email) => email.Value;

    [GeneratedRegex(@"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
