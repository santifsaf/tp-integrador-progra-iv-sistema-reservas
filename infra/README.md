# Keycloak

Autenticación del sistema: login de usuarios con GitHub y tokens entre microservicios (client credentials).
Para la visión general del proyecto, ver el [README principal](../README.md).

## Configuración proveedor externo (GitHub)

Se debe configurar la OAuth App de GitHub (Settings > Developer settings > OAuth Apps)
De aquí se obtienen el Client y Secret.

**Homepage URL** tiene que ser:

```
http://localhost:8081
```

**Redirect URI** tiene que ser:

```
http://localhost:8081/realms/seat-reservation/broker/github/endpoint
```

## Levantar el entorno

```bash
cp infra/.env.example infra/.env
# Dentro de .env, completar los parametros GITHUB_CLIENT_ID y GITHUB_CLIENT_SECRET (Pedir por el grupo o a Feli)
docker compose -f infra/docker-compose.yml up -d
```

- Keycloak: http://localhost:8081 (consola de admin con `KC_ADMIN` / `KC_ADMIN_PASSWORD`, realm `master`)
- Realm del TP: `seat-reservation`, importado desde `keycloak/import/seat-reservation-realm.json`

Keycloak no usa base externa: guarda todo dentro del contenedor. Al recrearlo arranca limpio y reimporta
el realm, así que los usuarios que entraron con GitHub se pierden. Para aplicar cambios del JSON:
`docker compose -f infra/docker-compose.yml up -d --force-recreate keycloak`.


## Postgres de Booking

Cada microservicio tiene su propia base PostgreSQL en su propio contenedor. El de Booking es el servicio `booking-db`:

```bash
docker compose -f infra/docker-compose.yml up -d --wait booking-db
```

- **Puerto:** se publica en `localhost:5432`. Se puede cambiar con `BOOKING_DB_PORT` en `infra/.env`;
  entre contenedores siempre es `booking-db:5432`. Cada servicio debe usar un puerto distinto del host
  (sugerido: Availability 5433, Payment 5434, Notifications 5435).
- **Credenciales:** usuario, contraseña y base valen `booking` (`BOOKING_DB_USER`, `BOOKING_DB_PASSWORD`,
  `BOOKING_DB_NAME`). Son valores de desarrollo local, no secretos.
- **Bases:** además de `booking`, la primera vez que se crea el volumen el script
  `postgres/booking/init/` crea `booking_tests`, que usan los tests de `Booking.Data.Tests`.
- **Datos:** viven en el volumen `booking-db-data` y sobreviven a `docker compose down`.
  Para empezar de cero (y que vuelva a correr el script de inicialización): `docker compose -f infra/docker-compose.yml down -v`.

Connection string desde la máquina (para `dotnet ef`, los tests y la API corriendo fuera de Docker):

```
Host=localhost;Port=5432;Database=booking;Username=booking;Password=booking
```

Para la API, guardarla en user-secrets:

```bash
dotnet user-secrets set "ConnectionStrings:Booking" "Host=localhost;Port=5432;Database=booking;Username=booking;Password=booking" --project src/Booking/Booking.Api
```

### Migraciones y tests

```bash
dotnet ef database update --project src/Booking/Booking.Data
dotnet test src/Booking/Booking.Data.Tests
```

Los tests aplican las migraciones solos sobre `booking_tests` y requieren `booking-db` levantado. Para
ejecutar el resto sin la base: `dotnet test --filter "Category!=Integration"`.

Al incorporar Booking al compose, configurar en su servicio (el host es el nombre del servicio, no `localhost`):

```yaml
environment:
  ConnectionStrings__Booking: Host=booking-db;Port=5432;Database=booking;Username=booking;Password=booking
depends_on:
  booking-db:
    condition: service_healthy
```

## Postgres de Payment

La base de Payment es el servicio `payment-db`. Como el resto, es una base propia en su propio contenedor:
ningún otro servicio la lee.

```bash
docker compose -f infra/docker-compose.yml up -d --wait payment-db
```

- **Puerto:** se publica en `localhost:5434`. Se puede cambiar con `PAYMENT_DB_PORT` en `infra/.env`;
  entre contenedores siempre es `payment-db:5432`.
- **Credenciales:** usuario, contraseña y base valen `payment` (`PAYMENT_DB_USER`, `PAYMENT_DB_PASSWORD`,
  `PAYMENT_DB_NAME`). Son valores de desarrollo local, no secretos.
- **Datos:** viven en el volumen `payment-db-data` y sobreviven a `docker compose down`.
  Para empezar de cero: `docker compose -f infra/docker-compose.yml down -v`.

Connection string desde la máquina (para `dotnet ef` y para la API corriendo fuera de Docker):

```
Host=localhost;Port=5434;Database=payment;Username=payment;Password=payment
```

