# Historial de prompts — Felipe Bertocchi

## 1. Keycloak con login por GitHub

- **Fecha:** 28/10/2026
- **Herramienta:** Claude Code (Claude Opus 5.5)
- **Rama:** `feat/auth`

### Prompt

> Levanta Keycloak con docker compose en `/infra` en el puerto 8081
>
> El login de usuarios vamos a hacerlo con GitHub como identity provider (ya tengo el client id y el secret). Más adelante capaz sumo otro providor. Login clasico con Usuario y contraseña no hace falta, salvo para usuario de prueba
>
> Además cada microservicio (availability, booking, payment y notifications) necesita su client para autenticarse con los otros servicios. Availability llama a Booking y Booking a Payment por gRPC
>
> Dejá la config del realm en un JSON y un README breve que explique como levantarlo y como pedir un token. Agrega tambien un usuario de prueba para no tener que entrar siempre con GitHub.

## 2. README general del proyecto

- **Fecha:** 28/10/2026
- **Herramienta:** Claude Code (Claude Opus 5.5)
- **Rama:** `feat/auth`

### Prompt

> Arma un README en el root del proyecto con la arquitectura general, la estructura del repo y con instrucciones para levantar el entorno. Deja una sección por subgrupo con lo que tienen que completar sobre su servicio. El broker es Azure Service Bus (la connection string la pasa el profe). El README de `infra` dejalo solo para Keycloak.
