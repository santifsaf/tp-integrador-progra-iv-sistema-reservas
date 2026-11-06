namespace Booking.Business.Messages;

// Reglas mínimas para que una solicitud de reserva se pueda procesar.
//
// DECISIÓN: si un campo falta en el JSON, el deserializador no falla: deja el valor por
// defecto (Guid.Empty, 0, null). Sin esta validación, un mensaje sin eventId, seatId o
// userId se procesaría igual y podría terminar en una reserva confirmada (y cobrada) con
// esos ids vacíos. Por eso se valida explícitamente cada campo.
//
// Se usa en dos lugares:
//   - el consumidor (Booking.Api/Messaging), para mandar el mensaje a la dead-letter
//     queue: un mensaje inválido no se arregla reintentando;
//   - ReservationRequestProcessor, como segunda barrera, por si alguien lo llama sin
//     pasar por el consumidor. Así la regla vive en la capa de negocio y se puede testear.
public static class ReservationRequestedMessageValidator
{
    // Devuelve la lista de problemas encontrados; vacía si el mensaje es válido.
    public static IReadOnlyList<string> Validate(ReservationRequestedMessage? message)
    {
        if (message is null)
            return ["El mensaje está vacío o no es un JSON válido."];

        var errors = new List<string>();

        if (message.RequestId == Guid.Empty) errors.Add("Falta requestId.");
        if (message.EventId == Guid.Empty) errors.Add("Falta eventId.");
        if (message.SeatId == Guid.Empty) errors.Add("Falta seatId.");
        if (message.UserId == Guid.Empty) errors.Add("Falta userId.");
        if (message.RequestedAt == default) errors.Add("Falta requestedAt.");

        // Payment no acepta importes en cero o negativos.
        if (message.AmountCents <= 0) errors.Add("amountCents tiene que ser mayor que cero.");

        // Código ISO 4217: tres letras en mayúscula (por ejemplo "ARS"). Puede llegar null
        // aunque el tipo no lo admita, si el campo no viene en el JSON.
        if (message.Currency is not { Length: 3 } || message.Currency.Any(c => c is < 'A' or > 'Z'))
            errors.Add("currency tiene que ser un código de 3 letras en mayúscula, por ejemplo ARS.");

        return errors;
    }
}

// Se lanza cuando ReservationRequestProcessor recibe una solicitud que no cumple el
// contrato. El consumidor la manda a la dead-letter queue en vez de reintentarla.
public sealed class InvalidReservationRequestException(IReadOnlyList<string> errors)
    : Exception("Solicitud de reserva inválida: " + string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
