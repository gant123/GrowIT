using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations
{
    public partial class AddReportRunLifecycleFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "ReportRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "ReportRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastDownloadedAt",
                table: "ReportRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "ReportRuns",
                type: "text",
                nullable: false,
                defaultValue: "Queued");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "ReportRuns");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "ReportRuns");

            migrationBuilder.DropColumn(
                name: "LastDownloadedAt",
                table: "ReportRuns");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ReportRuns");
        }
    }
}