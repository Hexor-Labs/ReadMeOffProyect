# Lo que falta

Cinco servicios sin implementar. Esta página existe para que quien los retome
no tenga que reconstruir el contexto: la especificación funcional está en
`prompts-efcore-microservicios.md`, y aquí queda lo que se decidió mientras se
construían los cinco primeros y que conviene respetar para que el conjunto siga
sintiéndose de una sola mano.

## Antes de escribir ninguno

Lee `services/OrderService/` entero. Es la referencia: outbox en la misma
transacción, máquina de estados en el dominio, DTOs en el borde, cliente HTTP
con Polly, migración con `EnableTenantIsolation` y restricciones CHECK.
`services/TenantService/` es la versión mínima del mismo patrón.

Reutiliza siempre `packages/SharedKernel/`. Si algo parece que hay que escribir
otra vez, probablemente ya está ahí.

## Los cinco

### billing-service

Entidades `Invoice` y `PaymentTransaction`. Casos de uso
`GenerateMonthlyInvoice`, `ProcessPaymentWebhook` y `SuspendTenantOnOverdue`.

Tres cosas que no son negociables:

1. **Nunca se guarda un número de tarjeta.** Solo el `ProviderTransactionId`,
   que es un token del proveedor. Guardar un PAN mete el proyecto en
   cumplimiento PCI-DSS completo, inviable para este equipo.
2. **La firma del webhook se valida antes de mirar el cuerpo**, con HMAC-SHA256
   y comparación en tiempo constante (`CryptographicOperations.FixedTimeEquals`).
   Comparar con `==` sobre cadenas termina en cuanto encuentra una diferencia, y
   ese tiempo distinto deja adivinar la firma byte a byte. El endpoint es
   anónimo por necesidad —el proveedor no tiene JWT— así que la firma es lo
   único que lo protege.
3. **Idempotencia**: el proveedor reenvía webhooks. Índice único sobre
   `ProviderTransactionId`.

No añadas Hangfire: no está en `Directory.Packages.props`. Un `BackgroundService`
con temporizador basta, como el publicador de outbox.

### inventory-service

Entidades `StockItem` y `StockMovement`. Casos de uso `ReserveStock`,
`ConfirmStockDeduction`, `ReleaseReservedStock` y `CheckLowStock`.

El punto crítico es el mismo que en reservations-service: **`SELECT … FOR UPDATE`
dentro de una transacción explícita**. Copia el patrón de
`services/ReservationsService/Infrastructure/Persistence/ReservationRepository.cs`,
que ya resuelve el SQL crudo parametrizado y el ámbito de transacción.

Dos cantidades, no una: `QuantityAvailable` es lo que queda por vender y
`QuantityReserved` lo apartado pero no cobrado. Reservar mueve de una a otra;
confirmar descuenta de verdad; liberar lo devuelve. Cada movimiento deja fila en
`StockMovement` con cantidad anterior y nueva — es el libro mayor del almacén y
es lo que permite cuadrar cuando algo no cuadra.

`ConfirmStockDeduction` consume `order.paid`, que llega **al menos una vez**:
guarda el `OrderId` procesado con índice único o descontarás dos veces.

CHECK en la migración: `quantity_available >= 0` y `quantity_reserved >= 0`.

### notification-service

Entidades `NotificationTemplate` y `NotificationLog`. Es sobre todo un
**consumidor** de eventos, no un CRUD.

La trampa está en que un consumidor corre fuera de una petición HTTP: **no hay
tenant en el contexto** y los filtros globales dejarían las consultas a cero.
Antes de tocar la base de datos hay que abrir
`TenantContext.BeginScope(tenantIdDelEvento, correlationId)`. Es lo más fácil de
olvidar del servicio entero.

Patrón Strategy para los canales (`IEmailSender`, `ISmsSender`) con
implementaciones que escriban en el log, como `LoggingEventBusPublisher`.

Nunca registres el cuerpo del mensaje si lleva datos personales: destinatario
enmascarado y resultado, nada más.

### checkout-service

Entidades `Cart`, `CartItem` y `ShippingOption`. `Checkout` llama a
order-service por HTTP síncrono **con Polly** — copia
`services/OrderService/Infrastructure/Http/CatalogHttpClient.cs` y su registro en
`Program.cs`, que ya trae espera máxima, reintentos con espera creciente y
cortacircuitos.

`UnitPriceSnapshot`: el carrito guarda el precio del momento. Decide
explícitamente qué pasa si al pagar el precio cambió, y déjalo comentado.

`Checkout` tiene que ser idempotente: si el cliente pulsa dos veces, no pueden
salir dos órdenes.

### landing-builder-service

Entidades `LandingPage` y `LandingBlock`. Su endpoint público es el **único del
hub con tráfico anónimo alto**, y eso cambia tres cosas:

1. Límite de peticiones propio y más estricto que el global.
2. `ICacheProvider` con implementación en memoria, listo para Redis. La página
   cambia poco y se puede cachear con agresividad; la invalida publicar de nuevo.
3. Al ser anónimo **no hay tenant en el contexto**, así que el filtro global
   dejaría la consulta a cero. Mismo problema que notification-service y misma
   solución: abrir el ámbito una vez resuelto el slug.

Una página sin publicar debe responder igual que una inexistente. Distinguirlas
deja adivinar qué hay en borrador.

## Criterio para darlos por terminados

```bash
dotnet build HubNegocios.sln --nologo   # 0 errores, 0 avisos
dotnet test HubNegocios.sln --nologo    # todo en verde
```

`TreatWarningsAsErrors` está activo: un aviso rompe la compilación. Y añade el
proyecto y sus tests a `HubNegocios.sln`, o no entran en la verificación.
