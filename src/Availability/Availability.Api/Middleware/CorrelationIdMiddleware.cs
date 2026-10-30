using Microsoft.AspNetCore.Http;

namespace Availability.Api.Middleware
{
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CorrelationIdMiddleware> _logger;
        private const string CorrelationIdHeaderName = "X-Correlation-ID";
        private const string CorrelationIdItemName = "CorrelationId";

        public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Leer o generar Correlation ID
            string correlationId = context.Request.Headers[CorrelationIdHeaderName].ToString();
            if (string.IsNullOrEmpty(correlationId))
            {
                correlationId = Guid.NewGuid().ToString("N");
            }

            // Guardar en HttpContext.Items para acceso en controladores/servicios
            context.Items[CorrelationIdItemName] = correlationId;

            // Agregar al response header
            context.Response.Headers[CorrelationIdHeaderName] = correlationId;

            // Log de auditoría al recibir la solicitud
            _logger.LogInformation(
                "Solicitud recibida | CorrelationId: {CorrelationId} | Método: {Method} | Path: {Path}",
                correlationId,
                context.Request.Method,
                context.Request.Path
            );

            await _next(context);

            // Log de auditoría al emitir respuesta
            _logger.LogInformation(
                "Respuesta emitida | CorrelationId: {CorrelationId} | StatusCode: {StatusCode}",
                correlationId,
                context.Response.StatusCode
            );
        }
    }
}
