using FluentAssertions;

using HubNegocios.ReservationsService.Domain.Entities;
using HubNegocios.ReservationsService.Domain.ValueObjects;
using HubNegocios.SharedKernel.Http;

using Xunit;

namespace HubNegocios.ReservationsService.Tests;

/// <summary>
/// Reglas del cupo.
///
/// Estas comprobaciones son sobre el objeto en memoria. NO demuestran que dos
/// reservas simultáneas no puedan vender la misma mesa: eso solo se puede
/// probar contra un Postgres de verdad, porque lo que lo impide es el
/// <c>SELECT … FOR UPDATE</c> del repositorio, no la entidad. El test de
/// concurrencia real está en <c>ConcurrenciaReservaTests</c>, marcado para
/// saltarse sin Docker.
/// </summary>
public sealed class TimeSlotTests
{
    private static TimeSlot CrearFranja(int aforo = 4) => TimeSlot.Open(
        Guid.NewGuid(),
        Guid.NewGuid(),
        new DateTime(2026, 5, 1, 20, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 5, 1, 23, 0, 0, DateTimeKind.Utc),
        aforo);

    [Fact]
    public void NaceConTodoElAforoDisponible()
    {
        var franja = CrearFranja(aforo: 10);

        franja.TotalCapacity.Should().Be(10);
        franja.AvailableCapacity.Should().Be(10);
        franja.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void LasHorasSeGuardanEnUtc()
    {
        var franja = CrearFranja();

        // Una franja en hora local deja de significar lo mismo en cuanto el
        // negocio abre una sede en otro huso.
        franja.SlotStart.Kind.Should().Be(DateTimeKind.Utc);
        franja.SlotEnd.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void RechazaUnaFranjaQueTerminaAntesDeEmpezar()
    {
        var acto = () => TimeSlot.Open(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 5, 1, 23, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 1, 20, 0, 0, DateTimeKind.Utc),
            4);

        acto.Should().Throw<DomainException>()
            .Which.Code.Should().Be("RESERVATION_SLOT_RANGE_INVALID");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void RechazaUnAforoQueNoEsPositivo(int aforo)
    {
        var acto = () => TimeSlot.Open(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 5, 1, 20, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 1, 23, 0, 0, DateTimeKind.Utc),
            aforo);

        acto.Should().Throw<DomainException>()
            .Which.Code.Should().Be("RESERVATION_SLOT_CAPACITY_INVALID");
    }

    [Fact]
    public void ReservarDescuentaDelCupo()
    {
        var franja = CrearFranja(aforo: 6);

        franja.Reserve(4);

        franja.AvailableCapacity.Should().Be(2);
    }

    [Fact]
    public void NoSePuedeReservarMasDeLoQueQueda()
    {
        var franja = CrearFranja(aforo: 4);
        franja.Reserve(3);

        var acto = () => franja.Reserve(2);

        acto.Should().Throw<DomainException>();
        franja.AvailableCapacity.Should().Be(1, "un intento fallido no puede dejar el cupo tocado");
    }

    [Fact]
    public void ElCupoNuncaBajaDeCero()
    {
        var franja = CrearFranja(aforo: 2);

        var acto = () => franja.Reserve(5);

        acto.Should().Throw<DomainException>();
        franja.AvailableCapacity.Should().Be(2);
    }

    [Fact]
    public void LiberarDevuelveLasPlazas()
    {
        var franja = CrearFranja(aforo: 6);
        franja.Reserve(4);

        franja.Release(4);

        franja.AvailableCapacity.Should().Be(6);
    }

    [Fact]
    public void ElCupoDisponibleNuncaSuperaAlAforo()
    {
        var franja = CrearFranja(aforo: 4);
        franja.Reserve(2);

        // Se libera de más, como pasaría si un fallo más arriba cancelara dos
        // veces la misma reserva.
        franja.Release(10);

        /*
          La entidad topa en el aforo en vez de protestar, y es la decisión
          correcta para una cancelación: lanzar aquí bloquearía una cancelación
          legítima por culpa de un error anterior. Topando, el peor caso es un
          cupo inexacto; sin topar, el local vendería más sillas de las que
          tiene y se descubriría cuando llegara la gente de más.
        */
        franja.AvailableCapacity.Should().Be(4);
    }

    [Fact]
    public void RechazaLiberarUnNumeroDePlazasQueNoEsPositivo()
    {
        var franja = CrearFranja();

        var acto = () => franja.Release(0);

        acto.Should().Throw<DomainException>();
    }

    [Fact]
    public void UnaFranjaBloqueadaNoAdmiteReservas()
    {
        var franja = CrearFranja();
        franja.Block("Evento privado");

        var acto = () => franja.Reserve(1);

        acto.Should().Throw<DomainException>();
    }

    [Fact]
    public void DesbloquearDevuelveLaFranjaAlServicio()
    {
        var franja = CrearFranja();
        franja.Block("Reforma");

        franja.Unblock();

        franja.IsBlocked.Should().BeFalse();
        franja.ReasonIfBlocked.Should().BeNull();
    }
}

public sealed class ConfirmationCodeTests
{
    [Fact]
    public void TieneElFormatoAcordado()
    {
        var codigo = ConfirmationCode.Generate();

        codigo.Value.Should().StartWith(ConfirmationCode.Prefix);
        codigo.Value.Should().HaveLength(ConfirmationCode.MaxLength);
    }

