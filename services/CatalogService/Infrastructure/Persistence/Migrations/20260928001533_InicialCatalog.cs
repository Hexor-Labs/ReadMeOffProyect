using System;
using HubNegocios.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HubNegocios.CatalogService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InicialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    price_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    availability_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    available_quantity = table.Column<int>(type: "integer", nullable: true),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    image_urls = table.Column<string[]>(type: "text[]", nullable: false),
                    attributes = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregate_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    event_payload = table.Column<string>(type: "jsonb", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_retry_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    dead_lettered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "item_price_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    new_price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_price_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_item_price_history_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_item_price_history_item_id",
                table: "item_price_history",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_price_history_tenant_item_fecha",
                table: "item_price_history",
                columns: new[] { "tenant_id", "item_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_items_tenant_activo",
                table: "items",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_items_tenant_categoria",
                table: "items",
                columns: new[] { "tenant_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ux_items_tenant_sku",
                table: "items",
                columns: new[] { "tenant_id", "sku" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_pendientes",
                table: "outbox_events",
                column: "id",
                filter: "published = false AND dead_lettered_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_tenant_agregado",
                table: "outbox_events",
                columns: new[] { "tenant_id", "aggregate_id" });

            /*
              Índice GIN sobre las etiquetas. Va con SQL a mano porque EF Core no
              sabe declarar el método de acceso de un índice, y hace falta de
              verdad: un B-tree no sirve para preguntar «qué filas contienen esta
              etiqueta» dentro de un text[], así que sin este índice el filtro por
              etiquetas recorre la tabla entera y deja de escalar en cuanto el
              negocio pasa de unos cientos de items.
            */
            migrationBuilder.Sql("CREATE INDEX ix_items_tags_gin ON items USING GIN (tags);");

            /*
              Row-Level Security: el cinturón, siendo los filtros globales de EF
              Core los tirantes. Actúa dentro de Postgres, así que sigue en pie si
              alguien escribe IgnoreQueryFilters(), entra por FromSqlRaw o se
              equivoca en un Where.

              outbox_events se queda FUERA a propósito, por el mismo motivo por el
              que ApplyTenantFilters() la excluye: la lee el publicador de fondo,
              que publica los eventos de todos los tenants y corre sin tenant en
              contexto. Con la política puesta no vería ni una fila y los eventos
              no saldrían nunca.
            */
            migrationBuilder.EnableTenantIsolation("items");
            migrationBuilder.EnableTenantIsolation("item_price_history");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            /*
              No hace falta deshacer el índice GIN ni las políticas de RLS: al
              soltar la tabla se van con ella. Escribirlo aquí de todas formas
              sería SQL que nunca se comprueba y que, si algún día se equivoca,
              rompe la vuelta atrás justo cuando más urge.
            */
            migrationBuilder.DropTable(
                name: "item_price_history");

            migrationBuilder.DropTable(
                name: "outbox_events");

            migrationBuilder.DropTable(
                name: "items");
        }
    }
}
