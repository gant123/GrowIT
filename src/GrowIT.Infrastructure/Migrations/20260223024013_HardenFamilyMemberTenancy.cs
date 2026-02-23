using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenFamilyMemberTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "FamilyMembers",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            // Backfill tenant ownership from the parent client record.
            migrationBuilder.Sql(@"
                UPDATE ""FamilyMembers"" fm
                SET ""TenantId"" = c.""TenantId""
                FROM ""Clients"" c
                WHERE fm.""ClientId"" = c.""Id"";
            ");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMembers_ClientId",
                table: "FamilyMembers",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMembers_TenantId",
                table: "FamilyMembers",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_FamilyMembers_Clients_ClientId",
                table: "FamilyMembers",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FamilyMembers_Clients_ClientId",
                table: "FamilyMembers");

            migrationBuilder.DropIndex(
                name: "IX_FamilyMembers_ClientId",
                table: "FamilyMembers");

            migrationBuilder.DropIndex(
                name: "IX_FamilyMembers_TenantId",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "FamilyMembers");
        }
    }
}
