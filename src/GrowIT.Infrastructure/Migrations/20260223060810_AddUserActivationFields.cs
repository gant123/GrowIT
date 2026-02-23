using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations
{
    public partial class AddUserActivationFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT TRUE;
                                 ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "DeactivatedAt" timestamp with time zone NULL;
                                 UPDATE "Users" SET "IsActive" = TRUE WHERE "IsActive" IS DISTINCT FROM TRUE;
                                 """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 ALTER TABLE "Users" DROP COLUMN IF EXISTS "DeactivatedAt";
                                 ALTER TABLE "Users" DROP COLUMN IF EXISTS "IsActive";
                                 """);
        }
    }
}