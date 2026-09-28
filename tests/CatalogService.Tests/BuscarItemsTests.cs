using FluentAssertions;

using HubNegocios.CatalogService.Application.UseCases;
using HubNegocios.SharedKernel.Http;

using Xunit;

namespace HubNegocios.CatalogService.Tests;

public sealed class BuscarItemsTests
{
    private readonly RepositorioItemsEnMemoria _repositorio = new();
    private readonly SearchItemsHandler _handler;

    public BuscarItemsTests() => _handler = new SearchItemsHandler(_repositorio);

    private static SearchItemsQuery Consulta(int page = 1, int pageSize = 20) =>
        new(null, null, null, null, page, pageSize);

    [Fact]
    public async Task RecortaElTamanoDePaginaAlTopeDelServicio()
    {
        using var ambito = Escenario.AbrirAmbito();

        var resultado = await _handler.HandleAsync(Consulta(pageSize: 5000));

        resultado.PageSize.Should().Be(SearchItemsHandler.MaxPageSize);

        // Y lo que de verdad importa: el número que llega al SQL va acotado, así
        // que nadie puede pedir diez mil filas por mucho que las escriba en la
        // cadena de consulta.
        _repositorio.UltimosCriterios!.PageSize.Should().Be(SearchItemsHandler.MaxPageSize);
    }

    [Fact]
    public async Task UsaElTamanoPorDefectoCuandoNoSePideNinguno()
    {
        using var ambito = Escenario.AbrirAmbito();

        var resultado = await _handler.HandleAsync(Consulta(pageSize: 0));

        resultado.PageSize.Should().Be(SearchItemsHandler.DefaultPageSize);
    }

    [Fact]
    public async Task LaPaginaNuncaEsMenorQueUno()
    {
        using var ambito = Escenario.AbrirAmbito();

        var resultado = await _handler.HandleAsync(Consulta(page: -3));

        resultado.Page.Should().Be(1);

        // Un Skip negativo reventaría la consulta en Postgres.
        _repositorio.UltimosCriterios!.Skip.Should().Be(0);
    }

    [Fact]
    public async Task DevuelveLaPaginaPedidaConElTotalSinPaginar()
    {
        using var ambito = Escenario.AbrirAmbito();

        for (var i = 1; i <= 5; i++)
        {
            _repositorio.Precargar(Escenario.CrearItem(sku: $"SKU-{i}", name: $"Item {i}"));
        }

        var resultado = await _handler.HandleAsync(Consulta(page: 2, pageSize: 2));

        resultado.Total.Should().Be(5);
        resultado.Page.Should().Be(2);
        resultado.TotalPages.Should().Be(3);
        resultado.Items.Should().HaveCount(2);
        resultado.Items.Select(item => item.Name).Should().Equal("Item 3", "Item 4");
    }

    [Fact]
    public async Task FiltraPorCategoriaYRangoDePrecio()
    {
        using var ambito = Escenario.AbrirAmbito();
        var categoria = Guid.NewGuid();

        _repositorio.Precargar(Escenario.CrearItem(sku: "SKU-A", name: "Barato", price: 1000m, categoryId: categoria));
        _repositorio.Precargar(Escenario.CrearItem(sku: "SKU-B", name: "Caro", price: 9000m, categoryId: categoria));
        _repositorio.Precargar(Escenario.CrearItem(sku: "SKU-C", name: "Otra categoría", price: 1000m));

        var resultado = await _handler.HandleAsync(new SearchItemsQuery(categoria, null, 500m, 5000m, 1, 10));

        resultado.Items.Should().ContainSingle().Which.Name.Should().Be("Barato");
    }

    [Fact]
    public async Task BuscaLasEtiquetasEnMinusculasYExigeTodasLasPedidas()
    {
        using var ambito = Escenario.AbrirAmbito();

        _repositorio.Precargar(Escenario.CrearItem(sku: "SKU-A", name: "Ensalada", tags: ["Vegano", "Sin-Gluten"]));
        _repositorio.Precargar(Escenario.CrearItem(sku: "SKU-B", name: "Pan", tags: ["vegano"]));

        var resultado = await _handler.HandleAsync(
            new SearchItemsQuery(null, ["VEGANO", "sin-gluten"], null, null, 1, 10));

        // Se guardan en minúsculas, así que la búsqueda también las baja: sin eso
        // el filtro no encontraría nada y parecería roto el índice.
        _repositorio.UltimosCriterios!.Tags.Should().Equal("vegano", "sin-gluten");
        resultado.Items.Should().ContainSingle().Which.Name.Should().Be("Ensalada");
    }

    [Fact]
    public async Task RechazaUnRangoDePrecioInvertido()
    {
        using var ambito = Escenario.AbrirAmbito();

        var acto = async () => await _handler.HandleAsync(new SearchItemsQuery(null, null, 9000m, 1000m, 1, 10));

        (await acto.Should().ThrowAsync<ValidationException>())
            .Which.Details.Should().ContainKey("minPrice");
    }
}
