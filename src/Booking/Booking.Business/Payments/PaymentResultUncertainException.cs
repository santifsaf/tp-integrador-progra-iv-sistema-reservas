namespace Booking.Business.Payments;

// Se lanza cuando Payment no dio un resultado claro (timeout).
//
// DECISIÓN: un timeout no rechaza la reserva, porque Payment pudo haber cobrado.
// Se lanza esta excepción para que el consumidor abandone el mensaje y Service Bus lo
// vuelva a entregar. En el reintento la reserva sigue Held y se vuelve a pedir el cobro:
// como Payment es idempotente por ReservationId, devuelve el resultado real sin cobrar
// de nuevo. Si nunca llega un resultado, la reserva vence y pasa a Expired, que es
// exactamente como la consigna define "Vencida": sin un resultado de pago.
public sealed class PaymentResultUncertainException(Guid reservationId)
    : Exception($"Payment no dio un resultado claro para la reserva {reservationId}; se reintenta.")
{
    public Guid ReservationId { get; } = reservationId;
}
