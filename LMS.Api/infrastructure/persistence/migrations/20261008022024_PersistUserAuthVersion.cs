using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Api.infrastructure.persistence.migrations
{
    /// <inheritdoc />
    public partial class PersistUserAuthVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "auth_version",
                table: "users",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auth_version",
                table: "users");
        }
    }
}
