using System;
using GrowIT.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260621012500_MakeBetaFeedbackPlatformOwned")]
    public partial class MakeBetaFeedbackPlatformOwned : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "BetaFeedbacks",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "BetaFeedbacks",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "BetaFeedbacks"
                SET "UserId" = '00000000-0000-0000-0000-000000000000'
                WHERE "UserId" IS NULL;

                UPDATE "BetaFeedbacks"
                SET "TenantId" = '00000000-0000-0000-0000-000000000000'
                WHERE "TenantId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "BetaFeedbacks",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "BetaFeedbacks",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
