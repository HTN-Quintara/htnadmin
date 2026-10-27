using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Finance_Accounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_reconcilable",
                table: "payment_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "opening_balance",
                table: "payment_accounts",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "opening_balance_as_of",
                table: "payment_accounts",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_accounts_opening_balance",
                table: "payment_accounts",
                sql: "round(opening_balance, 2) = opening_balance");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_accounts_opening_balance",
                table: "payment_accounts");

            migrationBuilder.DropColumn(
                name: "is_reconcilable",
                table: "payment_accounts");

            migrationBuilder.DropColumn(
                name: "opening_balance",
                table: "payment_accounts");

            migrationBuilder.DropColumn(
                name: "opening_balance_as_of",
                table: "payment_accounts");
        }
    }
}
