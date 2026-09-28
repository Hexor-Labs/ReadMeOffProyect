using System.ComponentModel.DataAnnotations;

using HubNegocios.ReservationsService.Application.UseCases;
using HubNegocios.SharedKernel.Http;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HubNegocios.ReservationsService.Infrastructure.Http;

/// <summary>
/// Cuerpo del alta masiva de franjas.
///
/// Es un DTO y no el comando del caso de uso ni la entidad. La diferencia no es
/// ceremonia: si el endpoint aceptara un <c>TimeSlot</c>, cualquiera podría
/// mandar <c>"availableCapacity": 999</c> en el JSON y el binder lo asignaría
/// sin preguntar. Aquí solo entra lo que el negocio tiene derecho a decidir.
///
/// Los días de la semana entran como números (0 = domingo, igual que
/// <see cref="DayOfWeek"/>) porque es lo que un formulario manda sin depender de
/// cómo se llamen los valores del enum en cada idioma.
/// </summary>
public sealed record CreateTimeSlotsRequest(
    [property: Required] Guid ItemId,
    [property: Required] DateOnly FromDate,
    [property: Required] DateOnly ToDate,
    [property: Required, MinLength(1)] IReadOnlyList<int> Weekdays,
    [property: Required] TimeOnly StartTime,
    [property: Required] TimeOnly EndTime,
    [property: Range(5, 1440)] int SlotDurationMinutes,
    [property: Range(1, 10_000)] int Capacity);

public sealed record CancelReservationRequest([property: StringLength(500)] string? Reason);

/// <summary>
/// Operaciones de sala sobre franjas y reservas.
///
/// No hay endpoint para crear una reserva: en este hub una reserva es la
/// consecuencia de una orden, y entra por el evento <c>order.created</c>. Abrir
/// una puerta directa permitiría reservas sin orden asociada, es decir sin cobro
/// y sin rastro en order-service.
/// </summary>
[ApiController]
[Route("api/reservations")]
public sealed class ReservationsController(
    CreateTimeSlotsHandler createTimeSlots,
    CheckInReservationHandler checkIn,
    CancelReservationHandler cancel) : ControllerBase
{
    /// <summary>Configura de una vez el horario de un recurso reservable.</summary>
    [HttpPost("time-slots")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType<CreateTimeSlotsResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateTimeSlotsAsync(
        [FromBody] CreateTimeSlotsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await createTimeSlots.HandleAsync(
            new CreateTimeSlotsCommand(
                request.ItemId,
                request.FromDate,
                request.ToDate,
                ToWeekdays(request.Weekdays),
                request.StartTime,
                request.EndTime,
                TimeSpan.FromMinutes(request.SlotDurationMinutes),
                request.Capacity),
            cancellationToken).ConfigureAwait(false);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Registra la llegada del cliente. La usa el personal de sala.</summary>
    [HttpPost("{reservationId:guid}/check-in")]
    [Authorize(Roles = "Owner,Manager,Staff")]
    [ProducesResponseType<CheckInReservationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CheckInAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        var result = await checkIn
            .HandleAsync(new CheckInReservationCommand(reservationId), cancellationToken)
            .ConfigureAwait(false);

        return Ok(result);
    }

    /// <summary>Cancela la reserva y devuelve sus plazas al cupo de la franja.</summary>
    [HttpPost("{reservationId:guid}/cancel")]
    [Authorize(Roles = "Owner,Manager,Staff")]
    [ProducesResponseType<CancelReservationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelAsync(
        Guid reservationId,
        [FromBody] CancelReservationRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await cancel
            .HandleAsync(new CancelReservationCommand(reservationId, request?.Reason), cancellationToken)
            .ConfigureAwait(false);

        return Ok(result);
    }

    /// <summary>
    /// Convierte los números del formulario en días de la semana.
    ///
    /// Se valida en el borde y no en el caso de uso porque un 9 aquí no es una
    /// regla de negocio incumplida, es una petición mal formada; y porque un
    /// <c>(DayOfWeek)9</c> colado hasta el dominio no generaría ninguna franja y
    /// el error saldría luego, disfrazado de «rango sin días válidos».
    /// </summary>
    private static List<DayOfWeek> ToWeekdays(IReadOnlyList<int> weekdays)
    {
        var invalid = weekdays.Where(day => day is < 0 or > 6).ToArray();

        if (invalid.Length > 0)
        {
            // Nombre completo: DataAnnotations trae otra ValidationException y
            // la de aquí es la del hub, la que el middleware convierte en 422.
            throw new HubNegocios.SharedKernel.Http.ValidationException(new Dictionary<string, string[]>
            {
                [nameof(CreateTimeSlotsRequest.Weekdays)] =
                    [$"Los días de la semana van de 0 (domingo) a 6 (sábado). Recibidos: {string.Join(", ", invalid)}."],
            });
        }

        return weekdays.Select(day => (DayOfWeek)day).Distinct().ToList();
    }
}
