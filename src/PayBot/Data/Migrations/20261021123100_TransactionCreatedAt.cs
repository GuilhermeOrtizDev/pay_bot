using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class TransactionCreatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.Sql("""UPDATE transactions SET "CreatedAt" = "OccurredAt";""");

            migrationBuilder.Sql("""ALTER TABLE transactions ALTER COLUMN "CreatedAt" DROP DEFAULT;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "transactions");
        }
    }
}
