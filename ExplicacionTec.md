1. Arquitectura general (resumen ejecutable)
                        ┌─────────────────────┐
                        │   API Gateway / BFF  │  ← único punto de entrada público
                        └──────────┬───────────┘
                                   │
                 ┌─────────────────┼─────────────────┐
                 │                 │                 │
          ┌──────▼─────┐   ┌───────▼──────┐   ┌──────▼──────┐
          │ Core Svcs  │   │ Capability   │   │ Capability  │
          │ (torso)    │   │ Svc: Reservas│   │ Svc: Shop   │
          └──────┬─────┘   └───────┬──────┘   └──────┬──────┘
                 │                 │                 │
                 └─────────────────┼─────────────────┘
                                   │
                        ┌──────────▼───────────┐
                        │  Event Bus (Kafka/    │
                        │  RabbitMQ) + Outbox   │
                        └───────────────────────┘
Core (torso): tenant-service, identity-service, billing-service, catalog-service, order-service, notification-service.
Capability services (extremidades): reservations-service, inventory-service, landing-builder-service, checkout-service.
Cada servicio = hexagonal, DB propia, se comunica por eventos (async) y, cuando hace falta respuesta inmediata, por API síncrona interna (REST/gRPC) detrás del gateway.
2. Multi-tenancy: la decisión concreta para ustedes
Con equipo pequeño y negocio en validación, no empiecen con "DB por tenant" (es operacionalmente muy caro: migraciones × N tenants, backups × N, monitoreo × N).

Recomendación concreta: una sola base de datos por servicio, tabla compartida + tenant_id + Row-Level Security (RLS) en Postgres.

ALTER TABLE reservations ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON reservations
  USING (tenant_id = current_setting('app.tenant_id')::uuid);

-- En cada conexión, antes de cualquier query:
SET app.tenant_id = '<uuid-del-tenant-autenticado>';
Esto da aislamiento casi tan fuerte como DB separada, sin el costo operativo. Migran a schema-per-tenant o DB-per-tenant solo si: (a) un cliente enterprise exige aislamiento físico contractualmente, o (b) un tenant específico satura recursos y afecta a otros (noisy neighbor).

Regla de oro no negociable: el tenant_id se resuelve UNA VEZ en el borde (middleware HTTP o al consumir el evento) y viaja en el contexto de ejecución — nunca como parámetro que cada dev tiene que acordarse de pasar. Si se les olvida en un query, RLS los salva.

3. Seguridad — nivel profesional
Esto es lo que más rápido hunde un SaaS B2B/B2C si se hace mal. Sin excusas aquí.

3.1 Autenticación y autorización
OIDC/OAuth2 para auth, no reinventen JWT a mano. Usen un proveedor (Auth0, Keycloak self-hosted, AWS Cognito, o Supabase Auth) — con 2 backend, no construyan su propio auth server, es la trampa clásica que consume meses.
JWT de acceso de corta duración (15 min) + refresh token rotativo almacenado en httpOnly, Secure, SameSite=Strict cookie (nunca en localStorage — vulnerable a XSS).
RBAC (Role-Based) para empezar: owner, admin, staff, customer. Si más adelante necesitan permisos finos por recurso (ej. "este staff solo ve mesas VIP"), evolucionan a ABAC (Attribute-Based).
El JWT debe llevar tenant_id y role firmados — así cada microservicio valida autorización sin ir a preguntarle a nadie más (stateless).
3.2 Encriptado
Dato	En tránsito	En reposo
Toda comunicación externa	TLS 1.3 obligatorio (HTTPS)	—
Comunicación interna entre servicios	mTLS si el orquestador lo soporta fácil (Kubernetes + service mesh); si no, al menos red privada + TLS	—
Contraseñas	—	bcrypt o argon2 (nunca MD5/SHA1 solos)
Datos sensibles (tarjetas, datos personales)	TLS	Encriptación a nivel de columna (pgcrypto) o delegar a proveedor de pagos (ver 3.4)
Backups	TLS al transferir	Encriptados en el bucket/almacenamiento (AES-256, la mayoría de clouds lo dan por defecto)
3.3 Gestión de secretos
NUNCA secrets en el código ni en .env commiteado (ver sección Git, 8.3).
Usen un vault: mínimo Doppler o AWS Secrets Manager / GCP Secret Manager; si quieren algo gratis y simple para empezar, Infisical.
Rotación de credenciales de DB y API keys cada 90 días como política mínima.
3.4 Pagos — no toquen datos de tarjeta directamente
Con ecommerce en el mix, jamás almacenen ni procesen números de tarjeta en su propia infra (eso los mete en cumplimiento PCI-DSS completo, inviable para 2 backend). Usen tokenización vía Stripe, MercadoPago o Wompi (más común en Colombia): el frontend manda los datos directo al proveedor, ustedes solo reciben un token/payment_intent_id.

