using Microsoft.Extensions.Logging;

namespace Booking.Business.Auditing;

// Logs de auditoría (consigna 3f): marcan el inicio y el fin de cada cadena de sucesos.
//
// DECISIÓN: el correlation id es el RequestId, el mismo en Availability, en el mensaje
// del broker, en Booking y en Payment (convención del equipo). Se abre un scope de logging
// con ese id para que TODOS los logs de la cadena lo lleven, no solo los de auditoría.
//
// Si Felipe define otro formato para la convención, se cambia solo en este archivo.
internal static class AuditLog
{
    private const string ServiceName = "Booking";

    public static IDisposable? BeginChain(ILogger logger, Guid requestId) =>
        logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = requestId,
            ["ServiceName"] = ServiceName,
        });

    public static void Start(ILogger logger, Guid requestId, string eventType) =>
        logger.LogInformation(
            "[AUDIT] START {EventType} | ServiceName={ServiceName} CorrelationId={CorrelationId}",
            eventType, ServiceName, requestId);

    public static void Step(ILogger logger, Guid requestId, string eventType) =>
        logger.LogInformation(
            "[AUDIT] STEP {EventType} | ServiceName={ServiceName} CorrelationId={CorrelationId}",
            eventType, ServiceName, requestId);

    public static void End(ILogger logger, Guid requestId, string eventType) =>
        logger.LogInformation(
            "[AUDIT] END {EventType} | ServiceName={ServiceName} CorrelationId={CorrelationId}",
            eventType, ServiceName, requestId);
}
