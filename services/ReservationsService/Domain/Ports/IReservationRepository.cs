using HubNegocios.ReservationsService.Domain.Entities;

namespace HubNegocios.ReservationsService.Domain.Ports;

/// <summary>
/// Una transacción de base de datos vista desde el dominio.
///
/// El puerto existe para que <c>ReserveSlotHandler</c> —donde vive la regla de
/// «no vender dos veces la misma mesa»— pueda abrir y confirmar una transacción
/// sin conocer EF Core, y para que las pruebas puedan comprobar que la abrió de
/// verdad. Es deliberadamente mínimo: confirmar, deshacer y cerrar.
///
/// Cerrar sin confirmar deshace. Eso es lo que hace que un <c>await using</c>
/// alrededor del caso de uso sea suficiente: cualquier excepción sale con la
/// transacción abortada, no a medias.
/// </summary>
public interface ITransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Puerto de salida hacia el almacenamiento de reservas.
///
/// Vive en el dominio y lo implementa la infraestructura: por eso los casos de
/// uso —incluido el crítico— se prueban sin Postgres y sin simular EF Core.
///
/// Los dos métodos <c>…ForUpdateAsync</c> son la excepción interesante: piden
/// explícitamente un bloqueo de fila. El nombre lo dice porque no es un detalle
/// del adaptador, es una decisión del caso de uso: quien llama necesita saber
/// que está serializando accesos, y por qué.
/// </summary>
public interface IReservationRepository
{
    /// <summary>Abre una transacción explícita. Solo hace falta cuando hay bloqueo o dos escrituras.</summary>
    Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lee la franja bloqueando su fila hasta el final de la transacción
    /// (<c>SELECT … FOR UPDATE</c>).
    ///
    /// Es la pieza que impide la sobreventa: dos peticiones que pidan la misma
    /// franja a la vez se ponen en fila aquí, y la segunda no ve el cupo hasta
    /// que la primera ha terminado de descontarlo.
    /// </summary>
    Task<TimeSlot?> LockTimeSlotForUpdateAsync(
        Guid timeSlotId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lee la reserva bloqueando su fila.
    ///
    /// Cancelar y registrar la llegada compiten por la misma fila: sin bloqueo,
    /// una cancelación y un check-in simultáneos se pisan y puede quedar una
    /// reserva cancelada con el cliente ya sentado, o cupo devuelto dos veces.
    /// </summary>
    Task<Reservation?> LockReservationForUpdateAsync(
        Guid reservationId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Lectura sin bloqueo, para la comprobación de idempotencia del consumidor.</summary>
    Task<Reservation?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<bool> ConfirmationCodeExistsAsync(string confirmationCode, CancellationToken cancellationToken = default);

    void AddTimeSlots(IEnumerable<TimeSlot> slots);

    void AddReservation(Reservation reservation);

    /// <summary>
    /// Confirma los cambios pendientes.
    ///
    /// No hay <c>Update</c>: lo que sale del repositorio viene rastreado, así
    /// que basta con modificarlo y guardar.
    ///
    /// El adaptador traduce las violaciones de índice único a
    /// <c>ConflictException</c> con código de negocio. Es importante: así el
    /// consumidor de eventos puede distinguir «esta orden ya estaba reservada»
    /// de un fallo cualquiera sin conocer los códigos de error de PostgreSQL.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
