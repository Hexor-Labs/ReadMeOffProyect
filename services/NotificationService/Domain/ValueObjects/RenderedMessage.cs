using System.Text.RegularExpressions;

namespace HubNegocios.NotificationService.Domain.ValueObjects;

/// <summary>
/// El mensaje ya listo para salir, con los marcadores sustituidos.
///
/// QUÉ SE HACE CON UN MARCADOR SIN VALOR es la decisión de diseño de esta clase.
/// Hay tres salidas y dos son malas:
///
/// - Dejar <c>{{customer_name}}</c> literal: el cliente recibe las tuberías del
///   sistema en su correo. Es la peor de las tres.
/// - Reventar el envío: un marcador mal escrito en una plantilla —que configura
///   el dueño del negocio, no un programador— dejaría a ese negocio sin
///   notificaciones, y nadie se enteraría hasta que un cliente reclamara.
/// - Sustituir por vacío y dejar constancia de qué marcador faltó. Es la que se
///   toma aquí: el mensaje sale, quizá con una frase algo escueta, y los NOMBRES
///   de los marcadores que faltaron van al log para que alguien arregle la
///   plantilla. Los nombres, nunca los valores.
/// </summary>
public sealed partial class RenderedMessage
{
    private RenderedMessage(string subject, string body, IReadOnlyList<string> missingPlaceholders)
    {
        Subject = subject;
        Body = body;
        MissingPlaceholders = missingPlaceholders;
    }

    public string Subject { get; }

    public string Body { get; }

    /// <summary>Marcadores de la plantilla para los que el evento no traía valor.</summary>
    public IReadOnlyList<string> MissingPlaceholders { get; }

    public static RenderedMessage From(
        string? subject,
        string? bodyTemplate,
        IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var missing = new List<string>();

        return new RenderedMessage(
            Replace(subject ?? string.Empty, values, missing),
            Replace(bodyTemplate ?? string.Empty, values, missing),
            missing.Distinct(StringComparer.Ordinal).ToList());
    }

    private static string Replace(
        string template,
        IReadOnlyDictionary<string, string> values,
        List<string> missing) =>
        Placeholder().Replace(template, match =>
        {
            var name = match.Groups["nombre"].Value;

            // Un valor en blanco cuenta como ausente: para quien lo recibe, «Hola
            // {{customer_name}}» y «Hola    » son el mismo problema.
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            missing.Add(name);
            return string.Empty;
        });

    /*
      Se admiten espacios dentro de las llaves —{{ customer_name }}— porque la
      plantilla la escribe una persona a mano y esa variante es la equivocación
      más común de todas; rechazarla no protege de nada y deja el marcador
      literal en el mensaje, que es justo lo que esta clase existe para evitar.
    */
    [GeneratedRegex(@"\{\{\s*(?<nombre>[A-Za-z0-9_]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