3.5 OWASP Top 10 — checklist mínimo
 Rate limiting en el API Gateway (ej. 100 req/min por IP/tenant) — previene brute force y DoS básico.
 Validación de input en el borde (schema validation: Zod, Joi, class-validator) — nunca confiar en el payload del front.
 Sanitización contra SQL injection (usen ORM/query builder parametrizado — Prisma, TypeORM, Drizzle; nunca concatenar strings SQL).
 CORS configurado explícito por dominio, no *.
 Headers de seguridad: Content-Security-Policy, X-Frame-Options, Strict-Transport-Security (usen helmet si es Node).
 Logs de auditoría para acciones sensibles (login, cambios de rol, pagos, borrado de datos) — quién, qué, cuándo, desde dónde.
 Dependencias: npm audit / dependabot activo en CI.
3.6 Cumplimiento legal (esto casi nadie lo pone y les puede caer una multa)
Si manejan datos de colombianos: Ley 1581 de 2012 (Habeas Data) — necesitan política de tratamiento de datos publicada y consentimiento explícito al registrar usuarios.
Si algún tenant tiene clientes en la UE: piensen en GDPR desde ya (derecho al olvido = endpoint para borrar datos de un usuario en cascada entre todos los microservicios — diséñenlo desde el modelo de datos, es doloroso agregarlo después).
Términos de servicio y política de privacidad por tenant si el hub va a tener marca blanca (white-label) — legal, no técnico, pero bloquea el lanzamiento si no está.
4. Elasticidad / Escalabilidad
Nivel MVP (lo que necesitan para lanzar):

Contenedores stateless (cualquier instancia puede atender cualquier request) para poder escalar horizontalmente sin dolor.
Autoscaling básico por CPU/memoria (Kubernetes HPA, o si usan algo más simple como Railway/Render/Fly.io, autoscaling nativo de la plataforma).
Connection pooling a la DB (PgBouncer) — con múltiples instancias de un servicio, sin esto agotan conexiones de Postgres rápido.
Nivel escala (cuando tengan tráfico real):

Particionamiento de la tabla más grande por tenant_id (Postgres table partitioning) cuando un servicio empiece a sufrir con millones de filas.
Cache distribuido (Redis) para catálogo/lecturas frecuentes — no cacheen datos transaccionales de pagos.
Circuit breakers entre servicios (ej. librería opossum en Node) para que si reservations-service cae, no tumbe en cascada a order-service.
Colas con backpressure — si el consumidor de eventos se satura, que el broker haga buffer, no que el productor se caiga.
5. Contenedores y orquestación
Para el tamaño de su equipo, mi recomendación honesta: no salten directo a Kubernetes self-managed, es una carga operativa brutal para 2 backend sin SRE dedicado.

Ruta recomendada:

Docker para cada microservicio (obligatorio, no negociable — reproducibilidad).
Docker Compose para desarrollo local (todos los servicios + Postgres + Kafka/RabbitMQ levantando con un comando).
Para producción: Kubernetes gestionado (GKE Autopilot, EKS Fargate, o DigitalOcean Kubernetes) que les quita gran parte de la carga operativa, o una plataforma tipo Railway / Render / Fly.io que abstrae todo esto si quieren lanzar rápido y aún no tienen la complejidad que justifique K8s puro.
# Ejemplo mínimo por servicio (Node/TS)
FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM node:20-alpine
WORKDIR /app
COPY --from=build /app/dist ./dist
COPY --from=build /app/node_modules ./node_modules
USER node
EXPOSE 3000
CMD ["node", "dist/main.js"]
Nota el USER node — nunca corran contenedores como root en producción.

6. DBs separadas — convención por servicio
Cada microservicio = su propia base de datos (aunque compartan el mismo servidor Postgres físico al inicio para ahorrar costos). Nunca que dos servicios lean/escriban la misma tabla directamente — si order-service necesita datos de catalog-service, los pide por API o los tiene replicados vía evento (read model local), nunca hace JOIN cruzando bases.

