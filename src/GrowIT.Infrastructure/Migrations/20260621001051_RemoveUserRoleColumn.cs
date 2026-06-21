using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowIT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUserRoleColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH desired_roles("Name", "SortOrder") AS (
                    VALUES
                        ('SuperAdmin', 1),
                        ('Owner', 2),
                        ('Admin', 3),
                        ('Manager', 4),
                        ('Case Manager', 5),
                        ('Analyst', 6),
                        ('Member', 7)
                    UNION ALL
                    SELECT BTRIM("Role"), 100
                    FROM "Users"
                    WHERE "Role" IS NOT NULL AND BTRIM("Role") <> ''
                ),
                normalized_roles AS (
                    SELECT DISTINCT ON (UPPER("Name"))
                        "Name",
                        UPPER("Name") AS "NormalizedName"
                    FROM desired_roles
                    WHERE "Name" IS NOT NULL AND BTRIM("Name") <> ''
                    ORDER BY UPPER("Name"), "SortOrder"
                )
                INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
                SELECT
                    (
                        SUBSTRING(MD5('growit-role:' || "NormalizedName") FROM 1 FOR 8) || '-' ||
                        SUBSTRING(MD5('growit-role:' || "NormalizedName") FROM 9 FOR 4) || '-' ||
                        SUBSTRING(MD5('growit-role:' || "NormalizedName") FROM 13 FOR 4) || '-' ||
                        SUBSTRING(MD5('growit-role:' || "NormalizedName") FROM 17 FOR 4) || '-' ||
                        SUBSTRING(MD5('growit-role:' || "NormalizedName") FROM 21 FOR 12)
                    )::uuid,
                    "Name",
                    "NormalizedName",
                    MD5('growit-role-stamp:' || "NormalizedName")
                FROM normalized_roles role_source
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "AspNetRoles" existing
                    WHERE existing."NormalizedName" = role_source."NormalizedName"
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
                SELECT u."Id", r."Id"
                FROM "Users" u
                JOIN "AspNetRoles" r
                    ON r."NormalizedName" = UPPER(BTRIM(u."Role"))
                WHERE u."Role" IS NOT NULL
                    AND BTRIM(u."Role") <> ''
                    AND NOT EXISTS (
                        SELECT 1
                        FROM "AspNetUserRoles" existing
                        WHERE existing."UserId" = u."Id"
                            AND existing."RoleId" = r."Id"
                    );
                """);

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                WITH ranked_roles AS (
                    SELECT
                        ur."UserId",
                        r."Name",
                        ROW_NUMBER() OVER (
                            PARTITION BY ur."UserId"
                            ORDER BY CASE UPPER(r."Name")
                                WHEN 'SUPERADMIN' THEN 1
                                WHEN 'OWNER' THEN 2
                                WHEN 'ADMIN' THEN 3
                                WHEN 'MANAGER' THEN 4
                                WHEN 'CASE MANAGER' THEN 5
                                WHEN 'ANALYST' THEN 6
                                WHEN 'MEMBER' THEN 7
                                ELSE 100
                            END, r."Name"
                        ) AS "Rank"
                    FROM "AspNetUserRoles" ur
                    JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
                )
                UPDATE "Users" u
                SET "Role" = ranked_roles."Name"
                FROM ranked_roles
                WHERE ranked_roles."UserId" = u."Id"
                    AND ranked_roles."Rank" = 1;
                """);
        }
    }
}
