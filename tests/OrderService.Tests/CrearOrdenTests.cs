using FluentAssertions;

using HubNegocios.OrderService.Application.UseCases;
using HubNegocios.OrderService.Domain.Entities;
using HubNegocios.OrderService.Domain.Ports;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace HubNegocios.OrderService.Tests;

internal sealed class RepositorioOrdenesEnMemoria : IOrderRepository
{
    private readonly List<Order> _ordenes = [];

    public List<OrderStatusHistory> Historial { get; } = [];
    public int VecesGuardado { get; private set; }

    public void Precargar(Order order) => _ordenes.Add(order);

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_ordenes.Find(o => o.Id == id));

    public Task<(IReadOnlyList<Order> Orders, int Total)> GetHistoryAsync(
        Guid customerId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var todas = _ordenes.Where(o => o.CustomerId == customerId).ToList();
        var pagina = todas.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult<(IReadOnlyList<Order>, int)>((pagina, todas.Count));
    }

    public Task<int> NextSequenceAsync(int year, CancellationToken cancellationToken = default) =>
        Task.FromResult(_ordenes.Count + 1);

    public void Add(Order order) => _ordenes.Add(order);

    public void AddStatusHistory(OrderStatusHistory history) => Historial.Add(history);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        VecesGuardado++;
        return Task.CompletedTask;
    }
}

internal sealed class CatalogoFalso : ICatalogClient
{
    private readonly Dictionary<Guid, CatalogItemSnapshot> _items = [];

    /// <summary>Si es true, simula que catalog-service no responde.</summary>
    public bool Caido { get; set; }

    public CatalogItemSnapshot Registrar(
        string nombre, decimal precio, string moneda = "COP", bool activo = true)
    {
        var snapshot = new CatalogItemSnapshot(Guid.NewGuid(), nombre, "SKU-1", precio, moneda, activo, "{}");
        _items[snapshot.ItemId] = snapshot;
        return snapshot;
    }

    public Task<IReadOnlyList<CatalogItemSnapshot>> GetItemsAsync(
        IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new DomainException("CATALOG_UNAVAILABLE", "El catálogo no responde.");
        }

        IReadOnlyList<CatalogItemSnapshot> encontrados =
            itemIds.Where(_items.ContainsKey).Select(id => _items[id]).ToList();

        return Task.FromResult(encontrados);
    }
}

internal sealed class OutboxEnMemoria : IOutboxWriter
{
    public List<string> Eventos { get; } = [];

    public void Enqueue<TPayload>(Guid aggregateId, string aggregateType, string eventType, TPayload payload) =>
        Eventos.Add(eventType);

    public void EnqueueFor<TPayload>(Guid tenantId, Guid aggregateId, string aggregateType, string eventType, TPayload payload) =>
        Eventos.Add(eventType);
}

internal sealed class RelojFijo(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public sealed class CrearOrdenTests
{
    private readonly RepositorioOrdenesEnMemoria _repositorio = new();
    private readonly CatalogoFalso _catalogo = new();
    private readonly OutboxEnMemoria _outbox = new();
    private readonly CreateOrderHandler _handler;

    public CrearOrdenTests() => _handler = new CreateOrderHandler(
        _repositorio,
        _catalogo,
        _outbox,
        new RelojFijo(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero)),
        NullLogger<CreateOrderHandler>.Instance);

    [Fact]
    public async Task ElTotalSaleDelCatalogoYNoDeLoQuePidaElCliente()
    {
        var botella = _catalogo.Registrar("Botella", precio: 150_000m);

        var resultado = await _handler.HandleAsync(new CreateOrderCommand(
            Guid.NewGuid(),
            OrderType.Purchase,
            null,
            [new OrderLineRequest(botella.ItemId, 3)]));

        // 3 × 150.000. Es el precio que confirmó el catálogo, no uno enviado
        // en el cuerpo de la petición: ahí está la diferencia entre cobrar bien
        // y dejar que el cliente ponga el precio.
        resultado.Total.Should().Be(450_000m);
        resultado.OrderNumber.Should().Be("ORD-2026-000001");
    }

    [Fact]
    public async Task SumaLasCantidadesSiElMismoItemLlegaEnDosLineas()
    {
        var botella = _catalogo.Registrar("Botella", precio: 100m);

        var resultado = await _handler.HandleAsync(new CreateOrderCommand(
            Guid.NewGuid(),
            OrderType.Purchase,
            null,
            [new OrderLineRequest(botella.ItemId, 2), new OrderLineRequest(botella.ItemId, 3)]));

        resultado.Total.Should().Be(500m);
    }

    [Fact]
    public async Task RechazaUnItemQueYaNoExiste()
    {
        var acto = async () => await _handler.HandleAsync(new CreateOrderCommand(
            Guid.NewGuid(), OrderType.Purchase, null, [new OrderLineRequest(Guid.NewGuid(), 1)]));

        await acto.Should().ThrowAsync<ValidationException>();
        _outbox.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task RechazaUnItemDesactivado()
    {
        var agotado = _catalogo.Registrar("Agotado", precio: 100m, activo: false);

        var acto = async () => await _handler.HandleAsync(new CreateOrderCommand(
            Guid.NewGuid(), OrderType.Purchase, null, [new OrderLineRequest(agotado.ItemId, 1)]));

        (await acto.Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be("ORDER_ITEM_UNAVAILABLE");
    }

    [Fact]
    public async Task NoMezclaMonedasEnLaMismaOrden()
    {
        var enPesos = _catalogo.Registrar("En pesos", 100m, "COP");
        var enDolares = _catalogo.Registrar("En dólares", 100m, "USD");

        var acto = async () => await _handler.HandleAsync(new CreateOrderCommand(
            Guid.NewGuid(),
            OrderType.Purchase,
            null,
            [new OrderLineRequest(enPesos.ItemId, 1), new OrderLineRequest(enDolares.ItemId, 1)]));

        // Sumar pesos con dólares da un número sin significado.
        (await acto.Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be("ORDER_MIXED_CURRENCIES");
    }

    [Fact]
    public async Task SiElCatalogoNoRespondeNoSeCreaNada()
    {
        var botella = _catalogo.Registrar("Botella", 100m);
        _catalogo.Caido = true;

        var acto = async () => await _handler.HandleAsync(new CreateOrderCommand(
            Guid.NewGuid(), OrderType.Purchase, null, [new OrderLineRequest(botella.ItemId, 1)]));

        (await acto.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("CATALOG_UNAVAILABLE");

        _repositorio.VecesGuardado.Should().Be(0, "sin precio confirmado no hay orden");
    }

    [Fact]
    public async Task LaOrdenYSuEventoSeGuardanDeUnaSolaVez()
    {
        var botella = _catalogo.Registrar("Botella", 100m);

        await _handler.HandleAsync(new CreateOrderCommand(
            Guid.NewGuid(), OrderType.Purchase, null, [new OrderLineRequest(botella.ItemId, 1)]));

        _outbox.Eventos.Should().ContainSingle().Which.Should().Be("order.created");
        _repositorio.VecesGuardado.Should().Be(1, "un solo SaveChanges es lo que los mete en la misma transacción");
    }
}
