using FluentAssertions;

using HubNegocios.CatalogService.Application.UseCases;
using HubNegocios.SharedKernel.Http;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace HubNegocios.CatalogService.Tests;

/// <summary>
/// El caso de uso con más reglas del servicio: cambiar el precio tiene que
/// dejar rastro y avisar, y las dos cosas dentro de la misma transacción.
/// </summary>
public sealed class CambiarPrecioItemTests
{
    private readonly RepositorioItemsEnMemoria _repositorio = new();
    private readonly OutboxEnMemoria _outbox = new();
    private readonly UpdateItemPriceHandler _handler;

    public CambiarPrecioItemTests() => _handler = new UpdateItemPriceHandler(
        _repositorio,
        _outbox,
        // Reloj fijo: un test que dependa de la hora real falla solo, un día
        // cualquiera, por motivos que no tienen que ver con el código.
        new FakeTimeProvider(Escenario.Ahora),
        NullLogger<UpdateItemPriceHandler>.Instance);

    [Fact]
    public async Task CambiarElPrecioDejaRastroEnElHistorial()
    {
        using var ambito = Escenario.AbrirAmbito();
        var item = _repositorio.Precargar(Escenario.CrearItem(price: 5000m));

        await _handler.HandleAsync(new UpdateItemPriceCommand(item.Id, 7500m, "subida de insumos"));

        var rastro = _repositorio.Historial.Should().ContainSingle().Subject;
        rastro.ItemId.Should().Be(item.Id);
        rastro.TenantId.Should().Be(Escenario.Tenant);
        rastro.OldPrice.Should().Be(5000m);
        rastro.NewPrice.Should().Be(7500m);
        rastro.ChangedBy.Should().Be(Escenario.Usuario, "el autor sale del contexto de ejecución");
        rastro.ChangedAt.Should().Be(Escenario.Ahora.UtcDateTime);
        rastro.Reason.Should().Be("subida de insumos");

        item.PriceAmount.Should().Be(7500m);
    }

    [Fact]
    public async Task CambiarElPrecioEmiteItemPriceChanged()
    {
        using var ambito = Escenario.AbrirAmbito();
        var item = _repositorio.Precargar(Escenario.CrearItem());

        await _handler.HandleAsync(new UpdateItemPriceCommand(item.Id, 9000m, null));

        var evento = _outbox.Eventos.Should().ContainSingle().Subject;
        evento.EventType.Should().Be("item.price_changed");
        evento.AggregateId.Should().Be(item.Id);
    }

    [Fact]
    public async Task ElPrecioElHistorialYElEventoSeGuardanDeUnaSolaVez()
    {
        using var ambito = Escenario.AbrirAmbito();
        var item = _repositorio.Precargar(Escenario.CrearItem());

        await _handler.HandleAsync(new UpdateItemPriceCommand(item.Id, 9000m, null));

        // Si esto fueran dos transacciones, podría quedar el precio cambiado sin
        // historial, o un evento avisando de un cambio que no se guardó.
        _repositorio.VecesGuardado.Should().Be(1);
    }

    [Fact]
    public async Task ReenviarElMismoPrecioNoEnsuciaElHistorialNiEmiteNada()
    {
        using var ambito = Escenario.AbrirAmbito();
        var item = _repositorio.Precargar(Escenario.CrearItem(price: 5000m));

        await _handler.HandleAsync(new UpdateItemPriceCommand(item.Id, 5000m, "sin cambios"));

        _repositorio.Historial.Should().BeEmpty();
        _outbox.Eventos.Should().BeEmpty();
        _repositorio.VecesGuardado.Should().Be(0);
    }

    [Fact]
    public async Task UnItemQueNoExisteDaNotFound()
    {
        using var ambito = Escenario.AbrirAmbito();

        var acto = async () => await _handler.HandleAsync(new UpdateItemPriceCommand(Guid.NewGuid(), 100m, null));

        (await acto.Should().ThrowAsync<NotFoundException>())
            .Which.Code.Should().Be("ITEM_NOT_FOUND");
    }

    [Fact]
    public async Task UnPrecioNegativoNoLlegaAlHistorial()
    {
        using var ambito = Escenario.AbrirAmbito();
        var item = _repositorio.Precargar(Escenario.CrearItem());

        var acto = async () => await _handler.HandleAsync(new UpdateItemPriceCommand(item.Id, -1m, null));

        (await acto.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("ITEM_PRICE_NEGATIVE");

        _repositorio.Historial.Should().BeEmpty();
        _repositorio.VecesGuardado.Should().Be(0);
    }
}
