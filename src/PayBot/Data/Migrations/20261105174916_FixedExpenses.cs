using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixedExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fixed_expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    EndMonth = table.Column<DateOnly>(type: "date", nullable: true),
                    WhatsAppMessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fixed_expenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fixed_expenses_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_expenses_TenantId_StartMonth",
                table: "fixed_expenses",
                columns: new[] { "TenantId", "StartMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_expenses_WhatsAppMessageId",
                table: "fixed_expenses",
                column: "WhatsAppMessageId",
                unique: true);

            // PaymentMethod.Fixed = 5. Preserve one-month coverage for historical rows.
            migrationBuilder.Sql("""
                INSERT INTO fixed_expenses ("Id", "TenantId", "CreatedAt", "Amount", "Description", "StartMonth", "EndMonth", "WhatsAppMessageId")
                SELECT
                    "Id",
                    "TenantId",
                    "CreatedAt",
                    "Amount",
                    COALESCE(NULLIF(TRIM("Description"), ''), 'Despesa Fixa'),
                    date_trunc('month', "LocalDate")::date,
                    date_trunc('month', "LocalDate")::date,
                    "WhatsAppMessageId"
                FROM transactions
                WHERE "PaymentMethod" = 5;

                DELETE FROM transactions WHERE "PaymentMethod" = 5;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO transactions (
                    "Id", "TenantId", "CreatedAt", "OccurredAt", "LocalDate", "Direction", "Amount",
                    "PaymentMethod", "Description", "Installments", "IsFixed", "CardId", "WhatsAppMessageId")
                SELECT
                    "Id",
                    "TenantId",
                    "CreatedAt",
                    ("StartMonth"::timestamp AT TIME ZONE 'America/Sao_Paulo') + interval '12 hours',
                    "StartMonth",
                    2,
                    "Amount",
                    5,
                    "Description",
                    NULL,
                    FALSE,
                    NULL,
                    "WhatsAppMessageId"
                FROM fixed_expenses;

                DELETE FROM fixed_expenses;
                """);

            migrationBuilder.DropTable(
                name: "fixed_expenses");
        }
    }
}
