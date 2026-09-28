using HubNegocios.ReservationsService.Domain.Entities;
using HubNegocios.ReservationsService.Domain.Ports;
using HubNegocios.SharedKernel.Http;

using Microsoft.Extensions.Logging;

namespace HubNegocios.ReservationsService.Application.UseCases;

/// <summary>
/// Alta masiva de franjas: «todos los martes de 20:00 a 23:00 durante tres
/// meses, en tramos de una hora y aforo de cuatro».
///
/// Las horas son UTC. El servicio no convierte husos: quien configura el
/// horario —el panel del negocio— ya sabe en qué zona trabaja y manda UTC. Si
/// esta capa intentara adivinarlo, el mismo horario significaría cosas
/// distintas según quién lo guardó.
/// </summary>
public sealed record CreateTimeSlotsCommand(
    Guid ItemId,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyCollection<DayOfWeek> Weekdays,
    TimeOnly StartTime,
    TimeOnly EndTime,
    TimeSpan SlotDuration,
    int Capacity);

public sealed record CreateTimeSlotsResult(Guid ItemId, int Created, DateTime? FirstSlotStart, DateTime? LastSlotEnd);

/// <summary>
/// Genera de una vez todas las franjas de un horario recurrente.
///
/// Existe porque la alternativa —crearlas de una en una desde el panel— lleva a
/// que nadie configure el calendario más allá de la semana que viene, y una
/// franja que no existe no se puede reservar.
/// </summary>
public sealed class CreateTimeSlotsHandler(
    IReservationRepository repository,
    ILogger<CreateTimeSlotsHandler> logger)
{
    /// <summary>
    /// Tope de franjas por llamada.
    ///
    /// No es una limitación del negocio, es un freno: una duración de franja de
    /// un minuto sobre un año entero son medio millón de filas, y un cero de más
    /// en un formulario no debería poder tumbar la base de datos.
    /// </summary>
    public const int MaxSlotsPerCall = 5_000;

    public async Task<CreateTimeSlotsResult> HandleAsync(
        CreateTimeSlotsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Validate(command);

        var slots = Build(command);

        if (slots.Count == 0)
        {
            // Rango válido pero sin ningún día que encaje: por ejemplo «los
            // martes» en un rango de miércoles a viernes. Es un error de quien
            // llama, no un resultado vacío legítimo.
            throw new DomainException(
                "RESERVATION_SLOTS_EMPTY",
                "El rango indicado no contiene ninguno de los días de la semana seleccionados.");
        }

        repository.AddTimeSlots(slots);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Creadas {Count} franjas para el recurso {ItemId} entre {From} y {To}",
            slots.Count,
            command.ItemId,
            command.FromDate,
            command.ToDate);

        return new CreateTimeSlotsResult(
            command.ItemId,
            slots.Count,
            slots[0].SlotStart,
            slots[^1].SlotEnd);
    }

    private static void Validate(CreateTimeSlotsCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        if (command.ToDate < command.FromDate)
        {
            errors[nameof(command.ToDate)] = ["La fecha final no puede ser anterior a la inicial."];
        }

        if (command.Weekdays.Count == 0)
        {
            errors[nameof(command.Weekdays)] = ["Hay que indicar al menos un día de la semana."];
        }

        if (command.EndTime <= command.StartTime)
        {
            errors[nameof(command.EndTime)] = ["La hora final tiene que ser posterior a la inicial."];
        }

        if (command.SlotDuration <= TimeSpan.Zero)
        {
            errors[nameof(command.SlotDuration)] = ["La duración de la franja tiene que ser positiva."];
        }

        if (command.Capacity <= 0)
        {
            errors[nameof(command.Capacity)] = ["El aforo tiene que ser mayor que cero."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        /*
          El tope se comprueba con una estimación y no generando la lista: el
          objetivo es no construir en memoria el medio millón de objetos que
          justamente se quiere evitar.
        */
        var days = (command.ToDate.DayNumber - command.FromDate.DayNumber) + 1;
        var perDay = (long)((command.EndTime - command.StartTime) / command.SlotDuration);
        var estimated = perDay * days;

        if (estimated > MaxSlotsPerCall)
        {
            throw new DomainException(
                "RESERVATION_SLOTS_TOO_MANY",
                $"La configuración generaría del orden de {estimated} franjas y el máximo por llamada es {MaxSlotsPerCall}. " +
                "Parte el rango o alarga la duración de cada franja.");
        }
    }

    private static List<TimeSlot> Build(CreateTimeSlotsCommand command)
    {
        var slots = new List<TimeSlot>();

        for (var date = command.FromDate; date <= command.ToDate; date = date.AddDays(1))
        {
            if (!command.Weekdays.Contains(date.DayOfWeek))
            {
                continue;
            }

            for (var start = command.StartTime; start < command.EndTime; start = start.Add(command.SlotDuration))
            {
                var end = start.Add(command.SlotDuration);

                /*
                  Una franja que se sale de la hora de cierre no se recorta: se
                  descarta. Recortarla dejaría en el calendario tramos de diez
                  minutos que nadie configuró y que el cliente vería como
                  huecos reservables.

                  La comparación con <= cubre además el cruce de medianoche: al
                  pasar de 23:30 a 00:30, TimeOnly.Add da la vuelta y end queda
                  por debajo de start.
                */
                if (end <= start || end > command.EndTime)
                {
                    break;
                }

                slots.Add(TimeSlot.Open(
                    Guid.NewGuid(),
                    command.ItemId,
                    // Kind UTC explícito: Npgsql rechaza un DateTime sin zona
                    // al escribir en una columna timestamptz.
                    DateTime.SpecifyKind(date.ToDateTime(start), DateTimeKind.Utc),
                    DateTime.SpecifyKind(date.ToDateTime(end), DateTimeKind.Utc),
                    command.Capacity));
            }
        }

        return slots;
    }
}
