using System.Diagnostics;

using FluentAssertions;

using HubNegocios.IdentityService.Domain.Entities;
using HubNegocios.IdentityService.Domain.Ports;
using HubNegocios.IdentityService.Domain.ValueObjects;
using HubNegocios.IdentityService.Infrastructure.Security;
using HubNegocios.SharedKernel.Http;

using Xunit;

namespace HubNegocios.IdentityService.Tests;

/// <summary>
/// Reglas de bloqueo por intentos fallidos.
///
/// Es la defensa contra la fuerza bruta: sin ella, probar un diccionario contra
/// una cuenta es cuestión de tiempo de CPU. Con cinco intentos y quince minutos
/// de espera, ese mismo diccionario tarda años.
/// </summary>
public sealed class BloqueoPorIntentosTests
{
    private static readonly DateTime Ahora = new(2026, 4, 10, 9, 0, 0, DateTimeKind.Utc);

    private static User CrearUsuario() => User.Register(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Email.Create("ana@correo.com"),
        "Ana Pérez",
        null,
        "$2a$12$hashdementira",
        UserRole.Customer,
        Ahora,
        acceptsTerms: true,
        acceptsPrivacyPolicy: true);

    [Fact]
    public void UnUsuarioNuevoNoEstaBloqueado()
    {
        var usuario = CrearUsuario();

        usuario.IsLockedAt(Ahora).Should().BeFalse();
        usuario.LoginAttempts.Should().Be(0);
    }

    [Fact]
    public void CuatroFallosTodaviaNoBloquean()
    {
        var usuario = CrearUsuario();

        for (var i = 0; i < User.MaxLoginAttempts - 1; i++)
        {
            usuario.RegisterFailedLogin(Ahora);
        }

        usuario.IsLockedAt(Ahora).Should().BeFalse();
    }

    [Fact]
    public void AlQuintoFalloSeBloquea()
    {
        var usuario = CrearUsuario();

        for (var i = 0; i < User.MaxLoginAttempts; i++)
        {
            usuario.RegisterFailedLogin(Ahora);
        }

        usuario.IsLockedAt(Ahora).Should().BeTrue();
        usuario.LockedUntil.Should().NotBeNull();
    }

    [Fact]
    public void ElBloqueoSeLevantaSoloAlPasarElTiempo()
    {
        var usuario = CrearUsuario();
        for (var i = 0; i < User.MaxLoginAttempts; i++)
        {
            usuario.RegisterFailedLogin(Ahora);
        }

        // Temporal y no permanente a propósito: un bloqueo que solo levanta un
        // administrador convierte un ataque contra la cuenta de alguien en una
        // forma fácil de dejarlo fuera para siempre.
        usuario.IsLockedAt(Ahora.AddHours(1)).Should().BeFalse();
    }

    [Fact]
    public void UnAciertoBorraLosIntentosFallidos()
    {
        var usuario = CrearUsuario();
        usuario.RegisterFailedLogin(Ahora);
        usuario.RegisterFailedLogin(Ahora);

        usuario.RegisterSuccessfulLogin(Ahora);

        usuario.LoginAttempts.Should().Be(0);
        usuario.LastLogin.Should().Be(Ahora);
    }
}

/// <summary>
/// El hasher, incluida la parte que casi nunca se prueba: que tarde lo mismo
/// cuando el usuario no existe.
/// </summary>
public sealed class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new();

    [Fact]
    public void ElHashNoSeParecELaContrasena()
    {
        var hash = _hasher.Hash("una-contraseña-larga");

        hash.Should().NotContain("una-contraseña-larga");
        hash.Should().StartWith("$2");
    }

    [Fact]
    public void DosHashesDeLaMismaContrasenaSonDistintos()
    {
        var a = _hasher.Hash("misma-contraseña");
        var b = _hasher.Hash("misma-contraseña");

        // BCrypt añade sal aleatoria. Si salieran iguales, una tabla robada
        // delataría qué usuarios comparten contraseña.
        a.Should().NotBe(b);
    }

    [Fact]
    public void VerificaLaContrasenaCorrecta() =>
        _hasher.Verify("correcta", _hasher.Hash("correcta")).Should().BeTrue();

    [Fact]
    public void RechazaLaContrasenaIncorrecta() =>
        _hasher.Verify("incorrecta", _hasher.Hash("correcta")).Should().BeFalse();

    [Fact]
    public void UnHashCorruptoNoRevientaElLogin() =>
        _hasher.Verify("lo-que-sea", "esto-no-es-un-hash").Should().BeFalse();

    [Fact]
    public void ComprobarContraNadaTardaLoMismoQueComprobarDeVerdad()
    {
        var hashReal = _hasher.Hash("contraseña-real");

        // Se calientan las dos rutas para no medir la compilación JIT.
        _hasher.Verify("intento", hashReal);
        _hasher.Verify("intento", null);

        var conHash = Medir(() => _hasher.Verify("intento", hashReal));
        var sinHash = Medir(() => _hasher.Verify("intento", null));

        /*
          Sin el hash de descarte, la rama del nulo devolvería false al instante
          y esta proporción se dispararía: el login respondería en un
          milisegundo cuando el correo no existe y en cientos cuando sí, y
          cualquiera podría averiguar qué direcciones están dadas de alta
          cronometrando las respuestas.

          El margen es ancho —entre la mitad y el doble— porque medir tiempos en
          una máquina compartida es ruidoso. Detecta la diferencia de orden de
          magnitud, que es la que se explota.
        */
        var proporcion = sinHash.TotalMilliseconds / Math.Max(conHash.TotalMilliseconds, 0.001);
        proporcion.Should().BeInRange(0.5, 2.0);
    }

    private static TimeSpan Medir(Action accion)
    {
        var reloj = Stopwatch.StartNew();
        accion();
        reloj.Stop();
        return reloj.Elapsed;
    }
}

public sealed class EmailTests
{
    [Fact]
    public void SeNormalizaAMinusculas() =>
        Email.Create("  ANA@Correo.COM  ").Value.Should().Be("ana@correo.com");

    [Theory]
    [InlineData("")]
    [InlineData("sin-arroba")]
    [InlineData("@sindominio")]
    [InlineData("espacio en@medio.com")]
    public void RechazaCorreosInvalidos(string entrada)
    {
        var acto = () => Email.Create(entrada);

        acto.Should().Throw<DomainException>();
    }
}
