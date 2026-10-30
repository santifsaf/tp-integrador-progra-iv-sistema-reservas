using Booking.Business.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Business.Workers;

// Job en segundo plano que corre ReservationExpirationService cada cierto intervalo.
//
// DECISIÓN: se usa un BackgroundService dentro del mismo proceso de Booking en vez de una
// herramienta externa (Hangfire, Quartz, un cron). Alcanza para el TP y no agrega
// infraestructura. Corre en todas las réplicas; el UPDATE condicional evita trabajo doble.
//
// El worker es singleton pero el repositorio (DbContext) es scoped, así que se crea un
// scope nuevo en cada vuelta.
internal sealed class ReservationExpirationWorker(
    IServiceScopeFactory scopes,
    IOptions<ReservationsOptions> options,
    ILogger<ReservationExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.ExpirationInterval);

        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ReservationExpirationService>();
                var count = await service.ExpireHeldAsync(stoppingToken);

                if (count > 0)
                {
                    logger.LogInformation("Job de vencimiento: {Count} reservas pasaron a Expired", count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Si la base no responde, no se cae el proceso: se reintenta en la próxima vuelta.
                logger.LogError(ex, "Falló el job de vencimiento; se reintenta en la próxima vuelta");
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
