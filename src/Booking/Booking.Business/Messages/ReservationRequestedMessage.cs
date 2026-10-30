namespace Booking.Business.Messages;

// CONTRATO DEL MENSAJE DEL BROKER (Availability publica, Booking consume).
// Pendiente de cerrar con Rebeca. Cuando esté acordado, se mueve a /contracts para que
// ambos servicios usen la misma definición (cambios solo vía rama chore/contracts-*).
//
// Se serializa como JSON con nombres en camelCase:
// {
//   "requestId": "2f1c...", "eventId": "9a0b...", "seatId": "77de...",
//   "userId": "5c3e...", "requestedAt": "2026-10-28T02:00:00Z"
// }
//
// No hace falta un campo correlationId aparte: el correlation id ES el requestId.
// Sugerencia para Availability: poner MessageId = requestId y CorrelationId = requestId
// en el ServiceBusMessage, así el broker y los logs usan el mismo identificador.
public sealed record ReservationRequestedMessage(
    Guid RequestId,
    Guid EventId,
    Guid SeatId,
    Guid UserId,
    DateTimeOffset RequestedAt);
