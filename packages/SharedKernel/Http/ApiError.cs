using System.Text.Json.Serialization;

namespace HubNegocios.SharedKernel.Http;

/// <summary>
/// El único formato de error que sale de cualquier servicio del hub.
///
/// Uno solo, y no el que a cada servicio le parezca, porque al otro lado hay
/// un frontend que no debería aprender diez formas distintas de saber que algo
/// falló. Lo dice el documento técnico en el apartado del BFF y es de las
/// cosas más baratas de hacer bien desde el principio y más caras de
/// uniformar después.
/// </summary>
public sealed record ApiError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("details")] IReadOnlyDictionary<string, string[]>? Details,
    [property: JsonPropertyName("trace_id")] string TraceId);

/// <summary>Envoltorio: el cuerpo siempre es <c>{ "error": { ... } }</c>.</summary>
public sealed record ApiErrorResponse([property: JsonPropertyName("error")] ApiError Error);

/// <summary>
/// Fallo de negocio con un código estable que el frontend puede interpretar.
///
/// El código es contrato —<c>RESERVATION_SLOT_TAKEN</c> no cambia aunque se
/// reescriba el mensaje— y el mensaje es para las personas. Mezclarlos obliga
/// al frontend a comparar cadenas de texto en español, que es exactamente lo
/// que se rompe el día que alguien corrige una tilde.
/// </summary>
public class DomainException(string code, string message, IReadOnlyDictionary<string, string[]>? details = null)
    : Exception(message)
{
    public string Code { get; } = code;

    public IReadOnlyDictionary<string, string[]>? Details { get; } = details;
}

/// <summary>No existe, o existe en otro tenant (que para quien pregunta es lo mismo).</summary>
public sealed class NotFoundException(string code, string message) : DomainException(code, message);

/// <summary>Choca con el estado actual: doble reserva, stock agotado, slug repetido.</summary>
public sealed class ConflictException(string code, string message) : DomainException(code, message);

/// <summary>La petición no cumple las reglas de entrada.</summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : DomainException("VALIDATION_FAILED", "La petición tiene campos inválidos", errors);
