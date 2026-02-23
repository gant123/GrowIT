using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations;

public partial class AddUserPhotoUrl : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
                                 ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "PhotoUrl" text NULL;
                             """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
                                 ALTER TABLE "Users" DROP COLUMN IF EXISTS "PhotoUrl";
                             """);
    }
}