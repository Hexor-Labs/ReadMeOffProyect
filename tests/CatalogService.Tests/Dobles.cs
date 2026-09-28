using HubNegocios.CatalogService.Domain.Entities;
using HubNegocios.CatalogService.Domain.Ports;
using HubNegocios.CatalogService.Domain.ValueObjects;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.CatalogService.Tests;

/// <summary>
/// Doble del repositorio, escrito a mano.
///
/// Sin librería de mocks a propósito: un doble se lee de un vistazo, mientras
/// que tres llamadas encadenadas a un framework de simulación hay que
/// descifrarlas. Además obliga a que el puerto siga siendo pequeño: el día que
/// esta clase empiece a doler, será señal de que la interfaz creció demasiado.
///
/// Dos cosas que este doble reproduce a conciencia, porque los tests que
/// importan dependen de ellas:
///
/// 1. El filtro global. Todas las lecturas pasan por <c>Item.Visible</c>, que es
///    LITERALMENTE la expresión que <c>CatalogDbContext</c> registra como filtro
///    global. Así, cuando un test comprueba que un item borrado no sale en las
///    búsquedas, está comprobando el mismo predicado que se ejecutará contra
///    Postgres y no una copia que puede desviarse.
/// 2. La asignación del tenant al guardar, que en producción hace
///    <c>TenantAssignmentInterceptor</c>. El tenant tiene setter privado —como
///    debe ser—, así que aquí se asigna por reflexión: es el precio de no
///    ensuciar el dominio con un método público que solo usarían los tests.
/// </summary>
internal sealed class RepositorioItemsEnMemoria : IItemRepository
{
    private static readonly Func<Item, bool> EsVisible = Item.Visible.Compile();

    private readonly List<Item> _items = [];

    public List<ItemPriceHistory> Historial { get; } = [];

    public int VecesGuardado { get; private set; }

    /// <summary>Criterios con los que se llamó a la búsqueda la última vez.</summary>
    public ItemSearchCriteria? UltimosCriterios { get; private set; }

    /// <summary>
    /// Filas que hay en la tabla, borradas incluidas. Es la ventana que permite
    /// comprobar que el borrado lógico no borra nada de verdad.
    /// </summary>
    public int FilasEnTabla => _items.Count;

    /// <summary>Deja un item ya guardado, con el tenant del contexto puesto.</summary>
    public Item Precargar(Item item)
    {
        AsignarTenant(item);
        _items.Add(item);
        return item;
    }

    public Task<Item?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Visibles().FirstOrDefault(item => item.Id == id));

    public Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken = default) =>
        Task.FromResult(Visibles().Any(item => item.Sku == sku));

    public Task<ItemPage> SearchAsync(ItemSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        UltimosCriterios = criteria;

        var coincidencias = Visibles();

        if (criteria.CategoryId is { } categoryId)
        {
            coincidencias = coincidencias.Where(item => item.CategoryId == categoryId);
        }

        foreach (var etiqueta in criteria.Tags)
        {
            coincidencias = coincidencias.Where(item => item.Tags.Contains(etiqueta));
        }

        if (criteria.MinPrice is { } minPrice)
        {
            coincidencias = coincidencias.Where(item => item.PriceAmount >= minPrice);
        }

        if (criteria.MaxPrice is { } maxPrice)
        {
            coincidencias = coincidencias.Where(item => item.PriceAmount <= maxPrice);
        }

        var todas = coincidencias.ToList();

        var pagina = todas
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Id)
            .Skip(criteria.Skip)
            .Take(criteria.PageSize)
            .ToList();

        return Task.FromResult(new ItemPage(pagina, todas.Count));
    }

    public Task<IReadOnlyList<Item>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Item>>(Visibles().Where(item => ids.Contains(item.Id)).ToList());

    public void Add(Item item) => _items.Add(item);

    public void AddPriceHistory(ItemPriceHistory history) => Historial.Add(history);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        VecesGuardado++;

        // Igual que el interceptor de verdad: al guardar, lo que entró sin
        // tenant se queda con el del contexto.
        foreach (var item in _items)
        {
            AsignarTenant(item);
        }

        return Task.CompletedTask;
    }

    private IEnumerable<Item> Visibles() => _items.Where(EsVisible);

    private static void AsignarTenant(Item item)
    {
        if (item.TenantId != Guid.Empty)
        {
            return;
        }

        typeof(Item)
            .GetProperty(nameof(Item.TenantId))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(item, [TenantContext.Current.RequireTenantId()]);
    }
}

internal sealed record EventoEncolado(Guid AggregateId, string AggregateType, string EventType);

internal sealed class OutboxEnMemoria : IOutboxWriter
{
    public List<EventoEncolado> Eventos { get; } = [];

    public void Enqueue<TPayload>(Guid aggregateId, string aggregateType, string eventType, TPayload payload) =>
        Eventos.Add(new EventoEncolado(aggregateId, aggregateType, eventType));

    public void EnqueueFor<TPayload>(
        Guid tenantId,
        Guid aggregateId,
        string aggregateType,
        string eventType,
        TPayload payload) =>
        Eventos.Add(new EventoEncolado(aggregateId, aggregateType, eventType));
}

/// <summary>Reloj fijo para los tests.</summary>
internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// Utilidades compartidas por los tests: el tenant de turno y una fábrica de
/// items con valores por defecto razonables, para que cada test solo escriba lo
/// que de verdad está probando.
/// </summary>
internal static class Escenario
{
    public static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid Usuario = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly DateTimeOffset Ahora = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Abre el ámbito de tenant, que en producción abre el middleware al validar
    /// el token. Se llama dentro de cada test y no en el constructor: el valor
    /// vive en un <c>AsyncLocal</c> y fijarlo fuera del método no garantiza que
    /// llegue al cuerpo del test.
    /// </summary>
    public static IDisposable AbrirAmbito() =>
        TenantContext.BeginScope(new TenantIdentity(Tenant, Usuario, "Owner", Guid.NewGuid()));

    public static Item CrearItem(
        string sku = "CAFE-01",
        string name = "Café",
        decimal price = 5000m,
        IEnumerable<string>? tags = null,
        Guid? categoryId = null,
        AvailabilityMode mode = AvailabilityMode.Unlimited,
        int? quantity = null) => Item.Create(
        Guid.NewGuid(),
        Sku.Create(sku),
        name,
        description: null,
        categoryId,
        tags,
        Money.Create(price, "COP"),
        mode,
        quantity,
        imageUrl: null,
        imageUrls: null,
        attributesJson: null);
}
