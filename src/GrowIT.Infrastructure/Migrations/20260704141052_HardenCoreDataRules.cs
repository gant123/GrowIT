using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenCoreDataRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Programs_TenantId_Name",
                table: "Programs",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Programs_CapacityLimit_Positive",
                table: "Programs",
                sql: "\"CapacityLimit\" IS NULL OR \"CapacityLimit\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Programs_DefaultUnitCost_NonNegative",
                table: "Programs",
                sql: "\"DefaultUnitCost\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Investments_Amount_Positive",
                table: "Investments",
                sql: "\"Amount\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Investments_SnapshotUnitCost_NonNegative",
                table: "Investments",
                sql: "\"SnapshotUnitCost\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_Funds_TenantId_Name",
                table: "Funds",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Funds_AvailableAmount_NonNegative",
                table: "Funds",
                sql: "\"AvailableAmount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Funds_AvailableAmount_NotOverTotal",
                table: "Funds",
                sql: "\"AvailableAmount\" <= \"TotalAmount\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Funds_TotalAmount_NonNegative",
                table: "Funds",
                sql: "\"TotalAmount\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Programs_TenantId_Name",
                table: "Programs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Programs_CapacityLimit_Positive",
                table: "Programs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Programs_DefaultUnitCost_NonNegative",
                table: "Programs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Investments_Amount_Positive",
                table: "Investments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Investments_SnapshotUnitCost_NonNegative",
                table: "Investments");

            migrationBuilder.DropIndex(
                name: "IX_Funds_TenantId_Name",
                table: "Funds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Funds_AvailableAmount_NonNegative",
                table: "Funds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Funds_AvailableAmount_NotOverTotal",
                table: "Funds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Funds_TotalAmount_NonNegative",
                table: "Funds");
        }
    }
}
