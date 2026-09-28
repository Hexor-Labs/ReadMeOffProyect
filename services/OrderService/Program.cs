using HubNegocios.OrderService.Application.UseCases;
using HubNegocios.OrderService.Domain.Ports;
using HubNegocios.OrderService.Infrastructure.Http;
using HubNegocios.OrderService.Infrastructure.Persistence;
using HubNegocios.SharedKernel.Hosting;

using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

builder.AddHubDefaults("order-service");
builder.AddHubPersistence<OrderDbContext>();

builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<CreateOrderHandler>();
builder.Services.AddScoped<CancelOrderHandler>();
builder.Services.AddScoped<GetOrderHistoryHandler>();

/*
  Llamada síncrona a catalog-service, con red de seguridad.

  Tres piezas que resuelven tres problemas distintos y que suelen confundirse:

  - El TIEMPO DE ESPERA (3 s) impide que una petición nuestra se quede colgada
    esperando a un servicio que no va a contestar. Sin él, los hilos se acumulan
    y acabamos cayendo nosotros por culpa de otro.

  - Los REINTENTOS con espera creciente cubren el fallo pasajero: un despliegue
    en marcha, un pico de red. La espera crece (1 s, 2 s, 4 s) porque reintentar
    de inmediato contra un servicio saturado es echar gasolina al fuego.

  - El CORTACIRCUITOS cubre el fallo sostenido. Tras cinco fallos seguidos deja
    de intentarlo durante treinta segundos y responde al instante. Esto protege
    a las dos partes: a catalog-service, que deja de recibir tráfico mientras se
    recupera, y a nosotros, que dejamos de gastar tres segundos por petición en
    algo que ya sabemos que va a fallar.

  Sin el cortacircuitos, los reintentos EMPEORAN una caída: multiplican por tres
  la carga justo cuando el otro servicio menos la aguanta.
*/
var catalogSection = builder.Configuration.GetSection("Services:Catalog");
var catalogTimeout = TimeSpan.FromSeconds(catalogSection.GetValue("TimeoutSeconds", 3));

builder.Services
    .AddHttpClient<ICatalogClient, CatalogHttpClient>(client =>
    {
        client.BaseAddress = new Uri(catalogSection["BaseUrl"]
            ?? throw new InvalidOperationException("Falta Services:Catalog:BaseUrl."));
        client.Timeout = catalogTimeout;
    })
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(3, intento => TimeSpan.FromSeconds(Math.Pow(2, intento - 1))))
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));

var app = builder.Build();

app.UseHubDefaults();

await app.RunAsync();

/// <summary>Visible para las pruebas de integración con WebApplicationFactory.</summary>
public partial class Program;
