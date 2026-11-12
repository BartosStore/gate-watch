using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "blacklist_entries",
                columns: table => new
                {
                    url = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blacklist_entries", x => x.url);
                });

            migrationBuilder.CreateTable(
                name: "blacklist_meta",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    last_import_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    entry_count = table.Column<int>(type: "integer", nullable: false),
                    source_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blacklist_meta", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "merchants",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "findings",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    merchant_id = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    blacklist_source = table.Column<string>(type: "text", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    first_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resolved = table.Column<bool>(type: "boolean", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_findings", x => x.id);
                    table.ForeignKey(
                        name: "FK_findings_merchants_merchant_id",
                        column: x => x.merchant_id,
                        principalTable: "merchants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "checks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant_id = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    urls_seen = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_checks", x => x.id);
                    table.ForeignKey(
                        name: "FK_checks_merchants_merchant_id",
                        column: x => x.merchant_id,
                        principalTable: "merchants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_checks_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "run_items",
                columns: table => new
                {
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant_id = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    check_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_run_items", x => new { x.run_id, x.merchant_id });
                    table.ForeignKey(
                        name: "FK_run_items_merchants_merchant_id",
                        column: x => x.merchant_id,
                        principalTable: "merchants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_run_items_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_findings_merchant_id_url",
                table: "findings",
                columns: new[] { "merchant_id", "url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_findings_resolved_last_seen_at",
                table: "findings",
                columns: new[] { "resolved", "last_seen_at" });

            migrationBuilder.CreateIndex(
                name: "IX_checks_merchant_id_started_at",
                table: "checks",
                columns: new[] { "merchant_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_checks_run_id",
                table: "checks",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "IX_run_items_merchant_id",
                table: "run_items",
                column: "merchant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blacklist_entries");

            migrationBuilder.DropTable(
                name: "blacklist_meta");

            migrationBuilder.DropTable(
                name: "findings");

            migrationBuilder.DropTable(
                name: "checks");

            migrationBuilder.DropTable(
                name: "run_items");

            migrationBuilder.DropTable(
                name: "merchants");

            migrationBuilder.DropTable(
                name: "runs");
        }
    }
}
