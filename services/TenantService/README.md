# tenant-service

Da de alta negocios en el hub, los suspende y resuelve cuál corresponde a cada
subdominio o dominio propio.

Es el primer eslabón: hasta que no emite `tenant.created`, para el resto del
sistema ese negocio no existe.

## Lo que distingue a este servicio de los otros nueve

**No aplica el filtro global de tenant.** En el resto del hub el tenant es el
filtro de todo; aquí el tenant es la fila. Un filtro global comparando contra el
tenant del contexto —que en este servicio no existe— dejaría todas las consultas
a cero.

A cambio se protege de otra forma: sus endpoints son de plataforma y exigen rol
`PlatformAdmin`. La única excepción es `GET /api/tenants/resolve/{slugOdominio}`,
que es anónimo porque el gateway lo llama antes de que exista sesión alguna.

## Levantarlo en local

```bash
cp ../../.env.example ../../.env      # y rellena los valores
docker compose up -d postgres         # desde la raíz del repo
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=tenant_db;Username=hub;Password=..."
export Jwt__SigningKey="una-clave-de-al-menos-32-caracteres"
dotnet ef database update --project services/TenantService/TenantService.csproj
dotnet run --project services/TenantService/TenantService.csproj
```

Con Swagger en `http://localhost:5xxx/swagger` (solo en Development).

## Variables de entorno

| Variable | Obligatoria | Para qué |
|---|---|---|
| `ConnectionStrings__Default` | sí | Postgres. El servicio se conecta como `hub_app`, no como dueño |
| `Jwt__SigningKey` | sí | Firma de los tokens. Del vault, nunca del código — el servicio no arranca sin ella |
| `Jwt__Issuer`, `Jwt__Audience` | sí | Validación del token |
| `Outbox__PollInterval` | no | Cada cuánto se publica. Por defecto 5 s |
| `Outbox__BatchSize` | no | Eventos por vuelta. Por defecto 100 |
| `Outbox__MaxRetries` | no | Intentos antes de apartar el evento. Por defecto 5 |

## Correr los tests

```bash
dotnet test tests/TenantService.Tests/TenantService.Tests.csproj
```

22 tests de dominio y de casos de uso. No necesitan base de datos ni Docker: el
dominio no depende de EF Core, que es justo lo que compra la arquitectura
hexagonal.

## Endpoints

| Método | Ruta | Acceso |
|---|---|---|
| `POST` | `/api/tenants` | `PlatformAdmin` |
| `PATCH` | `/api/tenants/{id}/branding` | `PlatformAdmin`, `Owner` |
| `POST` | `/api/tenants/{id}/suspend` | `PlatformAdmin` |
| `GET` | `/api/tenants/resolve/{slugOdominio}` | anónimo |
| `GET` | `/health` | anónimo — liveness |
| `GET` | `/ready` | anónimo — readiness, comprueba Postgres |

## Eventos que emite

| Evento | Cuándo | Quién lo escucha |
|---|---|---|
| `tenant.created` | Alta de un negocio | Todos: es como se enteran de que existe |
| `tenant.suspended` | Suspensión | Todos: deben dejar de atenderlo |
| `tenant.branding_updated` | Cambio de identidad visual | landing-builder-service |

No consume ninguno todavía. Cuando billing-service emita
`tenant.payment_overdue`, este servicio tendrá que escucharlo para suspender.

## Runbook

**El servicio no arranca y el log dice «Falta Jwt:SigningKey».** Es a propósito:
preferimos no arrancar a arrancar aceptando cualquier token. Define la variable.

**`/ready` devuelve 503 pero `/health` responde.** Postgres no contesta. El
contenedor está vivo, así que el orquestador debe dejar de mandarle tráfico sin
reiniciarlo. Revisa la base y el límite de conexiones.

**Hay eventos con `dead_lettered_at` no nulo.** Agotaron los cinco intentos y
están apartados. No se pierden: mira `last_error`, arregla la causa y ponles
`retry_count = 0` para que el publicador los recoja de nuevo.

```sql
SELECT id, event_type, retry_count, last_error
FROM outbox_events
WHERE dead_lettered_at IS NOT NULL
ORDER BY id;
```
