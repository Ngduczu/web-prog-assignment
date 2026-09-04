using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Api.infrastructure.persistence.migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    avatar_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.ForeignKey(
                        name: "FK_users_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_users_users_modified_by_id",
                        column: x => x.modified_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "courses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    max_students = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_courses", x => x.id);
                    table.CheckConstraint("ck_courses_max_students", "max_students > 0");
                    table.ForeignKey(
                        name: "FK_courses_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_courses_users_deleted_by_id",
                        column: x => x.deleted_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_courses_users_modified_by_id",
                        column: x => x.modified_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_courses_users_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "external_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    external_user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_users", x => x.id);
                    table.ForeignKey(
                        name: "FK_external_users_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "question_banks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_by_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_question_banks", x => x.id);
                    table.ForeignKey(
                        name: "FK_question_banks_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_question_banks_users_modified_by_id",
                        column: x => x.modified_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_question_banks_users_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "chat_rooms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: true),
                    direct_message_key = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_rooms", x => x.id);
                    table.CheckConstraint("ck_chat_rooms_type_reference", "(type = 'Course' AND course_id IS NOT NULL AND direct_message_key IS NULL) OR (type = 'DirectMessage' AND course_id IS NULL AND direct_message_key IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_chat_rooms_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_chat_rooms_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_enrollments", x => x.id);
                    table.ForeignKey(
                        name: "FK_enrollments_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_enrollments_users_student_id",
                        column: x => x.student_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "examinations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    shuffle_questions = table.Column<bool>(type: "boolean", nullable: false),
                    show_score_immediately = table.Column<bool>(type: "boolean", nullable: false),
                    require_fullscreen = table.Column<bool>(type: "boolean", nullable: false),
                    fullscreen_grace_seconds = table.Column<int>(type: "integer", nullable: false),
                    max_violations = table.Column<int>(type: "integer", nullable: false),
                    block_copy_paste = table.Column<bool>(type: "boolean", nullable: false),
                    block_right_click = table.Column<bool>(type: "boolean", nullable: false),
                    detect_tab_change = table.Column<bool>(type: "boolean", nullable: false),
                    detect_dev_tools = table.Column<bool>(type: "boolean", nullable: false),
                    max_disconnect_minutes = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_by_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_examinations", x => x.id);
                    table.CheckConstraint("ck_examinations_anti_cheat", "fullscreen_grace_seconds >= 0 AND max_violations >= 0 AND max_disconnect_minutes >= 0");
                    table.CheckConstraint("ck_examinations_duration", "duration_minutes > 0");
                    table.CheckConstraint("ck_examinations_schedule", "due_at > start_at");
                    table.ForeignKey(
                        name: "FK_examinations_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_examinations_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_examinations_users_modified_by_id",
                        column: x => x.modified_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exercises",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    due_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_by_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercises", x => x.id);
                    table.ForeignKey(
                        name: "FK_exercises_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_exercises_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exercises_users_modified_by_id",
                        column: x => x.modified_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lessons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    lesson_order = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_by_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lessons", x => x.id);
                    table.CheckConstraint("ck_lessons_order", "lesson_order >= 0");
                    table.ForeignKey(
                        name: "FK_lessons_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_lessons_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lessons_users_modified_by_id",
                        column: x => x.modified_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    reply_to_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    edited_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_chat_messages_chat_messages_reply_to_message_id",
                        column: x => x.reply_to_message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_chat_messages_chat_rooms_chat_room_id",
                        column: x => x.chat_room_id,
                        principalTable: "chat_rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chat_messages_users_sender_id",
                        column: x => x.sender_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "examination_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    examination_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    submit_reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    disconnected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    remaining_seconds = table.Column<int>(type: "integer", nullable: true),
                    last_activity_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    violation_count = table.Column<int>(type: "integer", nullable: false),
                    correct_answers = table.Column<int>(type: "integer", nullable: false),
                    total_questions = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_examination_attempts", x => x.id);
                    table.CheckConstraint("ck_examination_attempts_remaining_seconds", "remaining_seconds IS NULL OR remaining_seconds >= 0");
                    table.CheckConstraint("ck_examination_attempts_result", "correct_answers >= 0 AND total_questions >= 0 AND score >= 0");
                    table.ForeignKey(
                        name: "FK_examination_attempts_examinations_examination_id",
                        column: x => x.examination_id,
                        principalTable: "examinations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_examination_attempts_users_student_id",
                        column: x => x.student_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "examination_question_selection_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    examination_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_bank_id = table.Column<Guid>(type: "uuid", nullable: false),
                    difficulty = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    question_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_examination_question_selection_rules", x => x.id);
                    table.CheckConstraint("ck_examination_question_selection_rules_count", "question_count > 0");
                    table.ForeignKey(
                        name: "FK_examination_question_selection_rules_examinations_examinati~",
                        column: x => x.examination_id,
                        principalTable: "examinations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_examination_question_selection_rules_question_banks_questio~",
                        column: x => x.question_bank_id,
                        principalTable: "question_banks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exercise_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercise_attachments", x => x.id);
                    table.CheckConstraint("ck_exercise_attachments_file_size", "file_size > 0");
                    table.ForeignKey(
                        name: "FK_exercise_attachments_exercises_exercise_id",
                        column: x => x.exercise_id,
                        principalTable: "exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_exercise_attachments_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submissions", x => x.id);
                    table.ForeignKey(
                        name: "FK_submissions_exercises_exercise_id",
                        column: x => x.exercise_id,
                        principalTable: "exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_submissions_users_student_id",
                        column: x => x.student_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lesson_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_by_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_materials", x => x.id);
                    table.CheckConstraint("ck_materials_file_size", "file_size > 0");
                    table.ForeignKey(
                        name: "FK_materials_lessons_lesson_id",
                        column: x => x.lesson_id,
                        principalTable: "lessons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_materials_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_materials_users_modified_by_id",
                        column: x => x.modified_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "chat_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_attachments", x => x.id);
                    table.CheckConstraint("ck_chat_attachments_file_size", "file_size > 0 AND file_size < 20971520");
                    table.ForeignKey(
                        name: "FK_chat_attachments_chat_messages_chat_message_id",
                        column: x => x.chat_message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_read_message_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_participants", x => x.id);
                    table.ForeignKey(
                        name: "FK_chat_participants_chat_messages_last_read_message_id",
                        column: x => x.last_read_message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_chat_participants_chat_rooms_chat_room_id",
                        column: x => x.chat_room_id,
                        principalTable: "chat_rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chat_participants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "examination_violations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    examination_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_examination_violations", x => x.id);
                    table.ForeignKey(
                        name: "FK_examination_violations_examination_attempts_examination_att~",
                        column: x => x.examination_attempt_id,
                        principalTable: "examination_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "submission_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_attachments", x => x.id);
                    table.CheckConstraint("ck_submission_attachments_file_size", "file_size > 0");
                    table.ForeignKey(
                        name: "FK_submission_attachments_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "examination_fill_in_blank_answer_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    examination_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blank_order = table.Column<int>(type: "integer", nullable: false),
                    expected_answer = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_examination_fill_in_blank_answer_keys", x => x.id);
                    table.CheckConstraint("ck_examination_fill_in_blank_answer_keys_order", "blank_order >= 0");
                });

            migrationBuilder.CreateTable(
                name: "examination_multiple_choice_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    examination_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    option_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_examination_multiple_choice_options", x => x.id);
                    table.CheckConstraint("ck_examination_multiple_choice_options_order", "option_order >= 0");
                });

            migrationBuilder.CreateTable(
                name: "examination_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    examination_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_question_id = table.Column<Guid>(type: "uuid", nullable: true),
                    content = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    difficulty = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    shuffle_options = table.Column<bool>(type: "boolean", nullable: false),
                    question_order = table.Column<int>(type: "integer", nullable: false),
                    correct_option_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_examination_questions", x => x.id);
                    table.CheckConstraint("ck_examination_questions_order", "question_order >= 0");
                    table.ForeignKey(
                        name: "FK_examination_questions_examination_multiple_choice_options_c~",
                        column: x => x.correct_option_id,
                        principalTable: "examination_multiple_choice_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_examination_questions_examinations_examination_id",
                        column: x => x.examination_id,
                        principalTable: "examinations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "student_answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    examination_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    examination_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selected_option_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_student_answers", x => x.id);
                    table.ForeignKey(
                        name: "FK_student_answers_examination_attempts_examination_attempt_id",
                        column: x => x.examination_attempt_id,
                        principalTable: "examination_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_student_answers_examination_multiple_choice_options_selecte~",
                        column: x => x.selected_option_id,
                        principalTable: "examination_multiple_choice_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_student_answers_examination_questions_examination_question_~",
                        column: x => x.examination_question_id,
                        principalTable: "examination_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "student_fill_in_blank_answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_answer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blank_order = table.Column<int>(type: "integer", nullable: false),
                    answer_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_student_fill_in_blank_answers", x => x.id);
                    table.CheckConstraint("ck_student_fill_in_blank_answers_order", "blank_order >= 0");
                    table.ForeignKey(
                        name: "FK_student_fill_in_blank_answers_student_answers_student_answe~",
                        column: x => x.student_answer_id,
                        principalTable: "student_answers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fill_in_blank_answer_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blank_order = table.Column<int>(type: "integer", nullable: false),
                    expected_answer = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fill_in_blank_answer_keys", x => x.id);
                    table.CheckConstraint("ck_fill_in_blank_answer_keys_order", "blank_order >= 0");
                });

            migrationBuilder.CreateTable(
                name: "multiple_choice_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    option_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_multiple_choice_options", x => x.id);
                    table.CheckConstraint("ck_multiple_choice_options_order", "option_order >= 0");
                });

            migrationBuilder.CreateTable(
                name: "questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_bank_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    difficulty = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    shuffle_options = table.Column<bool>(type: "boolean", nullable: false),
                    correct_option_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_by_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_questions", x => x.id);
                    table.ForeignKey(
                        name: "FK_questions_multiple_choice_options_correct_option_id",
                        column: x => x.correct_option_id,
                        principalTable: "multiple_choice_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_questions_question_banks_question_bank_id",
                        column: x => x.question_bank_id,
                        principalTable: "question_banks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_questions_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_questions_users_modified_by_id",
                        column: x => x.modified_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_attachments_chat_message_id",
                table: "chat_attachments",
                column: "chat_message_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_attachments_object_key",
                table: "chat_attachments",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_chat_room_id_created_at_id",
                table: "chat_messages",
                columns: new[] { "chat_room_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_reply_to_message_id",
                table: "chat_messages",
                column: "reply_to_message_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_sender_id",
                table: "chat_messages",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_participants_chat_room_id_user_id",
                table: "chat_participants",
                columns: new[] { "chat_room_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chat_participants_last_read_message_id",
                table: "chat_participants",
                column: "last_read_message_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_participants_user_id",
                table: "chat_participants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_rooms_archived_at",
                table: "chat_rooms",
                column: "archived_at");

            migrationBuilder.CreateIndex(
                name: "IX_chat_rooms_course_id",
                table: "chat_rooms",
                column: "course_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chat_rooms_created_by_id",
                table: "chat_rooms",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_rooms_direct_message_key",
                table: "chat_rooms",
                column: "direct_message_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_courses_created_by_id",
                table: "courses",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_courses_deleted_at",
                table: "courses",
                column: "deleted_at");

            migrationBuilder.CreateIndex(
                name: "IX_courses_deleted_by_id",
                table: "courses",
                column: "deleted_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_courses_modified_by_id",
                table: "courses",
                column: "modified_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_courses_status",
                table: "courses",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_courses_teacher_id",
                table: "courses",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_course_id_status",
                table: "enrollments",
                columns: new[] { "course_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_student_id_course_id",
                table: "enrollments",
                columns: new[] { "student_id", "course_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_examination_attempts_examination_id_student_id",
                table: "examination_attempts",
                columns: new[] { "examination_id", "student_id" });

            migrationBuilder.CreateIndex(
                name: "IX_examination_attempts_status_last_activity_at",
                table: "examination_attempts",
                columns: new[] { "status", "last_activity_at" });

            migrationBuilder.CreateIndex(
                name: "IX_examination_attempts_student_id",
                table: "examination_attempts",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "IX_examination_fill_in_blank_answer_keys_examination_question_~",
                table: "examination_fill_in_blank_answer_keys",
                columns: new[] { "examination_question_id", "blank_order", "expected_answer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_examination_multiple_choice_options_examination_question_id~",
                table: "examination_multiple_choice_options",
                columns: new[] { "examination_question_id", "option_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_examination_question_selection_rules_examination_id_questio~",
                table: "examination_question_selection_rules",
                columns: new[] { "examination_id", "question_bank_id", "difficulty" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_examination_question_selection_rules_question_bank_id",
                table: "examination_question_selection_rules",
                column: "question_bank_id");

            migrationBuilder.CreateIndex(
                name: "IX_examination_questions_correct_option_id",
                table: "examination_questions",
                column: "correct_option_id");

            migrationBuilder.CreateIndex(
                name: "IX_examination_questions_examination_id_question_order",
                table: "examination_questions",
                columns: new[] { "examination_id", "question_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_examination_questions_source_question_id",
                table: "examination_questions",
                column: "source_question_id");

            migrationBuilder.CreateIndex(
                name: "IX_examination_violations_examination_attempt_id_occurred_at",
                table: "examination_violations",
                columns: new[] { "examination_attempt_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_examinations_course_id_status",
                table: "examinations",
                columns: new[] { "course_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_examinations_created_by_id",
                table: "examinations",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_examinations_modified_by_id",
                table: "examinations",
                column: "modified_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_examinations_start_at_due_at",
                table: "examinations",
                columns: new[] { "start_at", "due_at" });

            migrationBuilder.CreateIndex(
                name: "IX_exercise_attachments_created_by_id",
                table: "exercise_attachments",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_attachments_exercise_id",
                table: "exercise_attachments",
                column: "exercise_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exercise_attachments_object_key",
                table: "exercise_attachments",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exercises_course_id",
                table: "exercises",
                column: "course_id");

            migrationBuilder.CreateIndex(
                name: "IX_exercises_created_by_id",
                table: "exercises",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_exercises_modified_by_id",
                table: "exercises",
                column: "modified_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_external_users_provider_external_user_id",
                table: "external_users",
                columns: new[] { "provider", "external_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_users_user_id_provider",
                table: "external_users",
                columns: new[] { "user_id", "provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fill_in_blank_answer_keys_question_id_blank_order_expected_~",
                table: "fill_in_blank_answer_keys",
                columns: new[] { "question_id", "blank_order", "expected_answer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lessons_course_id_lesson_order",
                table: "lessons",
                columns: new[] { "course_id", "lesson_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lessons_created_by_id",
                table: "lessons",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_lessons_modified_by_id",
                table: "lessons",
                column: "modified_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_materials_created_by_id",
                table: "materials",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_materials_lesson_id",
                table: "materials",
                column: "lesson_id");

            migrationBuilder.CreateIndex(
                name: "IX_materials_modified_by_id",
                table: "materials",
                column: "modified_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_materials_object_key",
                table: "materials",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_multiple_choice_options_question_id_option_order",
                table: "multiple_choice_options",
                columns: new[] { "question_id", "option_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_question_banks_created_by_id",
                table: "question_banks",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_question_banks_modified_by_id",
                table: "question_banks",
                column: "modified_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_question_banks_teacher_id",
                table: "question_banks",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "IX_questions_correct_option_id",
                table: "questions",
                column: "correct_option_id");

            migrationBuilder.CreateIndex(
                name: "IX_questions_created_by_id",
                table: "questions",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_questions_modified_by_id",
                table: "questions",
                column: "modified_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_questions_question_bank_id_type_difficulty",
                table: "questions",
                columns: new[] { "question_bank_id", "type", "difficulty" });

            migrationBuilder.CreateIndex(
                name: "IX_student_answers_examination_attempt_id_examination_question~",
                table: "student_answers",
                columns: new[] { "examination_attempt_id", "examination_question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_student_answers_examination_question_id",
                table: "student_answers",
                column: "examination_question_id");

            migrationBuilder.CreateIndex(
                name: "IX_student_answers_selected_option_id",
                table: "student_answers",
                column: "selected_option_id");

            migrationBuilder.CreateIndex(
                name: "IX_student_fill_in_blank_answers_student_answer_id_blank_order",
                table: "student_fill_in_blank_answers",
                columns: new[] { "student_answer_id", "blank_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submission_attachments_object_key",
                table: "submission_attachments",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submission_attachments_submission_id",
                table: "submission_attachments",
                column: "submission_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submissions_exercise_id_student_id",
                table: "submissions",
                columns: new[] { "exercise_id", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submissions_student_id",
                table: "submissions",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_created_by_id",
                table: "users",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_modified_by_id",
                table: "users",
                column: "modified_by_id");

            migrationBuilder.AddForeignKey(
                name: "FK_examination_fill_in_blank_answer_keys_examination_questions~",
                table: "examination_fill_in_blank_answer_keys",
                column: "examination_question_id",
                principalTable: "examination_questions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_examination_multiple_choice_options_examination_questions_e~",
                table: "examination_multiple_choice_options",
                column: "examination_question_id",
                principalTable: "examination_questions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_fill_in_blank_answer_keys_questions_question_id",
                table: "fill_in_blank_answer_keys",
                column: "question_id",
                principalTable: "questions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_multiple_choice_options_questions_question_id",
                table: "multiple_choice_options",
                column: "question_id",
                principalTable: "questions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_courses_users_created_by_id",
                table: "courses");

            migrationBuilder.DropForeignKey(
                name: "FK_courses_users_deleted_by_id",
                table: "courses");

            migrationBuilder.DropForeignKey(
                name: "FK_courses_users_modified_by_id",
                table: "courses");

            migrationBuilder.DropForeignKey(
                name: "FK_courses_users_teacher_id",
                table: "courses");

            migrationBuilder.DropForeignKey(
                name: "FK_examinations_users_created_by_id",
                table: "examinations");

            migrationBuilder.DropForeignKey(
                name: "FK_examinations_users_modified_by_id",
                table: "examinations");

            migrationBuilder.DropForeignKey(
                name: "FK_question_banks_users_created_by_id",
                table: "question_banks");

            migrationBuilder.DropForeignKey(
                name: "FK_question_banks_users_modified_by_id",
                table: "question_banks");

            migrationBuilder.DropForeignKey(
                name: "FK_question_banks_users_teacher_id",
                table: "question_banks");

            migrationBuilder.DropForeignKey(
                name: "FK_questions_users_created_by_id",
                table: "questions");

            migrationBuilder.DropForeignKey(
                name: "FK_questions_users_modified_by_id",
                table: "questions");

            migrationBuilder.DropForeignKey(
                name: "FK_examinations_courses_course_id",
                table: "examinations");

            migrationBuilder.DropForeignKey(
                name: "FK_examination_questions_examinations_examination_id",
                table: "examination_questions");

            migrationBuilder.DropForeignKey(
                name: "FK_examination_multiple_choice_options_examination_questions_e~",
                table: "examination_multiple_choice_options");

            migrationBuilder.DropForeignKey(
                name: "FK_questions_question_banks_question_bank_id",
                table: "questions");

            migrationBuilder.DropForeignKey(
                name: "FK_multiple_choice_options_questions_question_id",
                table: "multiple_choice_options");

            migrationBuilder.DropTable(
                name: "chat_attachments");

            migrationBuilder.DropTable(
                name: "chat_participants");

            migrationBuilder.DropTable(
                name: "enrollments");

            migrationBuilder.DropTable(
                name: "examination_fill_in_blank_answer_keys");

            migrationBuilder.DropTable(
                name: "examination_question_selection_rules");

            migrationBuilder.DropTable(
                name: "examination_violations");

            migrationBuilder.DropTable(
                name: "exercise_attachments");

            migrationBuilder.DropTable(
                name: "external_users");

            migrationBuilder.DropTable(
                name: "fill_in_blank_answer_keys");

            migrationBuilder.DropTable(
                name: "materials");

            migrationBuilder.DropTable(
                name: "student_fill_in_blank_answers");

            migrationBuilder.DropTable(
                name: "submission_attachments");

            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropTable(
                name: "lessons");

            migrationBuilder.DropTable(
                name: "student_answers");

            migrationBuilder.DropTable(
                name: "submissions");

            migrationBuilder.DropTable(
                name: "chat_rooms");

            migrationBuilder.DropTable(
                name: "examination_attempts");

            migrationBuilder.DropTable(
                name: "exercises");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "courses");

            migrationBuilder.DropTable(
                name: "examinations");

            migrationBuilder.DropTable(
                name: "examination_questions");

            migrationBuilder.DropTable(
                name: "examination_multiple_choice_options");

            migrationBuilder.DropTable(
                name: "question_banks");

            migrationBuilder.DropTable(
                name: "questions");

            migrationBuilder.DropTable(
                name: "multiple_choice_options");
        }
    }
}
