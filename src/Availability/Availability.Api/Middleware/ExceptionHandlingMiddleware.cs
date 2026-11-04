using Availability.Api.DTOs.Responses;
using System.Text.Json;

namespace Availability.Api.Middleware
{
    /// <summary>
    /// Middleware que captura y maneja excepciones no controladas en el pipeline.
    /// Proporciona respuestas consistentes de error y facilita auditoría mediante CorrelationId.
    /// </summary>
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;
        private const string CorrelationIdItemName = "CorrelationId";

        /// <summary>
        /// Constructor que inyecta las dependencias necesarias.
        /// </summary>
        /// <param name="next">Delegado del siguiente middleware en la cadena</param>
        /// <param name="logger">Logger para registrar excepciones</param>
        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        /// <summary>
        /// Invoca el middleware. Captura excepciones y proporciona respuestas de error estándar.
        /// </summary>
        /// <param name="context">Contexto HTTP actual</param>
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                // Extraer CorrelationId del contexto
                string correlationId = context.Items[CorrelationIdItemName]?.ToString() ?? "unknown";

                // Loguear la excepción con CorrelationId para auditoría y debugging
                _logger.LogError(
                    ex,
                    "Excepción no controlada capturada | CorrelationId: {CorrelationId} | Mensaje: {ExceptionMessage}",
                    correlationId,
                    ex.Message
                );

                // Preparar respuesta de error estándar
                context.Response.ContentType = "application/json";
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;

                var errorResponse = new ErrorResponse(
                    code: "INTERNAL_SERVER_ERROR",
                    message: "Ha ocurrido un error interno en el servidor. Por favor, reintente más tarde.",
                    correlationId: correlationId,
                    details: ex.Message
                );

                // Serializar y enviar respuesta
                var jsonResponse = JsonSerializer.Serialize(errorResponse);
                await context.Response.WriteAsync(jsonResponse);
            }
        }
    }
}
