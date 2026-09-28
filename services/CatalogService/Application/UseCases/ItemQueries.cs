using HubNegocios.CatalogService.Domain.Entities;
using HubNegocios.CatalogService.Domain.Ports;
using HubNegocios.SharedKernel.Http;

namespace HubNegocios.CatalogService.Application.UseCases;

public sealed record SearchItemsQuery(
    Guid? CategoryId,
    IReadOnlyCollection<string>? Tags,
    decimal? MinPrice,
    decimal? MaxPrice,
    int Page,
    int PageSize);

public sealed record ItemSummary(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    Guid? CategoryId,
    IReadOnlyList<string> Tags,
    decimal PriceAmount,
    string PriceCurrency,
    string AvailabilityMode,
    int? AvailableQuantity,
    string? ImageUrl,
    bool IsActive);

public sealed record SearchItemsResult(
    IReadOnlyList<ItemSummary> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages);

/// <summary>
/// Busca en el catálogo del negocio con filtros y paginación.
///
/// Es la consulta más frecuente del servicio: la hace la página pública en cada
/// visita. De ahí el tope de <see cref="MaxPageSize"/> — sin él, una sola
/// petición con <c>pageSize=50000</c> basta para traer el catálogo entero a
/// memoria, serializarlo y tumbar el servicio sin necesidad de ningún ataque
/// elaborado.
///
/// Los items borrados no aparecen aquí y no hace falta filtrarlos: el filtro
/// global del contexto los quita antes, en el mismo sitio donde quita los de
/// otros tenants. Ver <c>Item.Visible</c>.
/// </summary>
public sealed class SearchItemsHandler(IItemRepository repository)
{
    /// <summary>Tope duro de filas por página.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Tamaño cuando el cliente no pide ninguno.</summary>
    public const int DefaultPageSize = 20;

    public async Task<SearchItemsResult> HandleAsync(
        SearchItemsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.MinPrice is { } min && query.MaxPrice is { } max && min > max)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["minPrice"] = ["El precio mínimo no puede ser mayor que el máximo."],
            });
        }

        /*
          Se recorta en silencio en vez de rechazar la petición. Pedir más de lo
          permitido no es un error del cliente que merezca un 422: es una
          expectativa que el servidor no puede cumplir, y devolver las primeras
          cien filas es más útil que no devolver nada. Lo que no se negocia es
          que el número que llega al SQL esté acotado.
        */
        var pageSize = query.PageSize switch
        {
            <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            var solicitado => solicitado,
        };

        var page = Math.Max(query.Page, 1);

        var criteria = new ItemSearchCriteria(
            query.CategoryId,
            NormalizarEtiquetas(query.Tags),
            query.MinPrice,
            query.MaxPrice,
            page,
            pageSize);

        var pagina = await repository.SearchAsync(criteria, cancellationToken).ConfigureAwait(false);

        var totalPages = (int)Math.Ceiling(pagina.Total / (double)pageSize);

        return new SearchItemsResult(
            pagina.Items.Select(Resumir).ToList(),
            pagina.Total,
            page,
            pageSize,
            totalPages);
    }

    /// <summary>
    /// Las etiquetas se buscan en minúsculas porque así se guardan. Comparar
    /// <c>text[]</c> en Postgres distingue mayúsculas, así que sin esto un
    /// filtro por <c>Vegano</c> no devolvería nada y parecería un fallo del
    /// índice.
    /// </summary>
    private static List<string> NormalizarEtiquetas(IReadOnlyCollection<string>? tags) =>
        (tags ?? [])
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static ItemSummary Resumir(Item item) => new(
        item.Id,
        item.Sku,
        item.Name,
        item.Description,
        item.CategoryId,
        item.Tags,
        item.PriceAmount,
        item.PriceCurrency,
        item.AvailabilityMode.ToString(),
        item.AvailableQuantity,
        item.ImageUrl,
        item.IsActive);
}

/// <summary>
/// Lo que order-service copia en la línea de la orden: el precio y los
/// atributos tal como estaban en el momento de comprar.
/// </summary>
public sealed record ItemSnapshot(
    Guid Id,
    string Sku,
    string Name,
    decimal PriceAmount,
    string PriceCurrency,
    string AvailabilityMode,
    int? AvailableQuantity,
    bool IsActive,
    string AttributesJson);

/// <summary>
/// Los items encontrados y los ids que no existen, para que quien pregunta
/// sepa exactamente qué falta sin tener que comparar listas.
/// </summary>
public sealed record GetItemsByIdsResult(IReadOnlyList<ItemSnapshot> Items, IReadOnlyList<Guid> MissingIds);

/// <summary>
/// Devuelve varios items por id, en una sola consulta.
///
/// La llama order-service de forma síncrona mientras crea una orden, con dos
/// objetivos: comprobar que todas las líneas apuntan a items que existen y
/// tomar la foto del precio y los atributos para guardarla en la orden. Esa foto
/// es lo que hace que subir el precio mañana no reescriba lo que el cliente
/// pagó ayer.
///
/// Es síncrona a propósito —y es la única llamada síncrona entre servicios del
/// núcleo— porque una orden no se puede crear «a la espera» de saber si sus
/// items existen. A cambio, order-service la envuelve en Polly con
/// reintentos y cortocircuito.
/// </summary>
public sealed class GetItemsByIdsHandler(IItemRepository repository)
{
    /// <summary>
    /// Tope de ids por llamada. Una orden con más de doscientas líneas distintas
    /// no es un pedido, es un error o un abuso; y sin tope esta ruta sería una
    /// forma cómoda de pedir el catálogo completo de golpe.
    /// </summary>
    public const int MaxIds = 200;

    public async Task<GetItemsByIdsResult> HandleAsync(
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        var ids = itemIds.Where(id => id != Guid.Empty).Distinct().ToList();

        if (ids.Count == 0)
        {
            return new GetItemsByIdsResult([], []);
        }

        if (ids.Count > MaxIds)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["itemIds"] = [$"No se pueden consultar más de {MaxIds} items en una sola llamada."],
            });
        }

        var items = await repository.GetByIdsAsync(ids, cancellationToken).ConfigureAwait(false);

        /*
          Un id que no vuelve puede ser inexistente, borrado o de otro tenant, y
          para quien pregunta las tres cosas son lo mismo: no existe. Distinguirlas
          convertiría esta ruta en una forma de averiguar qué vende el negocio de
          al lado.
        */
        var encontrados = items.Select(item => item.Id).ToHashSet();

        return new GetItemsByIdsResult(
            items.Select(Fotografiar).ToList(),
            ids.Where(id => !encontrados.Contains(id)).ToList());
    }

    private static ItemSnapshot Fotografiar(Item item) => new(
        item.Id,
        item.Sku,
        item.Name,
        item.PriceAmount,
        item.PriceCurrency,
        item.AvailabilityMode.ToString(),
        item.AvailableQuantity,
        item.IsActive,
        item.AttributesJson);
}
