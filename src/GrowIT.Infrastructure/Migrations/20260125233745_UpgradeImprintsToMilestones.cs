using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeImprintsToMilestones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Imprints_Investments_InvestmentId",
                table: "Imprints");

            migrationBuilder.AlterColumn<Guid>(
                name: "InvestmentId",
                table: "Imprints",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "Imprints",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "DateOccurred",
                table: "Imprints",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "FamilyMemberId",
                table: "Imprints",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Imprints",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Imprints_ClientId",
                table: "Imprints",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Imprints_FamilyMemberId",
                table: "Imprints",
                column: "FamilyMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_Imprints_Clients_ClientId",
                table: "Imprints",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Imprints_FamilyMembers_FamilyMemberId",
                table: "Imprints",
                column: "FamilyMemberId",
                principalTable: "FamilyMembers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Imprints_Investments_InvestmentId",
                table: "Imprints",
                column: "InvestmentId",
                principalTable: "Investments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Imprints_Clients_ClientId",
                table: "Imprints");

            migrationBuilder.DropForeignKey(
                name: "FK_Imprints_FamilyMembers_FamilyMemberId",
                table: "Imprints");

            migrationBuilder.DropForeignKey(
                name: "FK_Imprints_Investments_InvestmentId",
                table: "Imprints");

            migrationBuilder.DropIndex(
                name: "IX_Imprints_ClientId",
                table: "Imprints");

            migrationBuilder.DropIndex(
                name: "IX_Imprints_FamilyMemberId",
                table: "Imprints");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "Imprints");

            migrationBuilder.DropColumn(
                name: "DateOccurred",
                table: "Imprints");

            migrationBuilder.DropColumn(
                name: "FamilyMemberId",
                table: "Imprints");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Imprints");

            migrationBuilder.AlterColumn<Guid>(
                name: "InvestmentId",
                table: "Imprints",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Imprints_Investments_InvestmentId",
                table: "Imprints",
                column: "InvestmentId",
                principalTable: "Investments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
