# TP Integrador — Programación IV: Reserva de Asientos

Sistema de reserva de asientos basado en microservicios .NET 10.

## Arquitectura

| Servicio      | Arquitectura       | Proyectos                                | Rol en el flujo                                                           |
| ------------- | ------------------ | ---------------------------------------- | ------------------------------------------------------------------------- |
| Availability  | Clean Architecture | Api, Application, Domain, Infrastructure | Punto de entrada del usuario: consulta disponibilidad y solicita reservas |
| Booking       | N-Layer            | Api, Business, Data                      | Procesa las solicitudes, retiene o rechaza asientos y gestiona el pago    |
| Payment       | N-Layer            | Grpc, Business, Data                     | Simula el pago; expone un servicio gRPC                                   |
| Notifications | Worker             | Worker                                   | Notifica el resultado de la reserva                                       |
| Auth          | Keycloak           | —                                        | Login con GitHub y autenticación entre servicios                          |

Comunicación entre servicios:

- Availability → Booking: mensajes por **Azure Service Bus** (solicitud de reserva) y HTTP (consulta de estado).
- Booking → Payment: **gRPC**.
- Cada llamada entre servicios usa un token de Keycloak obtenido con _client credentials_.
- Cada microservicio tiene su propia base **PostgreSQL**; ningún servicio lee la base de otro.

> _Pendiente: diagrama general en `/docs`._

## Estructura del repositorio

```
/src
  /Availability     ← Clean Architecture
  /Booking          ← N-Layer
  /Payment          ← N-Layer + gRPC
  /Notifications    ← Worker
/contracts          ← .proto y contrato de mensajes del broker
/infra              ← docker-compose y configuración de Keycloak
/docs               ← diagramas y tabla de interpretación
/prompts            ← historial de prompts por integrante (Nombre_Apellido.md)
```

## Requisitos

- SDK de .NET 10
- Docker con Docker Compose
- Connection string de Azure Service Bus (la provee la cátedra)
- Client ID y Secret de la OAuth App de GitHub (pedir al grupo)

## Levantar el entorno

```bash
cp infra/.env.example infra/.env   # completar los valores pendientes
docker compose -f infra/docker-compose.yml up -d
```

Por ahora el docker compose levanta Keycloak en http://localhost:8081. Detalle de configuración, clients y cómo pedir un token: [`infra/README.md`](infra/README.md).

## Compilar y ejecutar en local

```bash
dotnet restore SeatReservation.sln
dotnet build SeatReservation.sln
```

Cada proceso en una terminal distinta:

```bash
dotnet run --project src/Availability/Availability.Api       # http://localhost:5100
dotnet run --project src/Booking/Booking.Api                 # http://localhost:5200
dotnet run --project src/Payment/Payment.Grpc                # http://localhost:5300
dotnet run --project src/Notifications/Notifications.Worker
```

Todos los hosts HTTP exponen `GET /health`.

## Sumar un servicio al docker-compose

Cada subgrupo aporta:

1. Un `Dockerfile` en `src/<Servicio>/`. El build context es la raíz del repo, para poder copiar `/contracts`.
2. Un bloque en `infra/docker-compose.yml` con el servicio y su base Postgres, vía PR.

Dentro de Docker, los servicios llegan a Keycloak en `http://keycloak:8081` y entre ellos por nombre de servicio (por ejemplo, `http://booking:8080`).

---

## Availability

> _A completar por el subgrupo Availability (Feli, Rebeca, Luciana)._
>
> - Endpoints expuestos y ejemplos de request/response
> - Capas y responsabilidades (Domain, Application, Infrastructure, Api)
> - Mensaje que publica en Azure Service Bus
> - Variables de configuración necesarias
> - Dónde el sistema se vuelve eventualmente consistente

## Booking

> _A completar por el subgrupo Booking (Luis, Gian, Santi)._
>
> - Estados de la reserva y transiciones
> - Consumo de mensajes e idempotencia por id de solicitud
> - Control de concurrencia sobre los asientos
> - Job de vencimiento de reservas retenidas
> - Endpoint de consulta de estado y llamada gRPC a Payment
> - Variables de configuración necesarias

## Payment

> _A completar por el subgrupo Payment (Agus, Juan, Kevin)._
>
> - Contrato `.proto` y RPCs expuestos
> - Resultados posibles del pago simulado (éxito, fallo, no disponible)
> - Variables de configuración necesarias

## Notifications

> _A completar..._
>
> - Qué eventos consume y qué notifica
> - En qué parte del diagrama se sumaría
> - Variables de configuración necesarias

## Happy path y flujo de fallo

> _A completar (punto 3j)._
>
> - Pasos para ejecutar el happy path de punta a punta
> - Comportamiento cuando falta un componente

---

## Trabajo en equipo

- Ramas: personal (`feat/<servicio>-<nombre>`) → subgrupo (`feat/<servicio>`) → `develop` → `main`. Cada salto pasa por un PR con revisión.
- Commits con Conventional Commits y el servicio como scope: `feat(booking): crear reserva retenida`.
- `/contracts` cambia solo vía ramas `chore/contracts-*` con aprobación de los subgrupos involucrados.
- No subir credenciales ni connection strings: van en `infra/.env` o `appsettings.Local.json` (ambos en `.gitignore`).
- Cada integrante registra sus prompts en `/prompts/Nombre_Apellido.md`.
