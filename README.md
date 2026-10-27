# TP Integrador — Programación IV

Estructura base del sistema de reserva de asientos en .NET 10. No implementa funcionalidades de negocio.

## Proyectos

- Availability: Api, Application, Domain e Infrastructure (Clean Architecture).
- Booking: Api, Business y Data (N-Layer).
- Payment: Grpc, Business y Data (N-Layer).
- Notifications: Worker.

Las bibliotecas están vacías. Los hosts HTTP incluyen únicamente `GET /health`; Payment tiene gRPC registrado pero todavía no define RPCs. Notifications inicia y espera el cierre de la aplicación.

Las referencias internas reflejan las arquitecturas exigidas. No hay referencias de proyecto entre microservicios.

## Abrir y compilar

Requiere SDK de .NET 10. Abrir `SeatReservation.sln` en Visual Studio o ejecutar:

```bash
dotnet restore SeatReservation.sln
dotnet build SeatReservation.sln
```

## Ejecutar cada proceso

Ejecutar cada comando en una terminal distinta:

```bash
dotnet run --project src/Availability/Availability.Api
dotnet run --project src/Booking/Booking.Api
dotnet run --project src/Payment/Payment.Grpc
dotnet run --project src/Notifications/Notifications.Worker
```

Comprobaciones HTTP locales: Availability en http://localhost:5100/health, Booking en http://localhost:5200/health y Payment en http://localhost:5300/health. Este último comprueba el host HTTP; no es una operación gRPC.

Los puertos son valores iniciales de desarrollo y pueden cambiarse. No se eligió todavía UI (MVC o frontend separado), base de datos, ORM, proveedor de autorización ni contratos. No se incorporan Docker, conexiones al broker ni modelos del dominio.

No subir credenciales o connection strings al repositorio. La consigna requiere conservar el historial de prompts por integrante durante el desarrollo.
