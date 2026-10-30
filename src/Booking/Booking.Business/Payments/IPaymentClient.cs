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
//       respuesta de éxito                      -> Succeeded
//       respuesta de pago rechazado             -> Failed
//       RpcException StatusCode.Unavailable     -> Unavailable
//       RpcException StatusCode.DeadlineExceeded-> Uncertain
//       cualquier otra RpcException             -> Uncertain
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

    // No se pudo conectar con Payment. La llamada no llegó, así que SEGURO no cobró:
    // se puede rechazar la reserva y liberar el asiento. Es el flujo de fallo de 3j
    // cuando falta el componente Payment.
    Unavailable,

    // La llamada salió pero no hubo respuesta (timeout u otro error a mitad de camino).
    // Payment PUDO haber cobrado, así que no es lo mismo que un pago fallido.
    Uncertain,
}
