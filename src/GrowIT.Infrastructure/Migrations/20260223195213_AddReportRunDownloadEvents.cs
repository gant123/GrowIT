using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations
{
    public partial class AddReportRunDownloadEvents : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportRunDownloadEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    DownloadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DownloadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    ClientIp = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportRunDownloadEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportRunDownloadEvents_TenantId",
                table: "ReportRunDownloadEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportRunDownloadEvents_ReportRunId",
                table: "ReportRunDownloadEvents",
                column: "ReportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportRunDownloadEvents_DownloadedAt",
                table: "ReportRunDownloadEvents",
                column: "DownloadedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportRunDownloadEvents");
        }
    }
}
