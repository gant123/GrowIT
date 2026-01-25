using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkInvestmentsToIndividuals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FamilyMemberId",
                table: "Investments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Investments_FamilyMemberId",
                table: "Investments",
                column: "FamilyMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_Investments_FamilyMembers_FamilyMemberId",
                table: "Investments",
                column: "FamilyMemberId",
                principalTable: "FamilyMembers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Investments_FamilyMembers_FamilyMemberId",
                table: "Investments");

            migrationBuilder.DropIndex(
                name: "IX_Investments_FamilyMemberId",
                table: "Investments");

            migrationBuilder.DropColumn(
                name: "FamilyMemberId",
                table: "Investments");
        }
    }
}
