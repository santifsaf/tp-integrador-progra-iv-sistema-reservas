using Microsoft.Extensions.Logging;

namespace Payment.Business.Auditing;

// Logs de auditoría (consigna 3f): marcan el inicio y el fin de cada cadena de sucesos.
//
// DECISIÓN: se usa el mismo formato que Booking (ServiceName, CorrelationId, EventType,
// Outcome) para que los logs de los tres microservicios se puedan cruzar con un solo
// filtro por CorrelationId. El correlation id viaja en el mensaje del broker y en la
// metadata gRPC "x-correlation-id"; no se genera uno nuevo en cada salto.
//
// El scope hace que TODOS los logs escritos durante la operación lleven el id, no
// solamente las líneas de auditoría. Así, si Payment.Business agrega un log de negocio,
// también queda correlacionado sin pasarlo por parámetro.
public static class AuditLog
{
    public const string ServiceName = "Payment";

    public static IDisposable? BeginChain(ILogger logger, string correlationId, string? clientId = null)
    {
        var state = new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["ServiceName"] = ServiceName,
        };

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            state["ClientId"] = clientId;
        }

        return logger.BeginScope(state);
    }

    public static void Start(ILogger logger, string correlationId, string eventType, string? detail = null) =>
        Write(logger, "START", correlationId, eventType, detail: detail);

    public static void Step(ILogger logger, string correlationId, string eventType, string? detail = null) =>
        Write(logger, "STEP", correlationId, eventType, detail: detail);

    public static void End(
        ILogger logger,
        string correlationId,
        string eventType,
        string outcome,
        long elapsedMs,
        string? detail = null) =>
        Write(logger, "END", correlationId, eventType, outcome, elapsedMs, detail);

    private static void Write(
        ILogger logger,
        string phase,
        string correlationId,
        string eventType,
        string? outcome = null,
        long? elapsedMs = null,
        string? detail = null)
    {
        // Se loguea con parámetros estructurados (no interpolación) para que el log sea
        // consultable por campos en cualquier colector (Seq, Application Insights, etc.).
        logger.LogInformation(
            "[AUDIT] {Phase} {EventType} | ServiceName={ServiceName} CorrelationId={CorrelationId} " +
            "Outcome={Outcome} ElapsedMs={ElapsedMs} Detail={Detail}",
            phase,
            eventType,
            ServiceName,
            correlationId,
            outcome ?? "-",
            elapsedMs?.ToString() ?? "-",
            detail ?? "-");
    }
}
