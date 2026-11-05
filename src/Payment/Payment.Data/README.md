# Payment.Data

Capa de datos del servicio Payment: entidades, `PaymentDbContext`, repositorio y migraciones de EF Core (PostgreSQL).

## Migraciones

La cadena de conexión se lee de la variable de entorno `PAYMENT_DB_CONNECTION_STRING`. Si falta, `dotnet ef` falla con un mensaje claro. No hay credenciales escritas en el código.

### 1. Definir la cadena de conexión

PowerShell:

```powershell
$env:PAYMENT_DB_CONNECTION_STRING = "Host=localhost;Port=5434;Database=payment;Username=payment;Password=payment"
```

Bash:

```bash
export PAYMENT_DB_CONNECTION_STRING="Host=localhost;Port=5434;Database=payment;Username=payment;Password=payment"
```

Los valores del ejemplo corresponden al servicio `payment-db` del `docker-compose`.

### 2. Aplicar la migración

Desde la raíz del repositorio:

```
dotnet ef database update --project src/Payment/Payment.Data
```

### Importante

La base debe ser nueva o tener ya el historial de migraciones.
### Crear una migración nueva

```
dotnet ef migrations add NombreDeLaMigracion --project src/Payment/Payment.Data
```
(No necesita una base en ejecución, pero la variable `PAYMENT_DB_CONNECTION_STRING` debe estar definida.)