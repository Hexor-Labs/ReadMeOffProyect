-- Arranque del Postgres de desarrollo.
--
-- Una base por servicio, aunque todas vivan en el mismo servidor físico. Eso
-- es lo que recomienda ExplicacionTec.md y tiene una razón concreta: cuesta lo
-- mismo al principio y evita que alguien escriba un JOIN entre las tablas de
-- dos servicios. Un JOIN así no se puede deshacer después sin reescribir medio
-- sistema, porque para cuando duele ya hay veinte consultas que dependen de él.

CREATE DATABASE tenant_db;
CREATE DATABASE identity_db;
CREATE DATABASE catalog_db;
CREATE DATABASE order_db;
CREATE DATABASE billing_db;
CREATE DATABASE notification_db;
CREATE DATABASE reservations_db;
CREATE DATABASE inventory_db;
CREATE DATABASE checkout_db;
CREATE DATABASE landing_db;

-- Dos roles, y esta separación es la que hace que Row-Level Security sirva
-- para algo.
--
-- El dueño de una tabla se salta RLS por defecto. Si la aplicación se conecta
-- como dueña, las políticas están escritas pero no se aplican nunca: parece
-- que hay aislamiento y no lo hay. Por eso:
--
--   hub       → dueño. Corre las migraciones y el publicador de outbox, que
--               necesita leer eventos de todos los tenants a la vez.
--   hub_app   → el que usa el servicio para atender peticiones. No es dueño de
--               nada, así que RLS se le aplica siempre.
--
-- En producción son además credenciales distintas, rotadas cada 90 días.

CREATE ROLE hub_app WITH LOGIN PASSWORD 'cambia-esto-en-local';

-- Permisos del rol de aplicación sobre cada base. Se conceden sobre las tablas
-- existentes y las futuras: sin la segunda parte, cada migración nueva deja
-- tablas a las que la aplicación no puede entrar.
\connect tenant_db
GRANT USAGE ON SCHEMA public TO hub_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hub_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO hub_app;

\connect identity_db
GRANT USAGE ON SCHEMA public TO hub_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hub_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO hub_app;

\connect catalog_db
GRANT USAGE ON SCHEMA public TO hub_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hub_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO hub_app;

\connect order_db
GRANT USAGE ON SCHEMA public TO hub_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hub_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO hub_app;

\connect billing_db
GRANT USAGE ON SCHEMA public TO hub_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hub_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO hub_app;

\connect notification_db
GRANT USAGE ON SCHEMA public TO hub_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hub_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO hub_app;

\connect reservations_db
GRANT USAGE ON SCHEMA public TO hub_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hub_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO hub_app;

\connect inventory_db
GRANT USAGE ON SCHEMA public TO hub_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hub_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO hub_app;

\connect checkout_db
GRANT USAGE ON SCHEMA public TO hub_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hub_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO hub_app;

\connect landing_db
GRANT USAGE ON SCHEMA public TO hub_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hub_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO hub_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO hub_app;
