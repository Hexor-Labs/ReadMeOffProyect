using FluentAssertions;

using HubNegocios.CatalogService.Application.UseCases;
using HubNegocios.SharedKernel.Http;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace HubNegocios.CatalogService.Tests;

/// <summary>
/// El borrado lógico y su consecuencia menos evidente: que el filtro global
/// combinado —tenant Y no borrado— de verdad esconde el item.
///
/// Este es el test que protege la decisión explicada en
/// <c>CatalogDbContext.OnModelCreating</c>. Si alguien separa el filtro de
/// tenant del de borrado, EF Core se queda con uno solo sin avisar y estos casos
/// empiezan a fallar, que es justo para lo que están.
/// </summary>
public sealed class BorradoLogicoItemTests
{
    private readonly RepositorioItemsEnMemoria _repositorio = new();
    private readonly OutboxEnMemoria _outbox = new();
    private readonly SoftDeleteItemHandler _handler;
    private readonly SearchItemsHandler _busqueda;
    private readonly GetItemsByIdsHandler _porIds;

    public BorradoLogicoItemTests()
    {
        _handler = new SoftDeleteItemHandler(
            _repositorio,
            _outbox,
            new FakeTimeProvider(Escenario.Ahora),
            NullLogger<SoftDeleteItemHandler>.Instance);

        _busqueda = new SearchItemsHandler(_repositorio);
        _porIds = new GetItemsByIdsHandler(_repositorio);
    }

    [Fact]
    public async Task ElBorradoNoQuitaLaFilaDeLaTabla()
    {
        using var ambito = Escenario.AbrirAmbito();
        var item = _repositorio.Precargar(Escenario.CrearItem());

        await _handler.HandleAsync(new SoftDeleteItemCommand(item.Id, "descatalogado"));

        item.IsDeleted.Should().BeTrue();
        item.IsActive.Should().BeFalse("un item retirado tampoco se ofrece");

        // Lo importante: hay órdenes viejas que apuntan a esta fila.
        _repositorio.FilasEnTabla.Should().Be(1);
    }

    [Fact]
    public async Task UnItemBorradoNoSaleEnLasBusquedas()
    {
        using var ambito = Escenario.AbrirAmbito();
        _repositorio.Precargar(Escenario.CrearItem(sku: "CAFE-01", name: "Café"));
        var retirado = _repositorio.Precargar(Escenario.CrearItem(sku: "TE-01", name: "Té"));

        var antes = await _busqueda.HandleAsync(new SearchItemsQuery(null, null, null, null, 1, 10));
        antes.Total.Should().Be(2);

        await _handler.HandleAsync(new SoftDeleteItemCommand(retirado.Id, null));

        var despues = await _busqueda.HandleAsync(new SearchItemsQuery(null, null, null, null, 1, 10));

        despues.Total.Should().Be(1, "el total tampoco puede contar los borrados");
        despues.Items.Should().ContainSingle().Which.Sku.Should().Be("CAFE-01");
    }

    [Fact]
    public async Task UnItemBorradoNoSaleEnLaConsultaPorIdsYSeReportaComoAusente()
    {
        using var ambito = Escenario.AbrirAmbito();
        var retirado = _repositorio.Precargar(Escenario.CrearItem());

        await _handler.HandleAsync(new SoftDeleteItemCommand(retirado.Id, null));

        var resultado = await _porIds.HandleAsync([retirado.Id]);

        // Es lo que necesita order-service: que un item retirado no se pueda
        // meter en una orden nueva, aunque el cliente tenga su id en el carrito.
        resultado.Items.Should().BeEmpty();
        resultado.MissingIds.Should().Equal(retirado.Id);
    }

    [Fact]
    public async Task ElBorradoEmiteItemDeletedYSeGuardaDeUnaSolaVez()
    {
        using var ambito = Escenario.AbrirAmbito();
        var item = _repositorio.Precargar(Escenario.CrearItem());

        await _handler.HandleAsync(new SoftDeleteItemCommand(item.Id, null));

        _outbox.Eventos.Should().ContainSingle().Which.EventType.Should().Be("item.deleted");
        _repositorio.VecesGuardado.Should().Be(1);
    }

    [Fact]
    public async Task RepetirElBorradoRespondeQueNoExiste()
    {
        using var ambito = Escenario.AbrirAmbito();
        var item = _repositorio.Precargar(Escenario.CrearItem());

        await _handler.HandleAsync(new SoftDeleteItemCommand(item.Id, null));

        var acto = async () => await _handler.HandleAsync(new SoftDeleteItemCommand(item.Id, null));

        // Consecuencia buscada del filtro: para quien pregunta, un item borrado
        // ya no existe. La fila sigue en su sitio, que es lo que se protege.
        (await acto.Should().ThrowAsync<NotFoundException>())
            .Which.Code.Should().Be("ITEM_NOT_FOUND");

        _repositorio.FilasEnTabla.Should().Be(1);
    }
}
