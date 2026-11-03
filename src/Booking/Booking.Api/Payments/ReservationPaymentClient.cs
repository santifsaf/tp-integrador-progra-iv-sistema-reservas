using Booking.Business.Payments;
using Booking.Data.Entities;

namespace Booking.Api.Payments;

// Adapter: conecta el contrato de Business con el gateway gRPC. Hay una sola
// operación lógica de cobro por reserva, por eso su Id también identifica el pago
// en todos los reintentos. Las transiciones de estado siguen a cargo de Business.
public sealed class ReservationPaymentClient(GrpcPaymentGateway gateway) : IPaymentClient
{
    public Task<PaymentResult> ChargeAsync(Reservation reservation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        var charge = new PaymentCharge(
            RequestId: reservation.RequestId,
            ReservationId: reservation.Id,
            PaymentOperationId: reservation.Id,
            AmountCents: reservation.AmountCents,
            Currency: reservation.Currency);

        // El gateway valida antes de pedir el token o llamar a Payment. Un dato
        // incompleto es un error de entrada, no una respuesta de cobro rechazado.
        return gateway.ChargeAsync(charge, ct);
    }
}
