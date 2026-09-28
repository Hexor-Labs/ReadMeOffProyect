using FluentAssertions;

using HubNegocios.CatalogService.Application.UseCases;
using HubNegocios.CatalogService.Domain.Entities;
using HubNegocios.SharedKernel.Http;

using Xunit;

namespace HubNegocios.CatalogService.Tests;

/// <summary>
/// La consulta que hace order-service antes de crear una orden. Lo que se prueba
/// aquí es lo que esa llamada promete: que dice qué ids no existen y que devuelve
/// la foto del precio y los atributos.
/// </summary>
public sealed class ConsultarItemsPorIdsTests
{
    private readonly RepositorioItemsEnMemoria _repositorio = new();
    private readonly GetItemsByIdsHandler _handler;

    public ConsultarItemsPorIdsTests() => _handler = new GetItemsByIdsHandler(_repositorio);

    [Fact]
    public async Task DevuelveLaFotoDelPrecioYLosAtributos()
    {
        using var ambito = Escenario.AbrirAmbito();
        var item = _repositorio.Precargar(Escenario.CrearItem(
            price: 5000m,
            mode: AvailabilityMode.StockBased,
            quantity: 3));

        var resultado = await _handler.HandleAsync([item.Id]);

        var foto = resultado.Items.Should().ContainSingle().Subject;
        foto.Id.Should().Be(item.Id);
        foto.PriceAmount.Should().Be(5000m);
        foto.PriceCurrency.Should().Be("COP");
        foto.AvailabilityMode.Should().Be(nameof(AvailabilityMode.StockBased));
        foto.AvailableQuantity.Should().Be(3);
        foto.AttributesJson.Should().Be("{}");
        resultado.MissingIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ReportaLosIdsQueNoExisten()
    {
        using var ambito = Escenario.AbrirAmbito();
        var item = _repositorio.Precargar(Escenario.CrearItem());
        var inventado = Guid.NewGuid();

        var resultado = await _handler.HandleAsync([item.Id, inventado]);

        resultado.Items.Should().ContainSingle();
        resultado.MissingIds.Should().Equal(inventado);
    }

    [Fact]
    public async Task IgnoraIdsRepetidosYVacios()
    {
        using var ambito = Escenario.AbrirAmbito();
        var item = _repositorio.Precargar(Escenario.CrearItem());

        var resultado = await _handler.HandleAsync([item.Id, item.Id, Guid.Empty]);

        resultado.Items.Should().ContainSingle();
        resultado.MissingIds.Should().BeEmpty();
    }

    [Fact]
    public async Task UnaListaVaciaNiSiquieraConsultaLaBaseDeDatos()
    {
        using var ambito = Escenario.AbrirAmbito();

        var resultado = await _handler.HandleAsync([]);

        resultado.Items.Should().BeEmpty();
        resultado.MissingIds.Should().BeEmpty();
    }

    [Fact]
    public async Task RechazaPedirMasItemsDeLosPermitidos()
    {
        using var ambito = Escenario.AbrirAmbito();

        var demasiados = Enumerable
            .Range(0, GetItemsByIdsHandler.MaxIds + 1)
            .Select(_ => Guid.NewGuid())
            .ToList();

        var acto = async () => await _handler.HandleAsync(demasiados);

        (await acto.Should().ThrowAsync<ValidationException>())
            .Which.Details.Should().ContainKey("itemIds");
    }
}
