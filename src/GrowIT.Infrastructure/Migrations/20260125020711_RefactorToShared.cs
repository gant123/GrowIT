using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefactorToShared : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Investments_FundId",
                table: "Investments",
                column: "FundId");

            migrationBuilder.CreateIndex(
                name: "IX_Investments_ProgramId",
                table: "Investments",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_Imprints_InvestmentId",
                table: "Imprints",
                column: "InvestmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Imprints_Investments_InvestmentId",
                table: "Imprints",
                column: "InvestmentId",
                principalTable: "Investments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Investments_Funds_FundId",
                table: "Investments",
                column: "FundId",
                principalTable: "Funds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Investments_Programs_ProgramId",
                table: "Investments",
                column: "ProgramId",
                principalTable: "Programs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Imprints_Investments_InvestmentId",
                table: "Imprints");

            migrationBuilder.DropForeignKey(
                name: "FK_Investments_Funds_FundId",
                table: "Investments");

            migrationBuilder.DropForeignKey(
                name: "FK_Investments_Programs_ProgramId",
                table: "Investments");

            migrationBuilder.DropIndex(
                name: "IX_Investments_FundId",
                table: "Investments");

            migrationBuilder.DropIndex(
                name: "IX_Investments_ProgramId",
                table: "Investments");

            migrationBuilder.DropIndex(
                name: "IX_Imprints_InvestmentId",
                table: "Imprints");
        }
    }
}
