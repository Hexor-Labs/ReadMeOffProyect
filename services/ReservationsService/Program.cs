using HubNegocios.ReservationsService.Application.UseCases;
using HubNegocios.ReservationsService.Domain.Ports;
using HubNegocios.ReservationsService.Infrastructure.Messaging;
using HubNegocios.ReservationsService.Infrastructure.Persistence;
using HubNegocios.SharedKernel.Hosting;

var builder = WebApplication.CreateBuilder(args);

/*
  Todo lo transversal —logs en JSON, JWT, autorización por defecto, límite de
  peticiones, Swagger, /health y /ready— vive en el SharedKernel. Este archivo
  solo declara lo propio de reservations-service, que es como debe ser: si
  mañana alguien compara este Program.cs con el de tenant-service, las
  diferencias que vea serán diferencias de verdad y no ruido copiado diez veces.
*/
builder.AddHubDefaults("reservations-service");
builder.AddHubPersistence<ReservationsDbContext>();

builder.Services.AddScoped<IReservationRepository, ReservationRepository>();
builder.Services.AddScoped<CreateTimeSlotsHandler>();
builder.Services.AddScoped<ReserveSlotHandler>();
builder.Services.AddScoped<CheckInReservationHandler>();
builder.Services.AddScoped<CancelReservationHandler>();
builder.Services.AddScoped<ConsumeOrderCreatedEventHandler>();

// Adaptador de entrada del bus. Mientras no haya broker real no hay nada que lo
// invoque en producción; se registra igual para que exista un solo sitio donde
// engancharlo el día que lo haya.
builder.Services.AddScoped<OrderCreatedConsumer>();

var app = builder.Build();

app.UseHubDefaults();

await app.RunAsync();

/// <summary>Visible para las pruebas de integración con WebApplicationFactory.</summary>
public partial class Program;
