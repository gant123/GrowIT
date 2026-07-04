using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActionDeduplicationKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "ReportRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "ReportRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "BetaFeedbacks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmissionFingerprint",
                table: "BetaFeedbacks",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportRuns_IdempotencyKey",
                table: "ReportRuns",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReportRuns_TenantId_RequestedByUserId_RequestFingerprint_Ge~",
                table: "ReportRuns",
                columns: new[] { "TenantId", "RequestedByUserId", "RequestFingerprint", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BetaFeedbacks_IdempotencyKey",
                table: "BetaFeedbacks",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BetaFeedbacks_UserId_SubmissionFingerprint_CreatedAt",
                table: "BetaFeedbacks",
                columns: new[] { "UserId", "SubmissionFingerprint", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReportRuns_IdempotencyKey",
                table: "ReportRuns");

            migrationBuilder.DropIndex(
                name: "IX_ReportRuns_TenantId_RequestedByUserId_RequestFingerprint_Ge~",
                table: "ReportRuns");

            migrationBuilder.DropIndex(
                name: "IX_BetaFeedbacks_IdempotencyKey",
                table: "BetaFeedbacks");

            migrationBuilder.DropIndex(
                name: "IX_BetaFeedbacks_UserId_SubmissionFingerprint_CreatedAt",
                table: "BetaFeedbacks");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "ReportRuns");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "ReportRuns");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "BetaFeedbacks");

            migrationBuilder.DropColumn(
                name: "SubmissionFingerprint",
                table: "BetaFeedbacks");
        }
    }
}
