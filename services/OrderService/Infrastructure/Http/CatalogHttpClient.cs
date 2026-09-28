using System.Net.Http.Json;

using HubNegocios.OrderService.Domain.Ports;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace HubNegocios.OrderService.Infrastructure.Http;

/// <summary>
/// Adaptador hacia catalog-service.
///
/// Los reintentos y el cortacircuitos NO están aquí: se configuran con Polly al
/// registrar el <c>HttpClient</c> (ver <c>Program.cs</c>). Así esta clase se
/// ocupa solo de traducir entre HTTP y el dominio, y la política de resiliencia
/// se cambia sin tocar código de negocio.
/// </summary>
public sealed class CatalogHttpClient(HttpClient httpClient, ILogger<CatalogHttpClient> logger) : ICatalogClient
{
    public async Task<IReadOnlyList<CatalogItemSnapshot>> GetItemsAsync(
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        if (itemIds.Count == 0)
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/items/by-ids")
        {
            Content = JsonContent.Create(new { itemIds }),
        };

        /*
          La correlación viaja al otro servicio. Es lo que permite seguir una
          operación completa entre microservicios cuando algo falla: sin esto,
          el log de catalog-service y el de order-service son dos historias
          separadas que nadie puede juntar.
        */
        request.Headers.TryAddWithoutValidation(
            TenantResolutionMiddleware.CorrelationHeader,
            TenantContext.Current.CorrelationId.ToString());

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var items = await response.Content
                .ReadFromJsonAsync<List<CatalogItemSnapshot>>(cancellationToken)
                .ConfigureAwait(false);

            return items ?? [];
        }
        catch (HttpRequestException ex)
        {
            /*
              Aquí llega también el cortacircuitos abierto: Polly lo convierte en
              una excepción. Se traduce a un error de negocio con código propio
              para que el frontend pueda decir «el catálogo no responde, inténtalo
              en un momento» en vez de un 500 genérico — y para que quede claro
              que no es culpa de la orden que el cliente intentaba crear.
            */
            logger.LogError(ex, "catalog-service no respondió al validar las líneas de la orden");

            throw new DomainException(
                "CATALOG_UNAVAILABLE",
                "No pudimos comprobar los productos en este momento. Inténtalo de nuevo en unos segundos.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Se agotó el tiempo de espera, no lo canceló el cliente.
            logger.LogError(ex, "catalog-service agotó el tiempo de espera");

            throw new DomainException(
                "CATALOG_TIMEOUT",
                "El catálogo tardó demasiado en responder. Inténtalo de nuevo.");
        }
    }
}
