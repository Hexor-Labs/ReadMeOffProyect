using HubNegocios.IdentityService.Application.UseCases;
using HubNegocios.IdentityService.Domain.Ports;
using HubNegocios.IdentityService.Infrastructure.Persistence;
using HubNegocios.IdentityService.Infrastructure.Security;
using HubNegocios.SharedKernel.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddHubDefaults("identity-service");
builder.AddHubPersistence<IdentityDbContext>();

// Adaptadores de seguridad. Singleton porque no guardan estado por petición y
// el hasher precalcula su hash de descarte una sola vez al arrancar.
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();

builder.Services.AddScoped<IIdentityRepository, IdentityRepository>();
builder.Services.AddScoped<RegisterUserHandler>();
builder.Services.AddScoped<LoginHandler>();
builder.Services.AddScoped<RefreshTokenHandler>();
builder.Services.AddScoped<LogoutHandler>();
builder.Services.AddScoped<AssignRoleHandler>();
builder.Services.AddScoped<CheckPermissionHandler>();

var app = builder.Build();

app.UseHubDefaults();

await app.RunAsync();

/// <summary>Visible para las pruebas de integración con WebApplicationFactory.</summary>
public partial class Program;
