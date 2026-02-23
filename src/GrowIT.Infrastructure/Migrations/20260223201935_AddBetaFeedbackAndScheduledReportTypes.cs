using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations
{
    public partial class AddBetaFeedbackAndScheduledReportTypes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Format",
                table: "ReportSchedules",
                type: "text",
                nullable: false,
                defaultValue: "pdf");

            migrationBuilder.AddColumn<string>(
                name: "ReportType",
                table: "ReportSchedules",
                type: "text",
                nullable: false,
                defaultValue: "impact-summary");

            migrationBuilder.CreateTable(
                name: "BetaFeedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    PageUrl = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AdminNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BetaFeedbacks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BetaFeedbacks_TenantId",
                table: "BetaFeedbacks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BetaFeedbacks_Status",
                table: "BetaFeedbacks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BetaFeedbacks_CreatedAt",
                table: "BetaFeedbacks",
                column: "CreatedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BetaFeedbacks");

            migrationBuilder.DropColumn(
                name: "Format",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "ReportType",
                table: "ReportSchedules");
        }
    }
}