postgres-server/
├── tenant_db
├── identity_db
├── billing_db
├── catalog_db
├── order_db
├── reservations_db
└── inventory_db
Migraciones: cada servicio maneja las suyas (Prisma Migrate, Flyway, etc.), versionadas en su propio repo/carpeta. Nunca una migración corre contra la DB de otro servicio.

7. Formatos de conexión con el Frontend
Con un frontend que "solo sabe de front" (no de arquitectura distribuida), esto es crítico: el frontend nunca debe hablar directo con 6 microservicios. Eso lo condena a manejar 6 formatos de error distintos, 6 estrategias de auth, etc.

7.1 BFF (Backend For Frontend) — recomendado
Un único servicio (o el mismo API Gateway con lógica de agregación) que el frontend consume, y que internamente orquesta las llamadas a los microservicios. El front solo conoce un contrato.

Frontend  →  BFF (REST u OpenAPI)  →  microservicios internos
7.2 Formato de API — REST + OpenAPI
Para este equipo, REST con especificación OpenAPI 3.0 es más simple de operar que GraphQL (GraphQL brilla con muchos clientes con necesidades de datos muy distintas; para un equipo chico agrega complejidad de resolvers, N+1 queries, etc. que no necesitan todavía).

Generen el contrato OpenAPI primero, y de ahí autogeneren tipos TypeScript para el frontend (openapi-typescript o orval). Esto elimina el típico "el front asume un campo que el back no manda".
Formato de error consistente en todos los endpoints:
{
  "error": {
    "code": "RESERVATION_SLOT_TAKEN",
    "message": "El horario seleccionado ya no está disponible",
    "details": {},
    "trace_id": "abc-123"
  }
}
El trace_id es oro para debuggear después (ver Observabilidad, 8.1).

7.3 Tiempo real
Si necesitan actualizaciones en vivo (ej. disponibilidad de mesas cambiando en tiempo real), WebSockets (Socket.IO) o Server-Sent Events desde el BFF — no expongan Kafka/RabbitMQ directo al navegador, jamás.

8. Lo que siempre se pasa (checklist pre-despliegue)
Esto es la lista de lo que casi todo equipo olvida hasta que ya es tarde:

8.1 Observabilidad
 Logs estructurados (JSON, no console.log de texto plano) con trace_id que viaja entre servicios (correlación distribuida).
 Métricas: al menos latencia, tasa de error, throughput por servicio (Prometheus + Grafana, o algo gestionado como Better Stack / Axiom si no quieren operar Prometheus).
 Tracing distribuido (OpenTelemetry) — sin esto, debuggear "por qué falló esta reserva" entre 4 microservicios es un infierno.
 Alertas (mínimo: caída de servicio, error rate > X%, DB cerca del límite de conexiones) a Slack/Discord/email.
8.2 Health checks y resiliencia
 Endpoint /health (liveness) y /ready (readiness) en cada servicio — sin esto el orquestador no sabe cuándo reiniciar o cuándo mandar tráfico.
 Timeouts explícitos en TODAS las llamadas HTTP entre servicios (nunca esperar indefinido).
 Retries con backoff exponencial en consumo de eventos (no reintentar infinito ni inmediato).
 Dead Letter Queue para eventos que fallan repetidamente — no los pierdan, no bloqueen la cola.
8.3 Configuración y ambientes
 Mínimo 3 ambientes: dev, staging, production — con variables de entorno separadas.
 .env.example commiteado, .env real en .gitignore desde el primer commit.
 Feature flags (aunque sea algo simple como un campo en DB o LaunchDarkly free tier) para poder activar/desactivar funcionalidades por tenant sin redeploy.
8.4 Backups y continuidad
 Backups automáticos diarios de cada DB, con prueba de restauración real (no solo "se generó el backup" — practiquen restaurarlo al menos una vez).
 Retención de al menos 7-30 días según criticidad del dato.
 Plan simple de disaster recovery: si Postgres se cae, ¿en cuánto tiempo están de vuelta? Documenten el RTO/RPO aunque sea informal.
