using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailDeliverySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "email_from_email",
                table: "org_settings",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "no-reply@hackthenorth.ph");

            migrationBuilder.AddColumn<string>(
                name: "email_from_name",
                table: "org_settings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "HackTheNorth.PH");

            migrationBuilder.AddColumn<string>(
                name: "email_inbox_id",
                table: "org_settings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_mode",
                table: "org_settings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Sandbox");

            migrationBuilder.AddColumn<string>(
                name: "email_protected_api_token",
                table: "org_settings",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_reply_to",
                table: "org_settings",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_token_last4",
                table: "org_settings",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_updated_at",
                table: "org_settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_org_settings_email_mode",
                table: "org_settings",
                sql: "email_mode IN ('Sandbox', 'Live')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_org_settings_email_sandbox_inbox",
                table: "org_settings",
                sql: "email_updated_at IS NULL OR email_mode <> 'Sandbox' OR email_inbox_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_org_settings_email_token",
                table: "org_settings",
                sql: "(email_protected_api_token IS NULL) = (email_token_last4 IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_org_settings_email_mode",
                table: "org_settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_org_settings_email_sandbox_inbox",
                table: "org_settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_org_settings_email_token",
                table: "org_settings");

            migrationBuilder.DropColumn(
                name: "email_from_email",
                table: "org_settings");

            migrationBuilder.DropColumn(
                name: "email_from_name",
                table: "org_settings");

            migrationBuilder.DropColumn(
                name: "email_inbox_id",
                table: "org_settings");

            migrationBuilder.DropColumn(
                name: "email_mode",
                table: "org_settings");

            migrationBuilder.DropColumn(
                name: "email_protected_api_token",
                table: "org_settings");

            migrationBuilder.DropColumn(
                name: "email_reply_to",
                table: "org_settings");

            migrationBuilder.DropColumn(
                name: "email_token_last4",
                table: "org_settings");

            migrationBuilder.DropColumn(
                name: "email_updated_at",
                table: "org_settings");
        }
    }
}
