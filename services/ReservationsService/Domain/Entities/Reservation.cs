using HubNegocios.ReservationsService.Domain.ValueObjects;
using HubNegocios.SharedKernel.Auditing;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.ReservationsService.Domain.Entities;

public enum ReservationStatus
{
    Confirmed,
    CheckedIn,
    NoShow,
    Cancelled,
}

/// <summary>
/// La reserva de un cliente sobre una franja concreta.
///
/// Nace confirmada: en este hub una reserva es la consecuencia de una orden que
/// ya existe, así que no hay estado «pendiente» que mantener. De ahí en
/// adelante solo puede pasar una de tres cosas —se presenta, no se presenta, o
/// se cancela— y el orden importa: <see cref="CheckIn"/> sobre una reserva
/// cancelada tiene que fallar, no corregir el estado en silencio.
///
/// <see cref="OrderId"/> es único en la tabla, y eso es lo que hace idempotente
/// al consumidor de <c>order.created</c>. Ver
/// <c>ConsumeOrderCreatedEventHandler</c>.
/// </summary>
public sealed class Reservation : ITenantOwned, IAuditable
{
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>Orden que originó la reserva. Único: una orden, una reserva.</summary>
    public Guid OrderId { get; private set; }

    public Guid TimeSlotId { get; private set; }

    public int PartySize { get; private set; }

    public string CustomerName { get; private set; } = string.Empty;
    public string? CustomerPhone { get; private set; }
    public string? CustomerEmail { get; private set; }

    /// <summary>Alergias, silla de bebé, mesa en la terraza…</summary>
    public string? SpecialRequests { get; private set; }

    public ReservationStatus Status { get; private set; } = ReservationStatus.Confirmed;

    /// <summary>El código que el cliente presenta al llegar. Único por tenant.</summary>
    public string ConfirmationCode { get; private set; } = string.Empty;

    public DateTime? ConfirmationSentAt { get; private set; }
    public DateTime? ReminderSentAt { get; private set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    private Reservation()
    {
    }

    public static Reservation Confirm(
        Guid id,
        Guid tenantId,
        Guid orderId,
        Guid timeSlotId,
        int partySize,
        string customerName,
        string? customerPhone,
        string? customerEmail,
        string? specialRequests,
        ConfirmationCode confirmationCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerName);

        if (partySize <= 0)
        {
            throw new DomainException(
                "RESERVATION_PARTY_SIZE_INVALID",
                "El número de comensales tiene que ser mayor que cero.");
        }

        return new Reservation
        {
            Id = id,
            TenantId = tenantId,
            OrderId = orderId,
            TimeSlotId = timeSlotId,
            PartySize = partySize,
            CustomerName = customerName.Trim(),
            CustomerPhone = Normalize(customerPhone),
            CustomerEmail = Normalize(customerEmail)?.ToLowerInvariant(),
            SpecialRequests = Normalize(specialRequests),
            Status = ReservationStatus.Confirmed,
            ConfirmationCode = confirmationCode.Value,
        };
    }

    /// <summary>Registra la llegada del cliente.</summary>
    public void CheckIn()
    {
        if (Status == ReservationStatus.CheckedIn)
        {
            // Idempotente: marcar dos veces la llegada de quien ya está sentado
            // no es un error, y tratarlo como tal obliga al personal de sala a
            // consultar el estado antes de cada pulsación.
            return;
        }

        if (Status != ReservationStatus.Confirmed)
        {
            throw new ConflictException(
                "RESERVATION_NOT_CHECKABLE",
                $"Una reserva en estado {Status} no admite registro de llegada.");
        }

        Status = ReservationStatus.CheckedIn;
    }

    /// <summary>
    /// Cancela la reserva. Devuelve las plazas que hay que reponer en la franja,
    /// para que el caso de uso no tenga que deducirlo.
    /// </summary>
    public int Cancel()
    {
        if (Status == ReservationStatus.Cancelled)
        {
            /*
              Aquí sí falla, al contrario que CheckIn. Cancelar tiene un efecto
              secundario —sumar plazas al cupo— y darlo por bueno dos veces
              significaría devolver el doble de plazas de las que se ocuparon,
              o sea sobreaforo. Mejor un conflicto visible.
            */
            throw new ConflictException(
                "RESERVATION_ALREADY_CANCELLED",
                "La reserva ya estaba cancelada.");
        }

        if (Status == ReservationStatus.CheckedIn)
        {
            throw new ConflictException(
                "RESERVATION_ALREADY_CHECKED_IN",
                "No se puede cancelar una reserva cuyo cliente ya llegó.");
        }

        Status = ReservationStatus.Cancelled;
        return PartySize;
    }

    /// <summary>El cliente no apareció. No devuelve cupo: la franja ya pasó.</summary>
    public void MarkNoShow()
    {
        if (Status != ReservationStatus.Confirmed)
        {
            throw new ConflictException(
                "RESERVATION_NOT_MARKABLE_NO_SHOW",
                $"Una reserva en estado {Status} no se puede marcar como ausencia.");
        }

        Status = ReservationStatus.NoShow;
    }

    /// <summary>Sella el envío de la confirmación. Lo llama notification-service.</summary>
    public void MarkConfirmationSent(DateTime at) => ConfirmationSentAt = at;

    /// <summary>Sella el envío del recordatorio; evita mandar dos.</summary>
    public void MarkReminderSent(DateTime at) => ReminderSentAt = at;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
