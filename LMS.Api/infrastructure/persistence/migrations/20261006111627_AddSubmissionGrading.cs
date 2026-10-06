using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Api.infrastructure.persistence.migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionGrading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "feedback",
                table: "submissions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "grade",
                table: "submissions",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "graded_at",
                table: "submissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "graded_by_id",
                table: "submissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_submissions_graded_by_id",
                table: "submissions",
                column: "graded_by_id");

            migrationBuilder.AddForeignKey(
                name: "FK_submissions_users_graded_by_id",
                table: "submissions",
                column: "graded_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_submissions_users_graded_by_id",
                table: "submissions");

            migrationBuilder.DropIndex(
                name: "IX_submissions_graded_by_id",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "feedback",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "grade",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "graded_at",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "graded_by_id",
                table: "submissions");
        }
    }
}
