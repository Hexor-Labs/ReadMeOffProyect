using HubNegocios.IdentityService.Domain.Ports;

namespace HubNegocios.IdentityService.Infrastructure.Security;

/// <summary>
/// Hasheo con BCrypt, factor de coste 12.
///
/// El factor de coste no es un número decorativo: cada unidad DUPLICA el
/// trabajo. 12 deja cada comprobación en unos cientos de milisegundos en
/// hardware normal, que es imperceptible al iniciar sesión y carísimo para
/// quien intente probar millones de contraseñas contra una tabla robada. Subir
/// el factor cuando las máquinas mejoren es la forma de que esto siga siendo
/// verdad dentro de cinco años.
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    /*
      Hash de descarte contra el que comparar cuando el correo no existe.

      Se calcula una vez al arrancar, con el mismo factor de coste que los
      reales, para que comprobar una contraseña contra "usuario inexistente"
      cueste lo mismo que comprobarla contra un usuario de verdad.

      Sin esto, el login responde en un milisegundo cuando el correo no existe y
      en doscientos cuando sí, y cualquiera puede averiguar qué direcciones
      están dadas de alta cronometrando las respuestas. Que el mensaje de error
      sea idéntico no sirve de nada: el reloj lo cuenta todo.
    */
    private static readonly string DummyHash =
        BCrypt.Net.BCrypt.HashPassword("contraseña-que-nadie-usa", WorkFactor);

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    public bool Verify(string password, string? hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            // Se gasta el tiempo igualmente y se descarta el resultado.
            BCrypt.Net.BCrypt.Verify(password ?? string.Empty, DummyHash);
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password ?? string.Empty, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // Hash corrupto o de otro algoritmo. No es motivo para reventar el
            // login: se trata como credencial incorrecta y queda el registro.
            return false;
        }
    }
}
