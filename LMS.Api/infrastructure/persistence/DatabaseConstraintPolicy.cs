using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LMS.Api.infrastructure.Persistence;

internal static class DatabaseConstraintPolicy
{
    private static readonly HashSet<string> BusinessUniqueConstraints =
    [
        "IX_users_email",
        "IX_users_single_system_role",
        "IX_enrollments_student_id_course_id",
        "IX_lessons_course_id_lesson_order",
        "IX_submissions_exercise_id_student_id",
        "IX_chat_rooms_course_id",
        "IX_chat_rooms_direct_message_key",
        "IX_materials_object_key",
        "IX_exercise_attachments_object_key",
        "IX_submission_attachments_object_key",
        "IX_chat_attachments_object_key"
    ];

    public static bool IsBusinessUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: not null
        } postgres && BusinessUniqueConstraints.Contains(postgres.ConstraintName);
}
