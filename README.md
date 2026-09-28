# Hub de Negocios Multi-Tenant

Plataforma SaaS multi-tenant en microservicios sobre **.NET 8 / C# / EF Core /
PostgreSQL**, con arquitectura hexagonal y comunicación por eventos.

La especificación funcional está en [`ExplicacionTec.md`](ExplicacionTec.md).
Las decisiones de arquitectura y los diagramas, en
[`docs/arquitectura.md`](docs/arquitectura.md).

## Estructura

```
hub-negocios/
├── packages/SharedKernel/     infraestructura transversal de los 10 servicios
├── services/                  un microservicio por carpeta
├── tests/                     un proyecto de pruebas por servicio
├── docker/                    arranque de Postgres (bases y roles)
├── docs/                      arquitectura y diagramas
└── docker-compose.yml         entorno local completo
```

Monorepo y no diez repositorios, siguiendo lo que recomienda `ExplicacionTec.md`
para un equipo de tres: un solo sitio donde buscar, un solo CI que configurar
bien, y cambios que cruzan servicios en un único commit.

## Arrancar en local

```bash
cp .env.example .env          # rellena los valores; el .env real nunca se sube
docker compose up -d          # Postgres con una base por servicio + RabbitMQ

dotnet restore
dotnet build
dotnet test
```

Para levantar un servicio concreto contra esa infraestructura:

```bash
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=tenant_db;Username=hub;Password=..."
export Jwt__SigningKey="una-clave-de-al-menos-32-caracteres"
dotnet run --project services/TenantService/TenantService.csproj
```

Cada servicio tiene su propio README con sus variables y su runbook.

## Las decisiones que conviene conocer antes de tocar nada

### El tenant entra una sola vez, en el borde

`TenantResolutionMiddleware` lee `tenant_id` del JWT **ya validado** y abre un
ámbito de `TenantContext` que dura toda la petición. De ahí en adelante nadie
vuelve a pasar el tenant a mano: lo recogen solos los filtros de EF Core, los
interceptores y la outbox.

Es la «regla de oro no negociable» de `ExplicacionTec.md`, y la razón es
práctica: el día que a alguien se le olvide pasar el parámetro, la consulta
devolvería datos de otro cliente.

### Tres capas de aislamiento, porque una sola falla

| Capa | Qué cubre | Por dónde se escapa |
|---|---|---|
| Filtro global de EF Core | Consultas LINQ | `IgnoreQueryFilters()`, SQL crudo |
| Interceptor de escritura | Que no se escriba en otro tenant | Lecturas |
| Row-Level Security | Todo, dentro del motor de Postgres | Si la app se conecta como **dueña** de la tabla |

Esa última casilla es la más importante del cuadro. El dueño de una tabla se
salta RLS por defecto: si el servicio se conectara con el usuario dueño, las
políticas estarían escritas pero no se aplicarían nunca. Por eso
`docker/postgres-init.sql` crea dos roles —`hub` dueño, `hub_app` para la
aplicación— y los servicios usan siempre el segundo.

### Outbox: un evento es una fila más de la transacción

Un caso de uso tiene que guardar el cambio y avisar al resto del sistema, y no
hay forma de hacer esas dos cosas atómicamente contra dos sistemas distintos.
Escribiendo el evento en la misma transacción, o pasan las dos cosas o no pasa
ninguna.

El precio es que la entrega es **al menos una vez**: los consumidores deben ser
idempotentes. La alternativa sería perder eventos, que es peor.

### Un shared kernel, no diez copias

La checklist del documento de prompts pide comparar los `Program.cs` de varios
servicios y desconfiar si divergen. Con un paquete compartido no pueden
divergir: es el mismo código. Ahí vive infraestructura transversal, **nunca**
lógica de negocio — un shared kernel que empieza a saber de órdenes o reservas
es el primer paso para volver a tener un monolito repartido en diez procesos.

## Convenciones

- **Tablas** en `snake_case`, mapeadas con `.HasColumnName()`.
- **Enums** guardados como texto (`.HasConversion<string>()`). Un `2` en una
  columna no dice nada a las tres de la mañana, y reordenar el enum en C#
  reinterpretaría en silencio todas las filas ya guardadas.
- **Dinero** en `decimal` / `numeric(18,2)`. Nunca `double`: en coma flotante
  binaria 0,1 no es 0,1 y los céntimos dejan de cuadrar.
- **Fechas** siempre en UTC.
- **DTOs** en los endpoints, nunca entidades de dominio. Si el endpoint aceptara
  la entidad, el cliente podría mandar `"status": "Paid"` en el JSON.
- **Commits** con [Conventional Commits](https://www.conventionalcommits.org/).
- **Ramas** `feature/nombre-corto` con PR contra `main`.

## Formato de error

Uno solo en todos los servicios, para que el frontend no aprenda diez formas de
saber que algo falló:

```json
{
  "error": {
    "code": "ORDER_ITEM_UNAVAILABLE",
    "message": "Ya no están disponibles: Botella premium",
    "details": {},
    "trace_id": "0af7651916cd43dd8448eb211c80319c"
  }
}
```

El `code` es contrato estable y el `message` es para personas. El `trace_id`
viaja entre servicios en la cabecera `X-Correlation-Id` y se devuelve siempre,
también en los errores: es el número que un cliente puede leernos por teléfono.

## Pruebas

```bash
dotnet test                                                  # todo
dotnet test tests/OrderService.Tests/OrderService.Tests.csproj  # un servicio
```

Los tests de dominio y de casos de uso **no necesitan base de datos ni Docker**:
el dominio no depende de EF Core. Eso es lo que compra la arquitectura
hexagonal, y por eso los dobles están escritos a mano en vez de con una librería
de simulación.

Los tests de integración con Testcontainers **sí necesitan Docker** y están
marcados para saltarse cuando no lo hay.
