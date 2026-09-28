namespace HubNegocios.NotificationService.Domain.ValueObjects;

/// <summary>
/// El destino de un envío, enmascarado para poder escribirlo en el log.
///
/// Existe porque el log tiene que servir para atender una reclamación —«no me
/// llegó nada»— sin convertirse en la libreta de direcciones de todos los
/// clientes del hub. Con <c>a***@gmail.com</c> se puede confirmar que se mandó
/// al correo que el cliente dice tener; con el correo entero, cualquiera con
/// acceso a los logs se lleva la lista de contactos.
///
/// Nunca acompaña al cuerpo del mensaje en el log: el destino enmascarado y el
/// resultado del envío es todo lo que se registra.
/// </summary>
public readonly record struct MaskedRecipient
{
    private const string SinDestino = "(sin destino)";

    private MaskedRecipient(string value) => Value = value;

    public string Value { get; }

    public static MaskedRecipient For(string? destination)
    {
        var destino = destination?.Trim();

        if (string.IsNullOrEmpty(destino))
        {
            return new MaskedRecipient(SinDestino);
        }

        var arroba = destino.IndexOf('@', StringComparison.Ordinal);

        if (arroba > 0)
        {
            // Se deja la primera letra y el dominio: basta para reconocer el
            // buzón sin poder escribirle.
            return new MaskedRecipient($"{destino[0]}***{destino[arroba..]}");
        }

        // Teléfonos y tokens de dispositivo: solo los dos últimos caracteres,
        // que son los que el cliente reconoce al leérselos por teléfono.
        return destino.Length <= 2
            ? new MaskedRecipient("***")
            : new MaskedRecipient($"***{destino[^2..]}");
    }

    public override string ToString() => Value;
}
