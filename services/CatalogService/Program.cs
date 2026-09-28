using HubNegocios.CatalogService.Application.UseCases;
using HubNegocios.CatalogService.Domain.Ports;
using HubNegocios.CatalogService.Infrastructure.Persistence;
using HubNegocios.SharedKernel.Hosting;

var builder = WebApplication.CreateBuilder(args);

/*
  Todo lo transversal —logs en JSON, JWT, autorización por defecto, límite de
  peticiones, Swagger, /health y /ready— vive en el SharedKernel. Este archivo
  solo declara lo propio de catalog-service, que es como debe ser: si mañana
  alguien compara este Program.cs con el de tenant-service, las diferencias que
  vea serán diferencias de verdad y no ruido copiado diez veces.
*/
builder.AddHubDefaults("catalog-service");
builder.AddHubPersistence<CatalogDbContext>();

builder.Services.AddScoped<IItemRepository, ItemRepository>();
builder.Services.AddScoped<CreateItemHandler>();
builder.Services.AddScoped<UpdateItemPriceHandler>();
builder.Services.AddScoped<UpdateItemAvailabilityHandler>();
builder.Services.AddScoped<SoftDeleteItemHandler>();
builder.Services.AddScoped<SearchItemsHandler>();
builder.Services.AddScoped<GetItemsByIdsHandler>();

var app = builder.Build();

app.UseHubDefaults();

await app.RunAsync();

/// <summary>Visible para las pruebas de integración con WebApplicationFactory.</summary>
public partial class Program;
