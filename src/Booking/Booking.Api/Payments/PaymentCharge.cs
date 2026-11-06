namespace Booking.Api.Payments;

// DTO de entrada del gateway: expresa los datos del cobro sin acoplar el transporte
// gRPC a la entidad Reservation. RequestId correlaciona los logs y PaymentOperationId
// identifica el mismo cobro en todos sus reintentos. ReservationPaymentClient lo
// construye con los datos de la reserva y utiliza su Id como identificador de operación.
public sealed record PaymentCharge(
    Guid RequestId,
    Guid ReservationId,
    Guid PaymentOperationId,
    long AmountCents,
    string Currency);
