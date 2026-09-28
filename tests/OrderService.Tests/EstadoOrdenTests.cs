using FluentAssertions;

using HubNegocios.OrderService.Application.UseCases;
using HubNegocios.OrderService.Domain.Entities;
using HubNegocios.SharedKernel.Http;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace HubNegocios.OrderService.Tests;

/// <summary>
/// La máquina de estados de la orden.
///
/// Estas reglas son las que evitan los descuadres contables: si una orden
/// pagada se pudiera «cancelar» sin más, el dinero cobrado quedaría sin
/// contrapartida y solo se descubriría al cerrar el mes.
/// </summary>
public sealed class EstadoOrdenTests
{
    private static Order CrearOrden() => Order.Place(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "ORD-2026-000001",
        OrderType.Purchase,
        "COP",
        null,
        [OrderItem.Create(Guid.NewGuid(), Guid.NewGuid(), "Botella", "SKU-1", 2, 50_000m, null)]);

    [Fact]
    public void NaceEnPendienteYConElTotalCalculado()
    {
        var orden = CrearOrden();

        orden.Status.Should().Be(OrderStatus.Pending);
        orden.TotalAmount.Should().Be(100_000m);
    }

    [Fact]
    public void RecorreElCaminoNormalCompleto()
    {
        var orden = CrearOrden();

        orden.Confirm();
        orden.MarkPaid();
        orden.Fulfill();

        orden.Status.Should().Be(OrderStatus.Fulfilled);
    }

    [Fact]
    public void NoSePuedePagarLoQueNoSeConfirmo()
    {
        var orden = CrearOrden();

        var acto = () => orden.MarkPaid();

        acto.Should().Throw<ConflictException>()
            .Which.Code.Should().Be("ORDER_INVALID_TRANSITION");
    }

    [Fact]
    public void UnaOrdenPagadaNoSeCancela()
    {
        var orden = CrearOrden();
        orden.Confirm();
        orden.MarkPaid();

        var acto = () => orden.Cancel();

        // A partir del pago ya no es cancelar, es devolver: mueve dinero y
        // necesita su propio rastro contable.
        acto.Should().Throw<ConflictException>();
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed)]
    public void SeCancelaAntesDelPago(OrderStatus desde)
    {
        var orden = CrearOrden();
        if (desde == OrderStatus.Confirmed)
        {
            orden.Confirm();
        }

        var anterior = orden.Cancel();

        anterior.Should().Be(desde);
        orden.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void RepetirLaMismaTransicionNoEsUnError()
    {
        var orden = CrearOrden();
        orden.Cancel();

        var acto = () => orden.Cancel();

        // Los eventos se entregan «al menos una vez»: el segundo intento debe
        // ser inocuo, no reventar.
        acto.Should().NotThrow();
    }

    [Fact]
    public void UnaOrdenSinLineasNoEsUnaOrden()
    {
        var acto = () => Order.Place(
            Guid.NewGuid(), Guid.NewGuid(), "ORD-1", OrderType.Purchase, "COP", null, []);

        acto.Should().Throw<ValidationException>();
    }

    [Fact]
    public void RechazaCantidadCero()
    {
        var acto = () => OrderItem.Create(Guid.NewGuid(), Guid.NewGuid(), "X", null, 0, 100m, null);

        acto.Should().Throw<ValidationException>();
    }
}

public sealed class HistorialOrdenesTests
{
    [Fact]
    public async Task ElTamanoDePaginaTieneTopeDuro()
    {
        var repositorio = new RepositorioOrdenesEnMemoria();
        var handler = new GetOrderHistoryHandler(repositorio);

        var resultado = await handler.HandleAsync(Guid.NewGuid(), page: 1, pageSize: 1_000_000);

        // Sin tope, esta petición se traería la tabla entera a memoria: es una
        // forma trivial de tumbar el servicio desde fuera.
        resultado.PageSize.Should().Be(GetOrderHistoryHandler.MaxPageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task UnaPaginaInvalidaSeCorrigeALaPrimera(int pagina)
    {
        var handler = new GetOrderHistoryHandler(new RepositorioOrdenesEnMemoria());

        var resultado = await handler.HandleAsync(Guid.NewGuid(), pagina, pageSize: 20);

        resultado.Page.Should().Be(1);
    }
}

public sealed class CancelarOrdenTests
{
    [Fact]
    public async Task CancelarDejaRastroYAvisaAlResto()
    {
        var repositorio = new RepositorioOrdenesEnMemoria();
        var outbox = new OutboxEnMemoria();

        var orden = Order.Place(
            Guid.NewGuid(), Guid.NewGuid(), "ORD-2026-000001", OrderType.Reservation, "COP", null,
            [OrderItem.Create(Guid.NewGuid(), Guid.NewGuid(), "Mesa", null, 1, 0m, null)]);
        repositorio.Precargar(orden);

        var handler = new CancelOrderHandler(
            repositorio,
            outbox,
            new RelojFijo(DateTimeOffset.UnixEpoch),
            NullLogger<CancelOrderHandler>.Instance);

        await handler.HandleAsync(new CancelOrderCommand(orden.Id, "El cliente no puede venir"));

        orden.Status.Should().Be(OrderStatus.Cancelled);
        repositorio.Historial.Should().ContainSingle();

        // Este evento es el que devuelve el cupo en reservations-service y
        // libera el stock en inventory-service. Sin él, quedarían apartados
        // para siempre sin que nadie supiera por qué.
        outbox.Eventos.Should().ContainSingle().Which.Should().Be("order.cancelled");
    }

    [Fact]
    public async Task CancelarDosVecesNoDuplicaElEvento()
    {
        var repositorio = new RepositorioOrdenesEnMemoria();
        var outbox = new OutboxEnMemoria();

        var orden = Order.Place(
            Guid.NewGuid(), Guid.NewGuid(), "ORD-2026-000001", OrderType.Purchase, "COP", null,
            [OrderItem.Create(Guid.NewGuid(), Guid.NewGuid(), "X", null, 1, 10m, null)]);
        repositorio.Precargar(orden);

        var handler = new CancelOrderHandler(
            repositorio, outbox, new RelojFijo(DateTimeOffset.UnixEpoch),
            NullLogger<CancelOrderHandler>.Instance);

        await handler.HandleAsync(new CancelOrderCommand(orden.Id, null));
        await handler.HandleAsync(new CancelOrderCommand(orden.Id, null));

        outbox.Eventos.Should().HaveCount(1);
    }

    [Fact]
    public async Task CancelarAlgoQueNoExisteDaNoEncontrado()
    {
        var handler = new CancelOrderHandler(
            new RepositorioOrdenesEnMemoria(), new OutboxEnMemoria(),
            new RelojFijo(DateTimeOffset.UnixEpoch), NullLogger<CancelOrderHandler>.Instance);

        var acto = async () => await handler.HandleAsync(new CancelOrderCommand(Guid.NewGuid(), null));

        await acto.Should().ThrowAsync<NotFoundException>();
    }
}
