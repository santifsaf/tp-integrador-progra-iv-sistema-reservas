namespace Booking.Api.Payments;

// DTO de entrada del gateway: recibe los datos del cobro explícitamente porque
// Reservation todavía no contiene importe ni moneda. Permite probar la comunicación
// sin modificar el contrato del broker ni inventar valores para una reserva.
// RequestId correlaciona los logs; PaymentOperationId identifica el mismo cobro
// en todos sus reintentos. Su elección corresponde al futuro adaptador de la reserva.
public sealed record PaymentCharge(
    Guid RequestId,
    Guid ReservationId,
    Guid PaymentOperationId,
    long AmountCents,
    string Currency);
