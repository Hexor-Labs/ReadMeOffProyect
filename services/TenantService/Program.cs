using HubNegocios.SharedKernel.Hosting;
using HubNegocios.TenantService.Application.UseCases;
using HubNegocios.TenantService.Domain.Ports;
using HubNegocios.TenantService.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

/*
  Todo lo transversal —logs en JSON, JWT, autorización por defecto, límite de
  peticiones, Swagger, /health y /ready— vive en el SharedKernel. Este archivo
  solo declara lo propio de tenant-service, que es como debe ser: si mañana
  alguien compara este Program.cs con el de order-service, las diferencias que
  vea serán diferencias de verdad y no ruido copiado diez veces.
*/
builder.AddHubDefaults("tenant-service");
builder.AddHubPersistence<TenantDbContext>();

builder.Services.AddScoped<ITenantRepository, TenantRepository>();
builder.Services.AddScoped<CreateTenantHandler>();
builder.Services.AddScoped<UpdateTenantBrandingHandler>();
builder.Services.AddScoped<SuspendTenantHandler>();
builder.Services.AddScoped<GetTenantBySlugOrDomainHandler>();

var app = builder.Build();

app.UseHubDefaults();

await app.RunAsync();

/// <summary>Visible para las pruebas de integración con WebApplicationFactory.</summary>
public partial class Program;