8.5 Dominio, DNS, certificados
 Certificados TLS automáticos (Let's Encrypt vía Caddy/Traefik, o gestionado por la plataforma cloud).
 Si es multi-tenant con subdominios (tenant.tuapp.com) o dominios propios (white-label), configuren wildcard DNS y certificados wildcard desde ya — agregarlo después es doloroso.
 CDN para assets estáticos del frontend (Cloudflare, gratis para empezar).
8.6 Testing (mínimo viable, no exagerado)
 Tests unitarios del dominio (la lógica de negocio pura, sin mocks pesados — esto es barato con hexagonal porque el dominio no tiene dependencias externas).
 Al menos tests de integración de los flujos críticos (crear reserva, procesar pago) — no persigan 100% de cobertura, prioricen lo que si falla, duele en plata.
 Tests de contrato entre BFF y microservicios (Pact o similar) si quieren dormir tranquilos cuando cambien un endpoint.
8.7 Costos (nadie lo pone y después asusta la factura)
 Alertas de billing en el proveedor cloud (ej. AWS Budgets) desde el día 1.
 Revisen si Kafka gestionado (Confluent Cloud) vs RabbitMQ self-hosted vs algo más liviano (Redis Streams, o incluso SQS+SNS si están en AWS) tiene sentido de costo para su volumen actual — no paguen por Kafka enterprise si van a mover 100 eventos/día al inicio.
8.8 Documentación mínima
 README por servicio: cómo levantarlo local, variables de entorno requeridas, cómo correr tests.
 Diagrama de arquitectura actualizado (aunque sea en Mermaid dentro del repo, no un PDF que nadie vuelve a abrir).
 Runbook básico: "si el servicio X está caído, revisa Y, Z" — para cuando no estés tú disponible.
9. Git — flujo de trabajo para el equipo
9.1 Estructura de repos
Con multi-microservicio y equipo chico, dos opciones válidas:

Polyrepo (un repo por servicio): más aislamiento, pero con 2 backend gestionar 8+ repos es carga extra.
Monorepo (todos los servicios en un repo, con carpetas): recomendado para su tamaño de equipo — un solo lugar para buscar código, un solo CI a configurar bien, más fácil hacer cambios que cruzan servicios. Usen herramientas como Turborepo o Nx para que el monorepo no se vuelva lento de buildear.
/hub-negocios
├── services/
│   ├── tenant-service/
│   ├── identity-service/
│   ├── reservations-service/
│   └── ...
├── packages/
│   └── shared-kernel/       ← envelope de eventos, tipos comunes, Money, etc.
├── apps/
│   └── web-frontend/
└── docker-compose.yml
9.2 Branching
Con equipo de 3 devs, no usen Git Flow completo (demasiadas ramas para su tamaño). Usen trunk-based simplificado:

main → siempre desplegable.
feature/nombre-corto → rama corta (máximo 2-3 días de vida), PR contra main.
hotfix/nombre → para arreglos urgentes en producción.
9.3 Commits — Conventional Commits
Adóptenlo desde el día 1, permite generar changelogs automáticos y es estándar de la industria:

feat(reservations): agregar validación de doble reserva
fix(auth): corregir expiración de refresh token
chore(deps): actualizar prisma a 5.x
docs(readme): agregar instrucciones de docker-compose
9.4 Protección de rama y CI
 main protegida: no push directo, requiere PR + al menos 1 review (aunque sean 3 devs, revisar entre ustedes atrapa muchos bugs).
 CI obligatorio antes de mergear: lint + tests + build deben pasar en verde.
 Secrets nunca en el repo — usen GitHub Actions secrets / el vault mencionado en 3.3. Agreguen .env, *.pem, *.key al .gitignore desde el primer commit del repo, no después.
 Firma de commits (GPG) si quieren nivel senior pesado de verdad — opcional pero recomendable si van a tener colaboradores externos algún día.
9.5 Versionado
Semantic Versioning (MAJOR.MINOR.PATCH) por servicio si van a versionar APIs internas — importante cuando dos servicios necesiten coexistir en versiones distintas durante un despliegue gradual.

10. Orden de implementación sugerido (para no ahogarse)
Monorepo + shared-kernel (envelope de eventos, TenantContext) + Docker Compose local.
identity-service + tenant-service (sin esto nada más tiene sentido).
RLS en Postgres desde el primer servicio que toque datos de tenant.
catalog-service + order-service (el core transaccional).
Un solo capability service completo de punta a punta (ej. reservations-service) con Outbox Pattern funcionando.
BFF + contrato OpenAPI para que el frontend pueda empezar a integrar en paralelo.
Observabilidad básica (logs + health checks) antes de agregar el segundo capability service — así detectan problemas mientras el sistema todavía es simple.
CI/CD + ambientes staging/production.
Segundo capability service (checkout-service o inventory-service), reutilizando todo el patrón ya probado.
Seguridad avanzada (rate limiting fino, auditoría, rotación de secrets) antes de aceptar el primer tenant de pago real.
