using System.Linq.Expressions;

using HubNegocios.CatalogService.Domain.ValueObjects;
using HubNegocios.SharedKernel.Auditing;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.CatalogService.Domain.Entities;

/// <summary>
/// Cómo se decide si un item está disponible.
///
/// El catálogo sirve a verticales muy distintas y cada una cuenta la
/// disponibilidad a su manera: un plato de carta no se agota (<see cref="Unlimited"/>),
/// una camiseta sí (<see cref="StockBased"/>), una clase de yoga tiene plazas
/// por franja (<see cref="SlotBased"/>) y una entrada se acaba por orden de
/// llegada (<see cref="FirstCome"/>). Meter las cuatro reglas en un solo campo
/// booleano «hay stock» obligaría a cada consumidor a adivinar cuál de los
/// cuatro casos tiene delante.
/// </summary>
public enum AvailabilityMode
{
    /// <summary>Sin límite: no se lleva cuenta de unidades.</summary>
    Unlimited,

    /// <summary>Unidades físicas en almacén.</summary>
    StockBased,

    /// <summary>Plazas de una franja horaria.</summary>
    SlotBased,

    /// <summary>Cupo que se agota por orden de llegada.</summary>
    FirstCome,
}

/// <summary>
/// Un producto o servicio del catálogo de un negocio.
///
/// Es la entidad que más leen los demás: order-service pregunta por ella antes
/// de crear cada orden y la página pública la busca en cada visita. De ahí dos
/// rasgos suyos: nunca se borra de verdad (<see cref="IsDeleted"/>), porque hay
/// órdenes pasadas que la referencian, y todo cambio de precio deja rastro en
/// <see cref="ItemPriceHistory"/>.
///
/// El dominio es puro: no conoce EF Core ni ASP.NET. Las propiedades tienen
/// <c>private set</c> y se cambian por métodos con nombre, para que un estado
/// imposible —modo ilimitado con cantidad, precio negativo— no se pueda
/// construir.
/// </summary>
public sealed class Item : ITenantOwned, IAuditable
{
    private const int MaxTags = 20;

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>
    /// Categoría del negocio. Es un <c>Guid</c> suelto y no una relación: las
    /// categorías las administra el propio negocio y todavía no tienen tabla en
    /// este servicio; una clave ajena a algo que no existe solo estorba.
    /// </summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>
    /// Etiquetas libres: <c>vegano</c>, <c>sin-gluten</c>, <c>novedad</c>.
    /// Columna <c>text[]</c> nativa de Postgres, no una tabla aparte ni una
    /// cadena separada por comas: así el filtro puede apoyarse en un índice GIN.
    /// </summary>
    public string[] Tags { get; private set; } = [];

    public decimal PriceAmount { get; private set; }

    public string PriceCurrency { get; private set; } = string.Empty;

    public AvailabilityMode AvailabilityMode { get; private set; } = AvailabilityMode.Unlimited;

    /// <summary>
    /// Unidades o plazas libres. Nulo —y solo nulo— cuando el modo es
    /// <see cref="AvailabilityMode.Unlimited"/>: un 0 en ese caso se leería
    /// como «agotado», que es justo lo contrario de lo que significa.
    /// </summary>
    public int? AvailableQuantity { get; private set; }

    /// <summary>Imagen principal, la que se usa en listados y miniaturas.</summary>
    public string? ImageUrl { get; private set; }

    /// <summary>Galería. <c>text[]</c> nativo, igual que las etiquetas.</summary>
    public string[] ImageUrls { get; private set; } = [];

    /// <summary>
    /// Atributos propios de cada vertical: tallas, alérgenos, potencia del
    /// motor. Columna <c>jsonb</c>, de forma libre por diseño, porque el día
    /// que entre una vertical nueva no queremos una migración por atributo.
    /// </summary>
    public string AttributesJson { get; private set; } = "{}";

