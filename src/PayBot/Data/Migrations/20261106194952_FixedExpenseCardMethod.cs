using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixedExpenseCardMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CardId",
                table: "fixed_expenses",
                type: "uuid",
                nullable: true);

            // PaymentMethod.Fixed = 5 for existing Despesa Fixa rows.
            migrationBuilder.AddColumn<int>(
                name: "Method",
                table: "fixed_expenses",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.CreateIndex(
                name: "IX_fixed_expenses_CardId",
                table: "fixed_expenses",
                column: "CardId");

            migrationBuilder.AddForeignKey(
                name: "FK_fixed_expenses_cards_CardId",
                table: "fixed_expenses",
                column: "CardId",
                principalTable: "cards",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Card fixed charges previously stored as transactions with IsFixed = true.
            migrationBuilder.Sql("""
                INSERT INTO fixed_expenses ("Id", "TenantId", "CreatedAt", "Amount", "Description", "Method", "CardId", "StartMonth", "EndMonth", "WhatsAppMessageId")
                SELECT
                    "Id",
                    "TenantId",
                    "CreatedAt",
                    "Amount",
                    COALESCE(NULLIF(TRIM("Description"), ''), 'Despesa Fixa'),
                    COALESCE("PaymentMethod", 5),
                    "CardId",
                    date_trunc('month', "LocalDate")::date,
                    date_trunc('month', "LocalDate")::date,
                    "WhatsAppMessageId"
                FROM transactions
                WHERE "IsFixed" = TRUE;

                DELETE FROM transactions WHERE "IsFixed" = TRUE;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "Method",
                table: "fixed_expenses",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 5);
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
                    "Method",
                    "Description",
                    NULL,
                    TRUE,
                    "CardId",
                    "WhatsAppMessageId"
                FROM fixed_expenses
                WHERE "Method" IN (2, 3);

                DELETE FROM fixed_expenses WHERE "Method" IN (2, 3);
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_fixed_expenses_cards_CardId",
                table: "fixed_expenses");

            migrationBuilder.DropIndex(
                name: "IX_fixed_expenses_CardId",
                table: "fixed_expenses");

            migrationBuilder.DropColumn(
                name: "CardId",
                table: "fixed_expenses");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "fixed_expenses");
        }
    }
}
