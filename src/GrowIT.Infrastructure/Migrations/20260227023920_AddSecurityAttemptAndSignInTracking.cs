using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityAttemptAndSignInTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UnauthorizedAccessAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    ClientIp = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    Referer = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsAuthenticated = table.Column<bool>(type: "boolean", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnauthorizedAccessAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSignInEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    ClientIp = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSignInEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnauthorizedAccessAttempts_ClientIp",
                table: "UnauthorizedAccessAttempts",
                column: "ClientIp");

            migrationBuilder.CreateIndex(
                name: "IX_UnauthorizedAccessAttempts_OccurredAt",
                table: "UnauthorizedAccessAttempts",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserSignInEvents_ClientIp",
                table: "UserSignInEvents",
                column: "ClientIp");

            migrationBuilder.CreateIndex(
                name: "IX_UserSignInEvents_OccurredAt",
                table: "UserSignInEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserSignInEvents_UserId",
                table: "UserSignInEvents",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UnauthorizedAccessAttempts");

            migrationBuilder.DropTable(
                name: "UserSignInEvents");
        }
    }
}
