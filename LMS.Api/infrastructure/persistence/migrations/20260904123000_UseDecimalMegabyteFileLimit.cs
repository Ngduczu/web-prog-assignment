using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Api.infrastructure.persistence.migrations;

public partial class UseDecimalMegabyteFileLimit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_chat_attachments_file_size",
            table: "chat_attachments");

        migrationBuilder.AddCheckConstraint(
            name: "ck_chat_attachments_file_size",
            table: "chat_attachments",
            sql: "file_size > 0 AND file_size < 20000000");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_chat_attachments_file_size",
            table: "chat_attachments");

        migrationBuilder.AddCheckConstraint(
            name: "ck_chat_attachments_file_size",
            table: "chat_attachments",
            sql: "file_size > 0 AND file_size < 20971520");
    }
}