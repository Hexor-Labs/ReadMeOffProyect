using System.Net;
using System.Text.Json;

using HubNegocios.SharedKernel.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HubNegocios.SharedKernel.Http;

/// <summary>
/// Convierte cualquier excepción en el formato de error del hub.
///
/// Lo importante es lo que NO sale por el cable: de un fallo inesperado, el
/// cliente recibe un código genérico y el identificador de traza, nunca el
/// mensaje de la excepción. Un <c>Npgsql</c> sin filtrar cuenta nombres de
/// tablas y columnas, y a veces datos; es información gratis para quien esté
/// probando el sistema. La traza completa va al log, que es donde debe estar.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    IHostEnvironment environment,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            logger.LogWarning(
                "Regla de negocio incumplida: {Code} — {Message}",
                ex.Code,
                ex.Message);

            await WriteAsync(context, StatusFor(ex), new ApiError(ex.Code, ex.Message, ex.Details, TenantContext.TraceId))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // El cliente se fue. No es un error del servidor y no merece ruido.
            logger.LogDebug("Petición cancelada por el cliente");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fallo no controlado atendiendo {Method} {Path}", context.Request.Method, context.Request.Path);

            var message = environment.IsDevelopment()
                ? ex.Message
                : "Ocurrió un error inesperado. Si vuelve a pasar, danos el trace_id.";

            await WriteAsync(
                context,
                HttpStatusCode.InternalServerError,
                new ApiError("INTERNAL_ERROR", message, null, TenantContext.TraceId)).ConfigureAwait(false);
        }
    }

    private static HttpStatusCode StatusFor(DomainException exception) => exception switch
    {
        NotFoundException => HttpStatusCode.NotFound,
        ConflictException => HttpStatusCode.Conflict,
        ValidationException => HttpStatusCode.UnprocessableEntity,
        _ => HttpStatusCode.BadRequest,
    };

    private static async Task WriteAsync(HttpContext context, HttpStatusCode status, ApiError error)
    {
        if (context.Response.HasStarted)
        {
            // Ya se enviaron cabeceras: reescribir el cuerpo rompería la
            // respuesta a medias. Queda registrado y poco más se puede hacer.
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response
            .WriteAsync(JsonSerializer.Serialize(new ApiErrorResponse(error), Json))
            .ConfigureAwait(false);
    }
}
