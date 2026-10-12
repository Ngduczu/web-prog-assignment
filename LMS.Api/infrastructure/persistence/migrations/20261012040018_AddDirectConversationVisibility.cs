using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Api.infrastructure.persistence.migrations
{
    /// <inheritdoc />
    public partial class AddDirectConversationVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "conversation_cleared_at",
                table: "chat_participants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "conversation_hidden_at",
                table: "chat_participants",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "conversation_cleared_at",
                table: "chat_participants");

            migrationBuilder.DropColumn(
                name: "conversation_hidden_at",
                table: "chat_participants");
        }
    }
}
