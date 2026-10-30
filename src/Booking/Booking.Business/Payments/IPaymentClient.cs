using Booking.Data.Entities;

namespace Booking.Business.Payments;

// CONTRATO ENTRE LA LÓGICA DE NEGOCIO (Gian) Y EL CLIENTE gRPC A PAYMENT (Santi).
//
// DECISIÓN: la capa de negocio depende de esta interfaz y no del cliente generado del
// .proto. Así la lógica se puede probar sin levantar Payment, y si cambia el .proto solo
// cambia la implementación de Santi.
//
// Para Santi, al implementarla:
//   - Usar un deadline en la llamada (por ejemplo 5 s).
//   - Mandar reservation.Id como clave de idempotencia: Booking puede volver a pedir el
//     cobro de la misma reserva (reintentos) y Payment no debe cobrar dos veces.
//     Acordarlo con Agus y Juan en el .proto (payments.reservation_id UNIQUE).
//   - Cómo mapear la respuesta (ver PaymentResult):
//       respuesta PAYMENT_SUCCEEDED                   -> Succeeded
//       respuesta PAYMENT_REJECTED                    -> Failed
//       respuesta PAYMENT_UNAVAILABLE                 -> Unavailable
//       PAYMENT_STATUS_UNSPECIFIED (o PENDING)        -> Uncertain
//       CUALQUIER RpcException, incluida
//       StatusCode.Unavailable y DeadlineExceeded     -> Uncertain
//
// DECISIÓN: solo una RESPUESTA EXPLÍCITA de Payment puede rechazar la reserva. Un error
// técnico de comunicación, aunque sea StatusCode.Unavailable, no garantiza que Payment no
// haya procesado el cobro (la conexión puede cortarse después de que el pedido llegó).
// Por eso se trata como resultado incierto: la reserva queda Held, se reintenta y, si
// nunca llega un resultado, vence (Expired = "sin resultado de pago", como dice la consigna).
public interface IPaymentClient
{
    Task<PaymentResult> ChargeAsync(Reservation reservation, CancellationToken ct);
}

public enum PaymentResult
{
    // Payment cobró.
    Succeeded,

    // Payment respondió que el pago fue rechazado.
    Failed,

    // Payment RESPONDIÓ explícitamente que el pago no está disponible (PAYMENT_UNAVAILABLE).
    // Es una respuesta de negocio: Payment informa que no cobró, así que se puede rechazar
    // la reserva y liberar el asiento. No confundir con el error gRPC StatusCode.Unavailable.
    Unavailable,

    // No hay un resultado confiable: timeout, Payment caído (StatusCode.Unavailable) o
    // cualquier error de comunicación. Payment PUDO haber cobrado, así que no se rechaza.
    // Si Payment está caído durante todo el plazo de retención, la reserva termina Expired
    // y el asiento se libera: ese es el flujo de fallo de 3j cuando falta Payment.
    Uncertain,
}
