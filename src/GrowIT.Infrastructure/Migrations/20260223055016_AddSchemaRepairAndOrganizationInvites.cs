using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSchemaRepairAndOrganizationInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Schema repair for environments where these columns were added to the model
            // but never shipped in a migration.
            migrationBuilder.Sql("""
                ALTER TABLE "Programs" ADD COLUMN IF NOT EXISTS "CapacityLimit" integer NULL;
                ALTER TABLE "Programs" ADD COLUMN IF NOT EXISTS "CapacityPeriod" text NULL;
                ALTER TABLE "Imprints" ADD COLUMN IF NOT EXISTS "Category" integer NOT NULL DEFAULT 0;
                """);

            migrationBuilder.CreateTable(
                name: "OrganizationInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    FirstName = table.Column<string>(type: "text", nullable: false),
                    LastName = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationInvites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvites_TenantId",
                table: "OrganizationInvites",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvites_Email",
                table: "OrganizationInvites",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvites_TokenHash",
                table: "OrganizationInvites",
                column: "TokenHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationInvites");

            migrationBuilder.Sql("""
                ALTER TABLE "Programs" DROP COLUMN IF EXISTS "CapacityLimit";
                ALTER TABLE "Programs" DROP COLUMN IF EXISTS "CapacityPeriod";
                ALTER TABLE "Imprints" DROP COLUMN IF EXISTS "Category";
                """);
        }
    }
}
