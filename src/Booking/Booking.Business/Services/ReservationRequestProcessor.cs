using Booking.Business.Events;
using Booking.Business.Messages;
using Booking.Business.Payments;
using Booking.Data;
using Booking.Data.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Booking.Business.Auditing.AuditLog;

namespace Booking.Business.Services;

// Capa de negocio de Booking (N-Layer): procesa una solicitud de reserva que llegó por el
// broker (pasos 4, 5 y 6 del flujo).
//
// Esta clase no sabe nada del broker. La llama el consumidor (Booking.Api/Messaging), que
// decide qué hacer con el mensaje según cómo termine:
//   - Termina bien:     el mensaje se completa y se borra de la suscripción.
//   - Lanza excepción:  el mensaje se abandona y Service Bus lo vuelve a entregar.
// Por eso el procesamiento tiene que ser IDEMPOTENTE: procesar dos veces el mismo mensaje
// tiene que dejar el mismo resultado que procesarlo una vez.
//
// DECISIÓN: idempotencia por RequestId, con un índice único en la base. Service Bus
// garantiza entrega "al menos una vez", no "exactamente una vez". Un mensaje se repite si
// una réplica se cae después de procesar y antes de completar, o si se vence el lock.
//
// DECISIÓN: la recuperación no "continúa desde donde se cortó": cada entrega vuelve a
// ejecutar el procesamiento completo y decide qué pasos faltan mirando el estado guardado
// en la base. Por eso las réplicas no necesitan compartir memoria.
public sealed class ReservationRequestProcessor(
    IReservationRepository repository,
    IPaymentClient payment,
    IReservationEventPublisher publisher,
    IOptions<ReservationsOptions> options,
    TimeProvider clock,
    ILogger<ReservationRequestProcessor> logger)
{
    private readonly ReservationsOptions _options = options.Value;

    public async Task<Reservation> ProcessAsync(ReservationRequestedMessage request, CancellationToken ct)
    {
        using var _ = BeginChain(logger, request.RequestId);
        Start(logger, request.RequestId, "ProcessReservationRequest");

        var (reservation, shouldCharge) = await GetOrHoldAsync(request, ct);

        // Solo se cobra una reserva Held que le corresponde a esta entrega. Si ya está en un
        // estado final, o si otra réplica la está procesando en este momento, no hay más que hacer.
        if (shouldCharge && reservation.Status == ReservationStatus.Held)
        {
            await ChargeAndResolveAsync(reservation, ct);
        }

        End(logger, request.RequestId, $"ProcessReservationRequest -> {Describe(reservation)}");
        return reservation;
    }

    // Paso 4: si el asiento está libre se crea una reserva Held; si no, se rechaza.
    // Devuelve además si ESTA entrega del mensaje tiene que encargarse del cobro.
    private async Task<(Reservation Reservation, bool ShouldCharge)> GetOrHoldAsync(
        ReservationRequestedMessage request, CancellationToken ct)
    {
        // Idempotencia, primer chequeo: ¿esta solicitud ya generó una reserva?
        var existing = await repository.GetByRequestIdAsync(request.RequestId, ct);
        if (existing is not null)
        {
            // Si sigue Held, una entrega anterior se cortó antes de tener el resultado del
            // pago (réplica caída o timeout). Se devuelve tal cual y ProcessAsync retoma
            // desde el cobro. Payment es idempotente por ReservationId: no cobra dos veces.
            Step(logger, request.RequestId, $"DuplicateMessage (reservation already {existing.Status})");
            return (existing, true);
        }

        var now = clock.GetUtcNow();
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            RequestId = request.RequestId,
            UserId = request.UserId,
            EventId = request.EventId,
            SeatId = request.SeatId,
            Status = ReservationStatus.Held,
            ExpiresAt = now + _options.HoldDuration,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // No se consulta "¿está libre?" antes de insertar: entre la consulta y el insert
        // otra réplica podría ocupar el asiento. Se intenta insertar directamente y el
        // índice único parcial de la base decide quién gana (ver IReservationRepository).
        var result = await repository.TryHoldAsync(reservation, ct);

        switch (result)
        {
            case HoldResult.Created:
                Step(logger, request.RequestId, $"ReservationHeld {reservation.Id} expires {reservation.ExpiresAt:O}");
                await PublishSafelyAsync(ReservationEventType.ReservationCreated, reservation, ct);
                return (reservation, true);

            case HoldResult.SeatOccupied:
                reservation.Status = ReservationStatus.Rejected;
                reservation.RejectionReason = RejectionReason.SeatOccupied;
                reservation.ExpiresAt = null;
                await repository.AddRejectedAsync(reservation, ct);
                Step(logger, request.RequestId, "ReservationRejected (SeatOccupied)");
                return (reservation, false);

            case HoldResult.DuplicateRequest:
                // Idempotencia, segundo chequeo: otra réplica insertó la reserva de esta
                // misma solicitud entre nuestra consulta y nuestro insert, y la está
                // procesando AHORA. Esa réplica se encarga del cobro: si esta también
                // cobrara, habría dos llamadas a Payment en paralelo para la misma reserva.
                // Si la otra réplica se cae, su entrega vuelve al broker y se retoma;
                // si nunca se retoma, la reserva vence.
                Step(logger, request.RequestId, "RequestAlreadyProcessedByAnotherReplica (no charge)");
                var current = await repository.GetByRequestIdAsync(request.RequestId, ct)
                    ?? throw new InvalidOperationException(
                        $"La base informó la solicitud {request.RequestId} como duplicada pero no se encontró.");
                return (current, false);

            default:
                throw new InvalidOperationException($"Resultado de retención no contemplado: {result}");
        }
    }

    // Pasos 5 y 6: cobrar por gRPC y confirmar o rechazar según la respuesta de Payment.
    private async Task ChargeAndResolveAsync(Reservation reservation, CancellationToken ct)
    {
        // Si la retención ya venció (por ejemplo, el mensaje se reintentó mucho tiempo),
        // no se pide el cobro: el job de vencimiento la va a pasar a Expired.
        if (reservation.ExpiresAt <= clock.GetUtcNow())
        {
            Step(logger, reservation.RequestId, "HoldExpiredBeforeCharge (no charge requested)");
            return;
        }

        Step(logger, reservation.RequestId, $"PaymentRequested reservation {reservation.Id}");
        var paymentResult = await payment.ChargeAsync(reservation, ct);
        Step(logger, reservation.RequestId, $"PaymentResult {paymentResult}");

        (ReservationStatus Status, RejectionReason? Reason) target = paymentResult switch
        {
            PaymentResult.Succeeded => (ReservationStatus.Confirmed, null),
            PaymentResult.Failed => (ReservationStatus.Rejected, RejectionReason.PaymentFailed),

            // Payment no está: la llamada no llegó, así que no cobró. Se rechaza y el
            // asiento queda libre. Es el flujo de fallo de 3j.
            PaymentResult.Unavailable => (ReservationStatus.Rejected, RejectionReason.PaymentUnavailable),

            // Resultado incierto: la reserva queda Held y se reintenta el mensaje
            // (ver PaymentResultUncertainException).
            PaymentResult.Uncertain => throw new PaymentResultUncertainException(reservation.Id),

            _ => throw new InvalidOperationException($"Resultado de pago no contemplado: {paymentResult}"),
        };

        var now = clock.GetUtcNow();

        // Transición condicional: solo se aplica si la reserva SIGUE Held. Si mientras
        // esperábamos a Payment el job de vencimiento la pasó a Expired, el asiento ya pudo
        // haberse liberado para otra persona y no podemos confirmarla.
        var applied = await repository.TryChangeStatusAsync(
            reservation.Id, ReservationStatus.Held, target.Status, target.Reason, now, ct);

        if (!applied)
        {
            var current = await repository.GetByRequestIdAsync(reservation.RequestId, ct);
            if (current is not null)
            {
                reservation.Status = current.Status;
                reservation.RejectionReason = current.RejectionReason;
                reservation.UpdatedAt = current.UpdatedAt;
            }

            // Solo hace falta devolver la plata si se cobró y la reserva NO quedó confirmada
            // (por ejemplo, venció mientras se esperaba a Payment). Si ya estaba Confirmed,
            // es el mismo cobro informado dos veces (Payment es idempotente).
            if (paymentResult == PaymentResult.Succeeded && reservation.Status != ReservationStatus.Confirmed)
            {
                // Se cobró una reserva que ya no estaba Held. En un sistema real esto
                // dispara una devolución; para el TP queda registrado para conciliación.
                logger.LogWarning(
                    "Pago exitoso para la reserva {ReservationId}, pero ya estaba {Status}. Requiere devolución.",
                    reservation.Id, reservation.Status);
            }

            Step(logger, reservation.RequestId, $"PaymentResultDiscarded (reservation already {reservation.Status})");
            return;
        }

        reservation.Status = target.Status;
        reservation.RejectionReason = target.Reason;
        reservation.UpdatedAt = now;

        if (target.Status == ReservationStatus.Confirmed)
        {
            await PublishSafelyAsync(ReservationEventType.ReservationConfirmed, reservation, ct);
        }
    }

    // DECISIÓN: si falla el aviso a Notifications, NO se reprocesa la reserva.
    // La reserva ya quedó guardada y es lo importante; reintentar el mensaje entero solo
    // por la notificación implicaría volver a pedir el cobro. La solución robusta sería un
    // "transactional outbox" (guardar el evento en la misma transacción y publicarlo
    // después), que excede el alcance del TP.
    private async Task PublishSafelyAsync(ReservationEventType type, Reservation reservation, CancellationToken ct)
    {
        try
        {
            await publisher.PublishAsync(type, reservation, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "No se pudo publicar {EventType} para la reserva {ReservationId}", type, reservation.Id);
        }
    }

    private static string Describe(Reservation r) =>
        r.RejectionReason is null ? r.Status.ToString() : $"{r.Status} ({r.RejectionReason})";
}
