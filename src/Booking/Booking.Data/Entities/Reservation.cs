namespace Booking.Data.Entities;

// DECISIÓN: dominio anémico (consigna 3b-ii).
// La entidad solo transporta datos: no tiene métodos ni valida sus transiciones de estado.
// Las reglas (cuándo pasa a Held, Confirmed, Rejected o Expired) viven en la capa de
// negocio (Booking.Business), que es lo que caracteriza a un modelo anémico en N-Layer.
//
// Booking no tiene tablas de eventos ni de asientos: el catálogo es de Availability.
// EventId y SeatId son referencias lógicas, sin FK, porque cada servicio tiene su base.
public class Reservation
{
    public Guid Id { get; set; }

    // Identificador que asigna Availability a la solicitud (requestId).
    // Da la idempotencia (una solicitud genera como máximo una reserva) y es el
    // correlation id de los logs de auditoría en todos los servicios.
    public Guid RequestId { get; set; }

    // "sub" del token de Keycloak del usuario que pidió la reserva.
    public Guid UserId { get; set; }

    public Guid EventId { get; set; }

    public Guid SeatId { get; set; }

    public ReservationStatus Status { get; set; }

    // Solo tiene valor cuando Status == Rejected.
    public RejectionReason? RejectionReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Solo tiene valor en las reservas que pasaron por Held: es el límite para recibir el
    // resultado del pago antes de pasar a Expired (paso 8 del flujo).
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
