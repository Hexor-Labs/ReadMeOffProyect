using FluentAssertions;

using HubNegocios.CatalogService.Application.UseCases;
using HubNegocios.CatalogService.Domain.Entities;
using HubNegocios.SharedKernel.Http;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace HubNegocios.CatalogService.Tests;

public sealed class CrearItemTests
{
    private readonly RepositorioItemsEnMemoria _repositorio = new();
    private readonly OutboxEnMemoria _outbox = new();
    private readonly CreateItemHandler _handler;

    public CrearItemTests() => _handler = new CreateItemHandler(
        _repositorio,
        _outbox,
        NullLogger<CreateItemHandler>.Instance);

    private static CreateItemCommand Comando(
        string sku = "cafe-01",
        AvailabilityMode mode = AvailabilityMode.Unlimited,
        int? quantity = null) => new(
        sku,
        "Café con leche",
        "Doble de leche",
        CategoryId: null,
        Tags: ["Vegano", "vegano", "  "],
        PriceAmount: 5000m,
        PriceCurrency: "cop",
        mode,
        quantity,
        ImageUrl: null,
        ImageUrls: null,
        AttributesJson: null);

    [Fact]
    public async Task CreaElItemNormalizandoSkuMonedaYEtiquetas()
    {
        using var ambito = Escenario.AbrirAmbito();

        var resultado = await _handler.HandleAsync(Comando());

        resultado.Sku.Should().Be("CAFE-01");
        resultado.PriceCurrency.Should().Be("COP");

        var item = await _repositorio.GetByIdAsync(resultado.ItemId);
        item.Should().NotBeNull();
        item!.Tags.Should().Equal("vegano");
    }

    [Fact]
    public async Task EmiteItemCreatedConElItemComoAgregado()
    {
        using var ambito = Escenario.AbrirAmbito();

        var resultado = await _handler.HandleAsync(Comando());

        var evento = _outbox.Eventos.Should().ContainSingle().Subject;
        evento.EventType.Should().Be("item.created");
        evento.AggregateId.Should().Be(resultado.ItemId);
    }

    [Fact]
    public async Task ElEventoYElItemSeGuardanDeUnaSolaVez()
    {
        using var ambito = Escenario.AbrirAmbito();

        await _handler.HandleAsync(Comando());

        // Un único SaveChanges es lo que hace que el evento y el item caigan en
        // la misma transacción. Dos llamadas aquí serían dos transacciones y el
        // patrón Outbox dejaría de garantizar nada.
        _repositorio.VecesGuardado.Should().Be(1);
    }

    [Fact]
    public async Task RechazaUnSkuYaUsadoEnElMismoNegocio()
    {
        using var ambito = Escenario.AbrirAmbito();

        await _handler.HandleAsync(Comando());

        var acto = async () => await _handler.HandleAsync(Comando("CAFE-01"));

        (await acto.Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be("ITEM_SKU_TAKEN");
    }

    [Fact]
    public async Task NoDejaRastroSiElSkuEsInvalido()
    {
        using var ambito = Escenario.AbrirAmbito();

        var acto = async () => await _handler.HandleAsync(Comando("no válido"));

        await acto.Should().ThrowAsync<DomainException>();

        _outbox.Eventos.Should().BeEmpty("un SKU inválido no puede emitir eventos");
        _repositorio.VecesGuardado.Should().Be(0);
    }

    [Fact]
    public async Task UnModoConCupoExigeCantidad()
    {
        using var ambito = Escenario.AbrirAmbito();

        var acto = async () => await _handler.HandleAsync(Comando(mode: AvailabilityMode.StockBased));

        (await acto.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("ITEM_QUANTITY_REQUIRED");

        _repositorio.VecesGuardado.Should().Be(0);
    }
}