    /// <summary>Visible y vendible. Distinto de borrado: se apaga y se enciende.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Borrado lógico. Nunca se ejecuta un DELETE sobre esta tabla: hay órdenes
    /// históricas que apuntan a items que el negocio ya no vende, y borrar la
    /// fila convertiría esas órdenes en un recibo sin producto.
    /// </summary>
    public bool IsDeleted { get; private set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    /// <summary>
    /// El ÚNICO criterio de visibilidad de un item: que sea del tenant en curso
    /// y que no esté borrado.
    ///
    /// Vive aquí, escrito una sola vez, porque EF Core admite un único filtro
    /// global por entidad: el <c>HasQueryFilter</c> del contexto usa esta misma
    /// expresión, y los dobles de test del repositorio la reutilizan. Si mañana
    /// alguien añade una condición, la añade en un sitio y se aplica en todos.
    ///
    /// Compara contra <c>TenantContext.Current</c>, que es un miembro ESTÁTICO.
    /// Eso no es casualidad: EF Core cachea el modelo por tipo de contexto, así
    /// que un filtro que capturase la instancia del DbContext seguiría
    /// apuntando a la de la primera petición y filtraría por el tenant
    /// equivocado a partir de la segunda.
    /// </summary>
    public static Expression<Func<Item, bool>> Visible { get; } =
        item => item.TenantId == TenantContext.Current.TenantId && !item.IsDeleted;

    private Item()
    {
    }

    public static Item Create(
        Guid id,
        Sku sku,
        string name,
        string? description,
        Guid? categoryId,
        IEnumerable<string>? tags,
        Money price,
        AvailabilityMode availabilityMode,
        int? availableQuantity,
        string? imageUrl,
        IEnumerable<string>? imageUrls,
        string? attributesJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var item = new Item
        {
            Id = id,
            Sku = sku.Value,
            Name = name.Trim(),
            Description = Limpiar(description),
            CategoryId = categoryId,
            Tags = NormalizarEtiquetas(tags),
            PriceAmount = price.Amount,
            PriceCurrency = price.Currency,
            ImageUrl = Limpiar(imageUrl),
            ImageUrls = NormalizarUrls(imageUrls),
            AttributesJson = NormalizarJson(attributesJson),
            IsActive = true,
            IsDeleted = false,
        };

        item.ChangeAvailability(availabilityMode, availableQuantity);

        return item;
    }

    /// <summary>
    /// Cambia el precio y devuelve el anterior, para que el caso de uso pueda
    /// registrarlo en el historial sin tener que leerlo antes de tocar nada.
    /// </summary>
    public decimal ChangePrice(Money nuevoPrecio)
    {
        EnsureUsable();

        if (!string.Equals(nuevoPrecio.Currency, PriceCurrency, StringComparison.Ordinal))
        {
            /*
              Cambiar de moneda no es cambiar de precio: dejaría el historial
              como una lista de números incomparables y descuadraría cualquier
              informe que sume importes. Si algún día hace falta, será su propio
              caso de uso, con su tipo de cambio.
            */
            throw new ConflictException(
                "ITEM_CURRENCY_MISMATCH",
                $"El item se vende en {PriceCurrency} y el nuevo precio viene en {nuevoPrecio.Currency}.");
        }

        var anterior = PriceAmount;
        PriceAmount = nuevoPrecio.Amount;
        return anterior;
    }

    /// <summary>Cambia el modo de disponibilidad y las unidades libres.</summary>
    public void ChangeAvailability(AvailabilityMode mode, int? availableQuantity)
    {
        EnsureUsable();

        if (mode == AvailabilityMode.Unlimited)
        {
            // Se ignora la cantidad en vez de rechazarla: quien pasa de stock a
            // ilimitado manda la cantidad que ya tenía, y en esa petición no
            // hay nada que corregir.
            AvailabilityMode = mode;
            AvailableQuantity = null;
            return;
        }

        if (availableQuantity is null or < 0)
        {
            throw new DomainException(
                "ITEM_QUANTITY_REQUIRED",
                $"El modo {mode} necesita una cantidad disponible de cero o más.");
        }

        AvailabilityMode = mode;
        AvailableQuantity = availableQuantity;
    }

    /// <summary>
    /// Marca el item como borrado. No hay método para borrarlo de verdad, ni
    /// aquí ni en el repositorio.
    /// </summary>
    public void SoftDelete()
    {
        IsDeleted = true;

        // Se apaga además de marcarse: mientras el borrado llega por evento a
        // los read models de otros servicios, un item apagado ya no se ofrece.
        IsActive = false;
    }

    private void EnsureUsable()
    {
        if (IsDeleted)
        {
            throw new ConflictException("ITEM_DELETED", "El item está borrado.");
        }
    }

    private static string? Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static string NormalizarJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? "{}" : json.Trim();

    /// <summary>
    /// Etiquetas en minúsculas, sin repetidas y sin huecos.
    ///
    /// Lo de las minúsculas no es estética: comparar <c>text[]</c> en Postgres
    /// distingue mayúsculas, así que sin normalizar, buscar <c>vegano</c> no
    /// encontraría los items etiquetados <c>Vegano</c> y el filtro parecería
    /// roto sin estarlo.
    /// </summary>
    private static string[] NormalizarEtiquetas(IEnumerable<string>? tags) =>
        (tags ?? [])
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Take(MaxTags)
            .ToArray();

    private static string[] NormalizarUrls(IEnumerable<string>? urls) =>
        (urls ?? [])
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
