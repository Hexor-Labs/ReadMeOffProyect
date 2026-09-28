# Arquitectura

Diagrama en Mermaid y dentro del repo, no un PDF que nadie vuelve a abrir
(ExplicacionTec.md, apartado 8.8).

```mermaid
flowchart TB
    Front["Frontend / BFF"] --> GW["API Gateway"]

    GW --> Tenant["tenant-service"]
    GW --> Identity["identity-service"]
    GW --> Catalog["catalog-service"]
    GW --> Order["order-service"]
    GW --> Billing["billing-service"]
    GW --> Checkout["checkout-service"]
    GW --> Landing["landing-builder-service"]

    Order -.->|"HTTP síncrono + Polly"| Catalog
    Checkout -.->|"HTTP síncrono + Polly"| Order

    Tenant --> Bus[["Event Bus"]]
    Identity --> Bus
    Catalog --> Bus
    Order --> Bus
    Billing --> Bus

    Bus --> Reservations["reservations-service"]
    Bus --> Inventory["inventory-service"]
    Bus --> Notification["notification-service"]
    Bus --> Tenant

    subgraph BD["Una base por servicio"]
        direction LR
        DB1[("tenant_db")]
        DB2[("identity_db")]
        DB3[("catalog_db")]
        DB4[("order_db")]
        DB5[("reservations_db")]
    end

    Tenant --- DB1
    Identity --- DB2
    Catalog --- DB3
    Order --- DB4
    Reservations --- DB5
```

## Las tres capas de aislamiento entre tenants

No es una sola medida, son tres, y cada una tapa lo que dejan pasar las otras.
Esto importa porque **una sola falla**: los filtros de EF Core se saltan con
`IgnoreQueryFilters()` o con SQL crudo, y RLS no sirve de nada si la aplicación
se conecta como dueña de las tablas.

| Capa | Dónde vive | Qué cubre | Qué NO cubre |
|---|---|---|---|
| Filtro global de EF Core | `ModelBuilderExtensions.ApplyTenantFilters` | Toda consulta LINQ | `IgnoreQueryFilters()`, `FromSqlRaw`, SQL a mano |
| Interceptor de escritura | `TenantAssignmentInterceptor` | Que no se escriba en otro tenant | Lecturas |
| Row-Level Security | Políticas en Postgres | Todo, incluido SQL crudo | Nada, si la app se conecta como dueña de la tabla |

```mermaid
sequenceDiagram
    participant C as Cliente
    participant M as TenantResolutionMiddleware
    participant U as Caso de uso
    participant I as TenantConnectionInterceptor
    participant P as Postgres

    C->>M: petición con JWT
    Note over M: lee tenant_id del token YA validado
    M->>M: TenantContext.BeginScope(tenant)
    M->>U: sigue la petición
    U->>I: abre conexión
    I->>P: SELECT set_config('app.tenant_id', $1, false)
    U->>P: SELECT ... (filtro de EF) 
    Note over P: RLS vuelve a filtrar por app.tenant_id
    P-->>U: solo filas del tenant
```

El tenant entra al sistema **una sola vez**, en el borde, y de ahí en adelante
viaja solo en el contexto de ejecución. Nunca es un parámetro que alguien tenga
que acordarse de pasar: el día que se olvide, la consulta devolvería datos de
otro cliente.

## Outbox: por qué existe

Un caso de uso tiene que hacer dos cosas —guardar el cambio y avisar al resto
del sistema— y no hay forma de hacerlas atómicamente contra dos sistemas
distintos.

```mermaid
sequenceDiagram
    participant U as Caso de uso
    participant DB as Postgres
    participant W as OutboxPublisherService
    participant B as Event Bus

    rect rgb(240, 240, 250)
        Note over U,DB: una sola transacción
        U->>DB: INSERT orden
        U->>DB: INSERT outbox_events
    end

    loop cada 5 s
        W->>DB: SELECT ... FOR UPDATE SKIP LOCKED
        W->>B: publica
        W->>DB: marca published = true
    end
```

Si el proceso muere entre publicar y marcar, el evento se reenvía: la entrega
es **al menos una vez** y los consumidores tienen que ser idempotentes. La
alternativa —marcar antes de publicar— sería «como mucho una vez», es decir,
perder eventos, que es bastante peor.

El `SKIP LOCKED` es lo que permite que varias instancias del servicio publiquen
a la vez sin pisarse: cada una se lleva las filas que bloquea y las demás pasan
de largo en vez de esperar su turno.

## Por qué hay un SharedKernel

La checklist del documento de prompts pide comparar los `Program.cs` de varios
servicios y desconfiar si divergen. Un paquete compartido convierte esa revisión
manual en algo que no puede fallar: no divergen porque es literalmente el mismo
código.

Lo que vive ahí es infraestructura transversal —tenancy, outbox, auditoría,
formato de error, arranque—, nunca lógica de negocio. Un shared kernel que
empieza a saber de órdenes o de reservas es el primer paso para volver a tener
un monolito, solo que repartido en diez procesos.
