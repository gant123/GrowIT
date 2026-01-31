using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateTenantAndUserEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OrganizationSize",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OrganizationType",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "TrackInvestments",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TrackOutcomes",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TrackPeople",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TrackPrograms",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Investments",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OrganizationSize",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "OrganizationType",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrackInvestments",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrackOutcomes",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrackPeople",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrackPrograms",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Investments");
        }
    }
}