Payment.Grpc la lee de la clave `ConnectionStrings:Payment`. Para guardarla en user-secrets:

```bash
dotnet user-secrets set "ConnectionStrings:Payment" "Host=localhost;Port=5434;Database=payment;Username=payment;Password=payment" --project src/Payment/Payment.Grpc
```

Si el comando avisa que falta el `UserSecretsId`, correr antes
`dotnet user-secrets init --project src/Payment/Payment.Grpc`.

### Migraciones

`dotnet ef` toma la cadena de la variable de entorno `PAYMENT_DB_CONNECTION_STRING` (si falta, falla con un mensaje claro):

```powershell
# PowerShell
$env:PAYMENT_DB_CONNECTION_STRING = "Host=localhost;Port=5434;Database=payment;Username=payment;Password=payment"
```

```bash
# Bash
export PAYMENT_DB_CONNECTION_STRING="Host=localhost;Port=5434;Database=payment;Username=payment;Password=payment"
```

```bash
dotnet ef database update --project src/Payment/Payment.Data
```

La base debe ser nueva o tener ya el historial de migraciones. Más detalle en
[`src/Payment/Payment.Data/README.md`](../src/Payment/Payment.Data/README.md).

### Tests

```bash
dotnet test src/Payment/Payment.Tests
```

Las pruebas de integración del repositorio levantan su propio PostgreSQL temporal con Testcontainers:
solo necesitan Docker en ejecución, no `payment-db`.

### Secrets

- `PAYMENT_CLIENT_SECRET` (en `infra/.env`, por defecto `payment-secret`) es el secret del client `payment` en Keycloak.
- Payment valida los tokens que recibe: `aud=payment` y solo acepta llamadas del client `booking`.
  Quien llama a Payment (Booking) usa su propio secret, `BOOKING_CLIENT_SECRET`.

Al incorporar Payment al compose, configurar en su servicio (el host es el nombre del servicio, no `localhost`):

```yaml
environment:
  ConnectionStrings__Payment: Host=payment-db;Port=5432;Database=payment;Username=payment;Password=payment
depends_on:
  payment-db:
    condition: service_healthy
```

## Login con GitHub

La pantalla de login muestra un botón por cada identity provider habilitado (por ahora, GitHub).
El registro con usuario y contraseña está deshabilitado: el formulario solo sirve para el usuario de prueba.
Para probar: http://localhost:8081/realms/seat-reservation/account

## Clients

| Client          | Tipo               | Para qué                  | `aud` del token |
| --------------- | ------------------ | ------------------------- | --------------- |
| `web`           | público, PKCE      | login del usuario final   | `availability`  |
| `availability`  | client credentials | llamar a Booking por HTTP | `booking`       |
| `booking`       | client credentials | llamar a Payment por gRPC | `payment`       |
| `payment`       | client credentials | —                         | —               |
| `notifications` | client credentials | —                         | —               |

Todos los usuarios nuevos (incluidos los de GitHub) reciben el rol de realm `user`.

## Pedir un token

Servicio a servicio (client credentials):

```bash
curl -s http://localhost:8081/realms/seat-reservation/protocol/openid-connect/token \
  -d grant_type=client_credentials -d client_id=availability -d client_secret=availability-secret
```

Usuario de prueba, sin navegador (solo para scripts y tests):

```bash
curl -s http://localhost:8081/realms/seat-reservation/protocol/openid-connect/token \
  -d grant_type=password -d client_id=web -d username=tester -d password=tester
```

## Validar el JWT desde un servicio

- Issuer: `http://localhost:8081/realms/seat-reservation` (siempre el mismo, incluso dentro de Docker).
- Desde un contenedor, la metadata está en
  `http://keycloak:8081/realms/seat-reservation/.well-known/openid-configuration`.
- Audience: el `clientId` del servicio que recibe la llamada (por ejemplo, Booking valida `aud=booking`).

Al incorporar Booking al compose, configurar estas variables en su servicio:

```yaml
environment:
  Authentication__Authority: http://localhost:8081/realms/seat-reservation
  Authentication__MetadataAddress: http://keycloak:8081/realms/seat-reservation/.well-known/openid-configuration
  Authentication__RequireHttpsMetadata: "false"
```

`Authority` conserva el issuer de los tokens; `MetadataAddress` usa el nombre accesible
desde la red de Docker. El Keycloak del compose ya habilita el backchannel dinámico
para publicar también una dirección interna de las claves de firma (`jwks_uri`).
Permitir metadata por HTTP es una configuración explícita del entorno local del TP.
Fuera de Development, Booking exige HTTPS por defecto y mantiene la validación de
issuer, audiencia, vencimiento y firma. El compose actual levanta Keycloak y el Postgres de Booking.
