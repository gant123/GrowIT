using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations
{
    public partial class AddUserNotificationPreferences : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "NotifyInviteActivity" boolean NOT NULL DEFAULT TRUE;
                                 ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "NotifySystemAlerts" boolean NOT NULL DEFAULT TRUE;
                                 UPDATE "Users" SET "NotifyInviteActivity" = TRUE WHERE "NotifyInviteActivity" IS DISTINCT FROM TRUE;
                                 UPDATE "Users" SET "NotifySystemAlerts" = TRUE WHERE "NotifySystemAlerts" IS DISTINCT FROM TRUE;
                                 """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 ALTER TABLE "Users" DROP COLUMN IF EXISTS "NotifyInviteActivity";
                                 ALTER TABLE "Users" DROP COLUMN IF EXISTS "NotifySystemAlerts";
                                 """);
        }
    }
}