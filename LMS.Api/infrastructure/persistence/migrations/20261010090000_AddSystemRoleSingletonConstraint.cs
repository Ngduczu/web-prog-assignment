using LMS.Api.infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Api.infrastructure.persistence.migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261010090000_AddSystemRoleSingletonConstraint")]
    public partial class AddSystemRoleSingletonConstraint : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_users_single_system_role",
                table: "users",
                column: "role",
                unique: true,
                filter: "\"role\" = 'System'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_single_system_role",
                table: "users");
        }
    }
}