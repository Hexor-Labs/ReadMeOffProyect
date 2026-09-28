namespace HubNegocios.ReservationsService.Domain.Ports;

/// <summary>
/// Los dos códigos de error que cruzan la frontera entre el adaptador de
/// persistencia y la aplicación.
///
/// El resto de códigos viven donde se lanzan, pero estos dos no pueden: los
/// produce el repositorio al traducir una violación de índice único y los
/// interpreta el consumidor de eventos para decidir si una orden ya estaba
/// reservada. Escritos como cadenas sueltas en dos archivos, el día que alguien
/// corrija uno el otro deja de reconocerlo y el consumidor pierde la
/// idempotencia sin que se rompa ninguna prueba de compilación.
/// </summary>
public static class ReservationErrorCodes
{
    /// <summary>Choca el índice único de <c>order_id</c>: esa orden ya tiene reserva.</summary>
    public const string OrderAlreadyReserved = "RESERVATION_ORDER_ALREADY_RESERVED";

    /// <summary>Choca el índice único del código de confirmación dentro del tenant.</summary>
    public const string ConfirmationCodeTaken = "RESERVATION_CODE_TAKEN";
}