    [Fact]
    public void DosCodigosSeguidosNoSonIguales()
    {
        var codigos = Enumerable.Range(0, 200)
            .Select(_ => ConfirmationCode.Generate().Value)
            .ToList();

        // No demuestra que sea criptográficamente sólido, pero sí detecta el
        // fallo clásico: sembrar el generador con la hora y que dos llamadas
        // seguidas devuelvan lo mismo. Un código adivinable deja ver reservas
        // ajenas.
        codigos.Distinct().Should().HaveCountGreaterThan(190);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("BOOK-")]
    [InlineData("RESV-1234")]
    public void RechazaCodigosConOtroFormato(string entrada)
    {
        var acto = () => ConfirmationCode.Create(entrada);

        acto.Should().Throw<DomainException>();
    }
}

public sealed class ReservationTests
{
    private static Reservation CrearReserva(int comensales = 4) => Reservation.Confirm(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        comensales,
        "  Ana Pérez  ",
        "3001234567",
        "  ANA@CORREO.COM  ",
        null,
        ConfirmationCode.Generate());

    [Fact]
    public void NaceConfirmadaYNormalizaLosDatosDelCliente()
    {
        var reserva = CrearReserva();

        reserva.Status.Should().Be(ReservationStatus.Confirmed);
        reserva.CustomerName.Should().Be("Ana Pérez");
        reserva.CustomerEmail.Should().Be("ana@correo.com");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void RechazaUnNumeroDeComensalesQueNoEsPositivo(int comensales)
    {
        var acto = () => CrearReserva(comensales);

        acto.Should().Throw<DomainException>()
            .Which.Code.Should().Be("RESERVATION_PARTY_SIZE_INVALID");
    }

    [Fact]
    public void CancelarDevuelveLasPlazasParaQueSeLiberenEnLaFranja()
    {
        var reserva = CrearReserva(comensales: 4);

        var plazas = reserva.Cancel();

        plazas.Should().Be(4);
        reserva.Status.Should().Be(ReservationStatus.Cancelled);
    }

    [Fact]
    public void NoSeRegistraLlegadaDeUnaReservaCancelada()
    {
        var reserva = CrearReserva();
        reserva.Cancel();

        var acto = () => reserva.CheckIn();

        acto.Should().Throw<DomainException>();
    }

    [Fact]
    public void UnaReservaConLlegadaRegistradaYaNoSeCancela()
    {
        var reserva = CrearReserva();
        reserva.CheckIn();

        var acto = () => reserva.Cancel();

        // El cliente ya está sentado: liberar el cupo aquí lo vendería otra vez
        // con la mesa ocupada.
        acto.Should().Throw<DomainException>();
    }
}
