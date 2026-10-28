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
