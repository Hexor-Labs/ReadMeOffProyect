using HubNegocios.SharedKernel.Auditing;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Tenancy;

namespace HubNegocios.ReservationsService.Domain.Entities;

/// <summary>
/// Una franja concreta de un recurso reservable: la mesa del martes de 20:00 a
/// 23:00, la pista de las 18:00, la habitación de esa noche.
///
/// <see cref="AvailableCapacity"/> es el campo peligroso de todo el servicio.
/// Es un contador que se lee, se decide y se escribe, y ese patrón sin
/// protección es el que vende dos veces la misma mesa. La entidad valida la
/// regla, pero la entidad no puede protegerse de otra transacción que esté
/// haciendo lo mismo a la vez: eso lo resuelve el bloqueo pesimista de
/// <c>ReserveSlotHandler</c>, y por debajo la restricción CHECK de la tabla.
///
/// El dominio es puro: no conoce EF Core ni ASP.NET. Propiedades con
/// <c>private set</c> y cambios por métodos con nombre, para que un estado
/// imposible —cupo negativo, bloqueada sin motivo— no se pueda construir.
/// </summary>
public sealed class TimeSlot : ITenantOwned, IAuditable
{
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>Recurso reservable al que pertenece la franja: mesa, pista, sala…</summary>
    public Guid ItemId { get; private set; }

    public DateTime SlotStart { get; private set; }
    public DateTime SlotEnd { get; private set; }

    /// <summary>Aforo con el que nació la franja. No cambia al reservar.</summary>
    public int TotalCapacity { get; private set; }

    /// <summary>Lo que queda libre. Nunca por debajo de cero ni por encima del total.</summary>
    public int AvailableCapacity { get; private set; }

    public bool IsBlocked { get; private set; }

    /// <summary>Por qué está bloqueada: "evento privado", "mantenimiento"…</summary>
    public string? ReasonIfBlocked { get; private set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    private TimeSlot()
    {
    }

    public static TimeSlot Open(
        Guid id,
        Guid itemId,
        DateTime slotStart,
        DateTime slotEnd,
        int totalCapacity)
    {
        if (slotEnd <= slotStart)
        {
            throw new DomainException(
                "RESERVATION_SLOT_RANGE_INVALID",
                "La franja tiene que terminar después de empezar.");
        }

        if (totalCapacity <= 0)
        {
            throw new DomainException(
                "RESERVATION_SLOT_CAPACITY_INVALID",
                "El aforo de la franja tiene que ser mayor que cero.");
        }

        return new TimeSlot
        {
            Id = id,
            ItemId = itemId,
            // UTC siempre. Una franja guardada en hora local deja de significar
            // lo mismo en cuanto el negocio abre una sede en otro huso.
            SlotStart = DateTime.SpecifyKind(slotStart, DateTimeKind.Utc),
            SlotEnd = DateTime.SpecifyKind(slotEnd, DateTimeKind.Utc),
            TotalCapacity = totalCapacity,
            AvailableCapacity = totalCapacity,
            IsBlocked = false,
        };
    }

    /// <summary>
    /// Descuenta plazas del cupo disponible.
    ///
    /// Quien llame a esto tiene que haber bloqueado antes la fila en la base de
    /// datos. El objeto en memoria comprueba la regla sobre el valor que leyó,
    /// y ese valor puede estar caducado desde el instante siguiente a leerlo.
    /// </summary>
    public void Reserve(int partySize)
    {
        if (partySize <= 0)
        {
            throw new DomainException(
                "RESERVATION_PARTY_SIZE_INVALID",
                "El número de comensales tiene que ser mayor que cero.");
        }

        if (IsBlocked)
        {
            throw new ConflictException(
                "RESERVATION_SLOT_BLOCKED",
                ReasonIfBlocked is null
                    ? "La franja está bloqueada."
                    : $"La franja está bloqueada: {ReasonIfBlocked}.");
        }

        if (partySize > AvailableCapacity)
        {
            throw new ConflictException(
                "RESERVATION_SLOT_FULL",
                $"La franja ya solo tiene {AvailableCapacity} plazas libres y se piden {partySize}.");
        }

        AvailableCapacity -= partySize;
    }

    /// <summary>
    /// Devuelve plazas al cupo tras una cancelación.
    ///
    /// Se topa en <see cref="TotalCapacity"/> a propósito: si un error de más
    /// arriba liberara dos veces la misma reserva, el tope convierte un
    /// sobreaforo silencioso —que se descubre cuando llega gente de más— en un
    /// dato simplemente inexacto.
    /// </summary>
    public void Release(int partySize)
    {
        if (partySize <= 0)
        {
            throw new DomainException(
                "RESERVATION_PARTY_SIZE_INVALID",
                "El número de plazas a liberar tiene que ser mayor que cero.");
        }

        AvailableCapacity = Math.Min(TotalCapacity, AvailableCapacity + partySize);
    }

    /// <summary>Cierra la franja a nuevas reservas sin tocar las ya confirmadas.</summary>
    public void Block(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        IsBlocked = true;
        ReasonIfBlocked = reason.Trim();
    }

    public void Unblock()
    {
        IsBlocked = false;
        ReasonIfBlocked = null;
    }
}
