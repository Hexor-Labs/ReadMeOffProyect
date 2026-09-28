namespace HubNegocios.IdentityService.Domain.Ports;

/// <summary>
/// Puerto del hasheo de contraseñas.
///
/// El dominio define qué necesita —guardar un hash y comprobar una contraseña
/// contra él— y la infraestructura decide con qué. Esa separación es la que
/// permitirá cambiar de algoritmo el día que BCrypt deje de ser suficiente sin
/// tocar ni un caso de uso.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Calcula el hash que se guarda. La contraseña en claro muere en esta llamada.</summary>
    string Hash(string password);

    /// <param name="hash">
    /// Puede ser nulo, y ese caso es la parte importante del contrato: cuando el
    /// correo no existe, el caso de uso llama igualmente a este método con nulo y
    /// la implementación debe gastar el MISMO tiempo que en una comprobación real
    /// antes de devolver <c>false</c>.
    ///
    /// Sin eso, el login responde en un milisegundo cuando el correo no existe y
    /// en doscientos cuando sí, y cualquiera puede averiguar qué direcciones
    /// están dadas de alta cronometrando las respuestas. Da igual que el mensaje
    /// de error sea el mismo: el reloj lo cuenta todo.
    /// </param>
    bool Verify(string password, string? hash);
}
