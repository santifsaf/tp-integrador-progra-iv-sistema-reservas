using Booking.Business;
using Booking.Business.Messages;
using Booking.Data.Entities;
using Booking.Demo.Simulation;
using Microsoft.Extensions.Options;

namespace Booking.Demo;

// Los escenarios que se pueden disparar desde el menú. Cada uno hace de "Availability":
// arma el mensaje ReservationRequested, lo publica en el broker y espera el resultado.
public sealed class DemoScenarios(
    IDemoBroker broker,
    InMemoryReservationRepository repository,
    SimulatedPaymentClient payment,
    SeatCatalog seats,
    IOptions<ReservationsOptions> options)
{
    private readonly Guid _defaultUser = Guid.NewGuid();
    private TimeSpan HoldDuration => options.Value.HoldDuration;

    // ---------------------------------------------------------------- menú

    public async Task RunMenuAsync()
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("================ SIMULADOR BOOKING ================");
            Console.WriteLine($" Evento: {seats.EventName} | Asientos A1..A10 | Retención: {HoldDuration.TotalSeconds:0}s");
            Console.WriteLine($" Broker: {broker.Description}");
            Console.WriteLine("---------------------------------------------------");
            Console.WriteLine(" 1) Reservar un asiento (elegís asiento y respuesta de Payment)");
            Console.WriteLine(" 2) Happy path: reserva con pago exitoso");
            Console.WriteLine(" 3) Asiento ocupado: dos personas, mismo asiento, una después de la otra");
            Console.WriteLine(" 4) Pago rechazado y después otra persona reserva el asiento liberado");
            Console.WriteLine(" 5) Payment responde 'no disponible'");
            Console.WriteLine(" 6) Carrera: 10 personas piden el mismo asiento al mismo tiempo");
            Console.WriteLine(" 7) Mensaje duplicado: el broker entrega dos veces la misma solicitud");
            Console.WriteLine(" 8) Timeout: Payment cobra pero la respuesta se pierde");
            Console.WriteLine(" 9) Payment caído o sin respuesta: la reserva vence y libera el asiento (flujo de fallo 3j)");
            Console.WriteLine(" e) Ver estado de todas las reservas");
            Console.WriteLine(" t) Correr todos los escenarios");
            Console.WriteLine(" 0) Salir");
            Console.Write("> ");

            var option = Console.ReadLine()?.Trim().ToLowerInvariant();
            switch (option)
            {
                case "1": await ManualAsync(); break;
                case "2": await HappyPathAsync(); break;
                case "3": await SeatOccupiedAsync(); break;
                case "4": await PaymentFailedAsync(); break;
                case "5": await PaymentUnavailableAsync(); break;
                case "6": await RaceAsync(); break;
                case "7": await DuplicateMessageAsync(); break;
                case "8": await TimeoutThenSucceededAsync(); break;
                case "9": await NeverRespondsAsync(); break;
                case "e": PrintState(); break;
                case "t": await RunAllAsync(); break;
                case "0" or null: return;
                default: Console.WriteLine("Opción inválida."); break;
            }
        }
    }

    public async Task RunAllAsync()
    {
        await HappyPathAsync();
        await SeatOccupiedAsync();
        await PaymentFailedAsync();
        await PaymentUnavailableAsync();
        await RaceAsync();
        await DuplicateMessageAsync();
        await TimeoutThenSucceededAsync();
        await NeverRespondsAsync();
        Title("RESUMEN FINAL");
        PrintState();
    }

    // ---------------------------------------------------------------- escenarios

    private async Task ManualAsync()
    {
        Console.Write("Asiento (A1..A10): ");
        var label = Console.ReadLine() ?? "";
        if (!seats.TryGetSeatId(label, out _))
        {
            Console.WriteLine("Ese asiento no existe (en el sistema real, Availability lo rechazaría antes de publicar).");
            return;
        }

        Console.WriteLine("Respuesta de Payment: 1) éxito  2) rechazado  3) no disponible  4) timeout y después éxito  5) caído / nunca responde");
        Console.Write("> ");
        var scenario = (Console.ReadLine()?.Trim()) switch
        {
            "2" => PaymentScenario.Failed,
            "3" => PaymentScenario.Unavailable,
            "4" => PaymentScenario.TimeoutThenSucceeded,
            "5" => PaymentScenario.NeverResponds,
            _ => PaymentScenario.Succeeded,
        };

        var request = await SendAsync(label.Trim().ToUpperInvariant(), scenario);
        await WaitForBrokerAsync();
        if (scenario == PaymentScenario.NeverResponds) await WaitUntilNotHeldAsync(request.RequestId);
        PrintResult(request.RequestId);
    }

    private async Task HappyPathAsync()
    {
        Title("HAPPY PATH: asiento libre y pago exitoso",
            "Esperado: Held -> Confirmed, un cobro real, avisos de 'creada' y 'confirmada'.");
        var request = await SendAsync("A1", PaymentScenario.Succeeded);
        await WaitForBrokerAsync();
        PrintResult(request.RequestId);
    }

    private async Task SeatOccupiedAsync()
    {
        Title("ASIENTO OCUPADO",
            "Primero alguien confirma A2. Después otra persona pide A2.",
            "Esperado: la segunda queda Rejected (SeatOccupied) sin pasar por Payment.");
        var first = await SendAsync("A2", PaymentScenario.Succeeded);
        await WaitForBrokerAsync();
        var second = await SendAsync("A2", PaymentScenario.Succeeded, Guid.NewGuid());
        await WaitForBrokerAsync();
        PrintResult(first.RequestId, second.RequestId);
    }

    private async Task PaymentFailedAsync()
    {
        Title("PAGO RECHAZADO",
            "El pago de A3 se rechaza. Después otra persona pide A3.",
            "Esperado: la primera queda Rejected (PaymentFailed) y libera el asiento; la segunda se confirma.");
        var first = await SendAsync("A3", PaymentScenario.Failed);
        await WaitForBrokerAsync();
        var second = await SendAsync("A3", PaymentScenario.Succeeded, Guid.NewGuid());
        await WaitForBrokerAsync();
        PrintResult(first.RequestId, second.RequestId);
    }

    private async Task PaymentUnavailableAsync()
    {
        Title("PAYMENT RESPONDE 'NO DISPONIBLE'",
            "Esperado: Rejected (PaymentUnavailable), sin cobro, asiento A4 liberado.");
        var request = await SendAsync("A4", PaymentScenario.Unavailable);
        await WaitForBrokerAsync();
        PrintResult(request.RequestId);
    }

    private async Task RaceAsync()
    {
        Title("CARRERA: 10 personas piden A5 al mismo tiempo",
            "Booking procesa varios mensajes en paralelo. El repositorio imita el índice único parcial.",
            "Esperado: exactamente 1 Confirmed y 9 Rejected (SeatOccupied), un solo cobro.");
        var requests = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => SendAsync("A5", PaymentScenario.Succeeded, Guid.NewGuid())));
        await WaitForBrokerAsync();
        PrintResult(requests.Select(r => r.RequestId).ToArray());

        var all = repository.Snapshot().Where(r => requests.Any(q => q.RequestId == r.RequestId)).ToList();
        var confirmed = all.Count(r => r.Status == ReservationStatus.Confirmed);
        var occupied = all.Count(r => r.RejectionReason == RejectionReason.SeatOccupied);
        Check(confirmed == 1 && occupied == 9, $"{confirmed} confirmada(s), {occupied} rechazada(s) por asiento ocupado");
    }

    private async Task DuplicateMessageAsync()
    {
        Title("MENSAJE DUPLICADO",
            "El broker entrega DOS veces la misma solicitud para A6 (pasa si una réplica se cae antes de completar).",
            "Esperado: una sola reserva y un solo cobro real (idempotencia por RequestId).");
        var request = NewMessage("A6", _defaultUser);
        payment.Plan(request.RequestId, PaymentScenario.Succeeded);
        await broker.PublishAsync(request);
        await broker.PublishAsync(request);
        await WaitForBrokerAsync();
        PrintResult(request.RequestId);

        var rows = repository.Snapshot().Count(r => r.RequestId == request.RequestId);
        Check(rows == 1, $"{rows} reserva(s) para la misma solicitud");
    }

    private async Task TimeoutThenSucceededAsync()
    {
        Title("TIMEOUT: Payment cobra pero la respuesta se pierde",
            "Booking no sabe si cobró: no rechaza, deja la reserva Held y el mensaje se reintenta.",
            "En el reintento Payment devuelve el resultado guardado (idempotente).",
            "Esperado: Confirmed con UN solo cobro real.");
        var request = await SendAsync("A7", PaymentScenario.TimeoutThenSucceeded);
        await WaitForBrokerAsync();
        PrintResult(request.RequestId);
    }

    private async Task NeverRespondsAsync()
    {
        Title("PAYMENT CAÍDO O SIN RESPUESTA (flujo de fallo 3j)",
            "Un error de comunicación no garantiza que Payment no haya cobrado: no se rechaza.",
            $"Cada reintento da timeout. A los {HoldDuration.TotalSeconds:0}s la retención vence.",
            "Esperado: Expired (sin resultado de pago), sin cobro, y A8 queda libre para otra persona.",
            "Esto tarda unos segundos...");
        var request = await SendAsync("A8", PaymentScenario.NeverResponds);
        await WaitForBrokerAsync();
        await WaitUntilNotHeldAsync(request.RequestId);

        var next = await SendAsync("A8", PaymentScenario.Succeeded, Guid.NewGuid());
        await WaitForBrokerAsync();
        PrintResult(request.RequestId, next.RequestId);
    }

    // ---------------------------------------------------------------- helpers

    private ReservationRequestedMessage NewMessage(string seatLabel, Guid userId) =>
        new(Guid.NewGuid(), seats.EventId, seats.SeatId(seatLabel), userId, DateTimeOffset.UtcNow);

    private async Task<ReservationRequestedMessage> SendAsync(string seatLabel, PaymentScenario scenario, Guid? userId = null)
    {
        var message = NewMessage(seatLabel, userId ?? _defaultUser);
        payment.Plan(message.RequestId, scenario);
        await broker.PublishAsync(message);
        return message;
    }

    private async Task WaitForBrokerAsync()
    {
        await broker.WaitUntilIdleAsync(TimeSpan.FromSeconds(90));
        await Task.Delay(300); // deja que terminen de imprimirse los logs
    }

    private async Task WaitUntilNotHeldAsync(Guid requestId)
    {
        var until = DateTime.UtcNow + HoldDuration + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < until
               && repository.Snapshot().Any(r => r.RequestId == requestId && r.Status == ReservationStatus.Held))
            await Task.Delay(500);
        await Task.Delay(300);
    }

    private static void Title(string title, params string[] lines)
    {
        Console.WriteLine();
        Console.WriteLine($"########## {title} ##########");
        foreach (var line in lines) Console.WriteLine($"  {line}");
        Console.WriteLine();
    }

    private static void Check(bool ok, string detail) =>
        Console.WriteLine(ok ? $"  OK  -> {detail}" : $"  FALLÓ -> {detail}");

    private void PrintResult(params Guid[] requestIds)
    {
        var rows = repository.Snapshot().Where(r => requestIds.Contains(r.RequestId)).ToList();
        Console.WriteLine();
        Console.WriteLine("  Resultado:");
        PrintRows(rows);
    }

    public void PrintState()
    {
        Console.WriteLine();
        Console.WriteLine("  Todas las reservas:");
        PrintRows(repository.Snapshot());

        var occupied = repository.Snapshot()
            .Where(r => r.Status is ReservationStatus.Held or ReservationStatus.Confirmed)
            .Select(r => seats.Label(r.SeatId)).Distinct().OrderBy(s => s).ToList();
        Console.WriteLine($"  Asientos ocupados: {(occupied.Count == 0 ? "ninguno" : string.Join(", ", occupied))}");
        Console.WriteLine($"  Mensajes en dead-letter: {broker.DeadLetterInfo}");
    }

    private void PrintRows(IReadOnlyList<Reservation> rows)
    {
        if (rows.Count == 0)
        {
            Console.WriteLine("    (no hay reservas)");
            return;
        }

        Console.WriteLine($"    {"Asiento",-8}{"Solicitud",-11}{"Estado",-11}{"Motivo",-20}{"Cobros",-7}");
        foreach (var r in rows)
        {
            Console.WriteLine($"    {seats.Label(r.SeatId),-8}{r.RequestId.ToString()[..8],-11}{r.Status,-11}" +
                              $"{r.RejectionReason?.ToString() ?? "-",-20}{payment.RealCharges(r.Id),-7}");
        }
    }
}
