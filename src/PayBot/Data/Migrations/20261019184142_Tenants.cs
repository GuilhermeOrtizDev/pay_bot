using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class Tenants : Migration
    {
        // Frozen copy of Tenant.DefaultId so this migration never changes if the constant does.
        private const string DefaultTenantId = "7f1c2d3e-0a4b-4c5d-9e6f-000000000001";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_cards_Kind_NormalizedName",
                table: "cards");

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.Id);
                });

            migrationBuilder.Sql(
                $"""INSERT INTO tenants ("Id", "Name", "NormalizedName", "CreatedAt") VALUES ('{DefaultTenantId}', 'Principal', 'principal', now());""");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql($"""UPDATE transactions SET "TenantId" = '{DefaultTenantId}';""");
            migrationBuilder.Sql($"""UPDATE cards SET "TenantId" = '{DefaultTenantId}';""");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "transactions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "cards",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Jid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_groups_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_TenantId_LocalDate",
                table: "transactions",
                columns: new[] { "TenantId", "LocalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_cards_TenantId_Kind_NormalizedName",
                table: "cards",
                columns: new[] { "TenantId", "Kind", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_groups_Jid",
                table: "groups",
                column: "Jid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_groups_TenantId",
                table: "groups",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_tenants_NormalizedName",
                table: "tenants",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_cards_tenants_TenantId",
                table: "cards",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_tenants_TenantId",
                table: "transactions",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cards_tenants_TenantId",
                table: "cards");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_tenants_TenantId",
                table: "transactions");

            migrationBuilder.DropTable(
                name: "groups");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropIndex(
                name: "IX_transactions_TenantId_LocalDate",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_cards_TenantId_Kind_NormalizedName",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "cards");

            migrationBuilder.CreateIndex(
                name: "IX_cards_Kind_NormalizedName",
                table: "cards",
                columns: new[] { "Kind", "NormalizedName" },
                unique: true);
        }
    }
}
