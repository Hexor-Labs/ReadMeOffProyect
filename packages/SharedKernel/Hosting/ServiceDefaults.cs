using System.Text;
using System.Threading.RateLimiting;

using HubNegocios.SharedKernel.Auditing;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.SharedKernel.Tenancy;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

using Serilog;
using Serilog.Formatting.Compact;

namespace HubNegocios.SharedKernel.Hosting;

/// <summary>
/// Todo lo que los diez servicios hacen igual, en un solo sitio.
///
/// La checklist del documento de prompts pide comparar los <c>Program.cs</c> de
/// varios servicios y desconfiar si divergen. Esta clase convierte esa
/// revisión manual en algo que no puede fallar: no divergen porque es
/// literalmente el mismo código. Un servicio que necesite algo distinto lo
/// añade después de llamar aquí, y esa diferencia queda a la vista en su
/// <c>Program.cs</c> en vez de escondida entre cien líneas repetidas.
/// </summary>
public static class ServiceDefaults
{
    /// <summary>Logs, autenticación, autorización, límite de peticiones, Swagger y reloj.</summary>
    public static WebApplicationBuilder AddHubDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Host.UseSerilog((context, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", serviceName)
            // JSON, no texto: un log que solo se puede leer con los ojos deja
            // de servir en cuanto hay más de un servicio y más de una réplica.
            .WriteTo.Console(new CompactJsonFormatter()));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddProblemDetails();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.AddHubAuthentication();
        builder.AddHubRateLimiting();

        builder.Services.AddControllers();

        return builder;
    }

    private static void AddHubAuthentication(this WebApplicationBuilder builder)
    {
        var jwt = builder.Configuration.GetSection("Jwt");
        var signingKey = jwt["SigningKey"];

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            // Preferimos no arrancar a arrancar sin validar firmas. Un servicio
            // que acepta cualquier token es peor que un servicio caído.
            throw new InvalidOperationException(
                "Falta Jwt:SigningKey. Viene de variable de entorno o del vault, nunca del código.");
        }

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt["Issuer"],
                    ValidateAudience = true,
                    ValidAudience = jwt["Audience"],
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ValidateLifetime = true,
                    // Por defecto son cinco minutos: demasiado para un token de
                    // acceso que vive quince.
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        builder.Services.AddAuthorizationBuilder()
            // Autenticado por defecto. Lo público se marca con [AllowAnonymous]
            // uno por uno, que es un olvido visible; al revés el olvido es
            // invisible y deja un endpoint abierto.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
    }

    private static void AddHubRateLimiting(this WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Se reparte por tenant cuando lo hay y por IP cuando no. Contar
            // solo por IP castigaría a todo un tenant detrás de una misma
            // oficina; contar solo por tenant dejaría el tráfico anónimo sin
            // freno, que es justo el que conviene frenar.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var partition = context.User.FindFirst("tenant_id")?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "desconocido";

                return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
        });
    }

    /// <summary>
    /// Registra el <see cref="DbContext"/> del servicio con los interceptores de
    /// tenant y auditoría, la outbox y sus comprobaciones de salud.
    /// </summary>
    public static WebApplicationBuilder AddHubPersistence<TContext>(this WebApplicationBuilder builder)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        var connectionString = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Default.");

        builder.Services.AddSingleton<TenantConnectionInterceptor>();
        builder.Services.AddSingleton<TenantAssignmentInterceptor>();
        builder.Services.AddSingleton<AuditInterceptor>();

        builder.Services.AddDbContext<TContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3));

            options.AddInterceptors(
                provider.GetRequiredService<TenantConnectionInterceptor>(),
                provider.GetRequiredService<TenantAssignmentInterceptor>(),
                provider.GetRequiredService<AuditInterceptor>());
        });

        builder.Services.AddScoped<IOutboxWriter>(provider =>
            new OutboxWriter(provider.GetRequiredService<TContext>(), provider.GetRequiredService<TimeProvider>()));

        builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));
        builder.Services.AddSingleton<IEventBusPublisher, LoggingEventBusPublisher>();
        builder.Services.AddHostedService<OutboxPublisherService<TContext>>();

        builder.Services
            .AddHealthChecks()
            // Solo en /ready: si Postgres se cae, el orquestador debe dejar de
            // mandarnos tráfico, no reiniciar el contenedor una y otra vez.
            .AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);

        return builder;
    }

    /// <summary>Encadena el middleware en el orden correcto. El orden aquí no es opinable.</summary>
    public static WebApplication UseHubDefaults(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseSerilogRequestLogging();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseRateLimiter();

        app.UseAuthentication();
        // Después de autenticar: antes, leería claims de un token sin validar.
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthorization();

        app.MapControllers();

        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
            .AllowAnonymous();

        return app;
    }
}
