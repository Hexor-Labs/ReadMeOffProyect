using System.Security.Cryptography;
using System.Text.RegularExpressions;

using HubNegocios.SharedKernel.Http;

namespace HubNegocios.ReservationsService.Domain.ValueObjects;

/// <summary>
/// El código que el cliente lleva encima: <c>BOOK-7K4M</c>.
///
/// Se genera con <see cref="RandomNumberGenerator"/> y no con <c>Random</c>, y
/// esto no es puntillismo. <c>Random</c> es un generador predecible: con unos
/// pocos códigos observados se puede reconstruir su estado y calcular los
/// siguientes. Como el código es lo que se presenta en la puerta, adivinarlo
/// equivale a poder consultar —o usar— la reserva de otra persona.
///
/// El alfabeto deja fuera <c>0/O</c> y <c>1/I</c>: el código se dicta por
/// teléfono y se lee en una pantalla, y esos cuatro caracteres son la mitad de
/// las equivocaciones.
///
/// Cuatro caracteres sobre 32 símbolos son algo más de un millón de
/// combinaciones. Es suficiente contra el error humano y contra un cliente que
/// pruebe códigos a mano, pero no contra quien los recorra en masa: si el día
/// que exista un endpoint público de consulta por código, tiene que ir con
/// límite de intentos y no fiarse solo de esta longitud.
/// </summary>
public readonly partial record struct ConfirmationCode
{
    public const string Prefix = "BOOK-";
    public const int RandomLength = 4;

    /// <summary>Longitud total en la columna: <c>BOOK-</c> más la parte aleatoria.</summary>
    public const int MaxLength = 5 + RandomLength;

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public string Value { get; }

    private ConfirmationCode(string value) => Value = value;

    /// <summary>Un código nuevo, imposible de adivinar a partir de los anteriores.</summary>
    public static ConfirmationCode Generate() =>
        new(string.Concat(Prefix, new string(RandomNumberGenerator.GetItems<char>(Alphabet, RandomLength))));

    /// <summary>Reconstruye un código que llega de fuera, validándolo.</summary>
    public static ConfirmationCode Create(string? raw)
    {
        var value = raw?.Trim().ToUpperInvariant() ?? string.Empty;

        if (!Pattern().IsMatch(value))
        {
            throw new DomainException(
                "RESERVATION_CODE_INVALID",
                $"El código de confirmación tiene que seguir el formato {Prefix}XXXX.");
        }

        return new ConfirmationCode(value);
    }

    public override string ToString() => Value;

    public static implicit operator string(ConfirmationCode code) => code.Value;

    /*
      Solo el alfabeto real: así un código con una O o un 1 —que esta clase no
      genera nunca— se rechaza al entrar en vez de buscarse en la tabla.

      El patrón se compone concatenando constantes para no repetir el alfabeto
      en dos sitios. El atributo exige una constante de compilación y un int
      interpolado no lo es, así que la longitud va literal: si cambia
      RandomLength, hay que cambiarla también aquí.
    */
    private const string PatternText = "^" + Prefix + "[" + Alphabet + "]{4}$";

    [GeneratedRegex(PatternText, RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
