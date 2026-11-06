namespace Booking.Data.Entities;

// DECISIÓN: los enums se guardan como TEXTO en la base (HasConversion<string>() en EF).
// En la tabla se lee "Held" en vez de 0, y el índice único parcial filtra por esos textos
// (ver IReservationRepository). Si se renombra un valor, hay que cambiar también el índice.
public enum ReservationStatus
{
    // Retenida: el asiento está apartado hasta que se procesa el cobro. Ocupa el asiento.
    Held,

    // Confirmada: el cobro fue exitoso. Ocupa el asiento.
    Confirmed,

    // Rechazada: la reserva no se concretó (ver RejectionReason). Libera el asiento.
    Rejected,

    // Vencida: se agotó el tiempo de retención sin resultado de pago. Libera el asiento.
    Expired,
}

public enum RejectionReason
{
    SeatOccupied,
    PaymentFailed,
    PaymentUnavailable,
}
