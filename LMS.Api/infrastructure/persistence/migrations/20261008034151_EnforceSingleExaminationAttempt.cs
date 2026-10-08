using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Api.infrastructure.persistence.migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleExaminationAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_examination_attempts_examination_id_student_id",
                table: "examination_attempts");

            migrationBuilder.CreateIndex(
                name: "IX_examination_attempts_examination_id_student_id",
                table: "examination_attempts",
                columns: new[] { "examination_id", "student_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_examination_attempts_examination_id_student_id",
                table: "examination_attempts");

            migrationBuilder.CreateIndex(
                name: "IX_examination_attempts_examination_id_student_id",
                table: "examination_attempts",
                columns: new[] { "examination_id", "student_id" });
        }
    }
}
