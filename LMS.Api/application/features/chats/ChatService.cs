using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.storage;
using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Chats;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Api.application.features.chats;

public sealed class ChatService : IChatService
{
    private readonly ICurrentUser _currentUser;
    private readonly IChatRoomRepository _rooms;
    private readonly IChatMessageRepository _messages;
    private readonly ICourseRepository _courses;
    private readonly IEnrollmentRepository _enrollments;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorage _fileStorage;
    private readonly IFileCleanupQueue? _fileCleanup;
    private readonly ITransactionManager? _transactionManager;
    private readonly ILogger<ChatService>? _logger;
    private readonly ITokenProvider _tokenProvider;

    public ChatService(
        ICurrentUser currentUser,
        IChatRoomRepository rooms,
        IChatMessageRepository messages,
        ICourseRepository courses,
        IEnrollmentRepository enrollments,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IFileStorage fileStorage,
        ITokenProvider tokenProvider,
        IFileCleanupQueue? fileCleanup = null,
        ITransactionManager? transactionManager = null,
        ILogger<ChatService>? logger = null)
    {
        _currentUser = currentUser;
        _rooms = rooms;
        _messages = messages;
        _courses = courses;
        _enrollments = enrollments;
        _users = users;
        _unitOfWork = unitOfWork;
        _fileStorage = fileStorage;
        _fileCleanup = fileCleanup;
        _transactionManager = transactionManager;
        _logger = logger;
        _tokenProvider = tokenProvider;
    }

    public async Task<ChatRealtimeTokenDto> CreateRealtimeTokenAsync(
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizeException("User authentication is no longer valid.");
        if (user.Status != AccountStatus.Active)
        {
            throw new UnauthorizeException("Account is not active.");
        }

        var token = _tokenProvider.CreateChatRealtimeToken(
            user.Id,
            user.Email,
            user.Role.ToString(),
            user.AuthVersion);
        return new ChatRealtimeTokenDto(token.Token, token.ExpiresAt);
    }

    public async Task<IReadOnlyList<ChatRoomDto>> ListRoomsAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var rooms = await _rooms.ListForUserAsync(userId, cancellationToken);

        var courseRoomIds = rooms
            .Where(room => room.Type == ChatRoomType.Course && room.CourseId is not null)
            .Select(room => room.CourseId!.Value)
            .Distinct()
            .ToArray();

        IReadOnlyDictionary<Guid, Course> coursesById = new Dictionary<Guid, Course>();
        var enrollmentsByCourseId = new Dictionary<Guid, Enrollment>();
        if (courseRoomIds.Length > 0)
        {
            var courses = await _courses.ListByIdsAsync(courseRoomIds, cancellationToken);
            coursesById = courses.ToDictionary(course => course.Id);
            var role = _currentUser.GetRequiredRole();
            if (role != Role.Admin)
            {
                var enrollments = await _enrollments.ListByStudentAsync(userId, cancellationToken);
                foreach (var enrollment in enrollments.Where(enrollment => enrollment.Status == EnrollmentStatus.Accepted))
                {
                    enrollmentsByCourseId[enrollment.CourseId] = enrollment;
                }
            }
        }

        var directOtherUserIds = rooms
            .Where(room => room.Type == ChatRoomType.DirectMessage)
            .SelectMany(room => room.Participants)
            .Where(participant => participant.UserId != userId)
            .Select(participant => participant.UserId)
            .Distinct()
            .ToArray();

        var directUsers = await _users.ListByIdsAsync(directOtherUserIds, cancellationToken);
        var directUsersById = directUsers.ToDictionary(user => user.Id);

        var accessibleRooms = new List<ChatRoomDto>(rooms.Count);
        var courseLifecycleChanged = false;
        foreach (var room in rooms)
        {
            if (room.Type == ChatRoomType.Course)
            {
                if (room.CourseId is null || !coursesById.TryGetValue(room.CourseId.Value, out var course))
                {
                    continue;
                }

                if (room.SynchronizeWithCourse(course))
                {
                    courseLifecycleChanged = true;
                }

                if (course.IsDeleted)
                {
                    continue;
                }

                var role = _currentUser.GetRequiredRole();
                var canManageCourse = role == Role.Admin || role == Role.Teacher && course.TeacherId == userId;
                var hasAcceptedEnrollment = role == Role.Student &&
                    enrollmentsByCourseId.TryGetValue(course.Id, out var enrollment) &&
                    enrollment.StudentId == userId &&
                    enrollment.CourseId == course.Id &&
                    enrollment.Status == EnrollmentStatus.Accepted;
                if (!canManageCourse && !hasAcceptedEnrollment)
                {
                    continue;
                }

                accessibleRooms.Add(MapRoom(room, course.Name));
            }
            else
            {
                var otherId = room.Participants.FirstOrDefault(p => p.UserId != userId)?.UserId;
                User? directUser = null;
                if (otherId.HasValue)
                {
                    directUsersById.TryGetValue(otherId.Value, out directUser);
                }

                accessibleRooms.Add(MapRoom(room, directUser?.FullName, directUser?.FullName, directUser?.Email));
            }
        }

        if (courseLifecycleChanged)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var unreadRoomIds = await _messages.ListUnreadRoomIdsAsync(
            userId,
            accessibleRooms.Select(room => room.Id).ToArray(),
            cancellationToken);
        var latestMessages = await _messages.ListLatestByRoomAsync(
            accessibleRooms.Select(room => room.Id).ToArray(),
            cancellationToken);
        return accessibleRooms
            .Select(room => room with
            {
                HasUnread = unreadRoomIds.Contains(room.Id),
                LatestMessage = latestMessages.TryGetValue(room.Id, out var latest)
                    ? new ChatRoomLatestMessageDto(latest.Id, latest.SenderId, latest.Content, latest.CreatedAt, latest.HasAttachments)
                    : null
            })
            .OrderByDescending(room => room.LatestMessage?.CreatedAt ?? room.CreatedAt)
            .ThenByDescending(room => room.CreatedAt)
            .ToArray();
    }

    public async Task<ChatRoomDto> GetOrCreateCourseRoomAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var course = await GetAccessibleCourseAsync(courseId, cancellationToken);
        var userId = _currentUser.GetRequiredUserId();
        var room = await _rooms.GetByCourseIdAsync(courseId, cancellationToken);
        if (room is null)
        {
            room = ChatRoom.CreateForCourse(courseId, userId);
            room.AddCourseParticipant(userId);
            if (course.TeacherId != userId)
            {
                room.AddCourseParticipant(course.TeacherId);
            }
            room.SynchronizeWithCourse(course);
            _rooms.Add(room);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        else
        {
            var lifecycleChanged = room.SynchronizeWithCourse(course);
            if (lifecycleChanged)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            if (!room.IsArchived)
            {
                room = await _rooms.EnsureCourseParticipantAsync(room.Id, userId, cancellationToken)
                    ?? throw new NotFoundException("The chat room was not found.");
                room = await _rooms.EnsureCourseParticipantAsync(room.Id, course.TeacherId, cancellationToken)
                    ?? throw new NotFoundException("The chat room was not found.");
            }
        }

        return MapRoom(room, course.Name);
    }

    public async Task<ChatRoomDto> GetOrCreateDirectRoomAsync(Guid otherUserId, CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        if (userId == otherUserId)
        {
            throw new BadRequestException("A direct chat requires another user.");
        }

        var otherUser = await _users.GetByIdAsync(otherUserId, cancellationToken)
            ?? throw new NotFoundException("The other user was not found.");
        if (otherUser.Status != AccountStatus.Active)
        {
            throw new ForbiddenException("Direct chat is available only for active users.");
        }

        var key = ChatRoom.BuildDirectMessageKey(userId, otherUserId);
        var room = await _rooms.GetByDirectMessageKeyAsync(key, cancellationToken);
        if (room is null)
        {
            room = ChatRoom.CreateDirectMessage(userId, otherUserId);
            _rooms.Add(room);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return MapRoom(room, otherUser.FullName, otherUser.FullName, otherUser.Email);
    }

    public async Task<IReadOnlyList<ChatMessageDto>> ListMessagesAsync(Guid roomId, ChatPageQuery query, CancellationToken cancellationToken)
    {
        await EnsureRoomAccessAsync(roomId, cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var messages = await _messages.ListByRoomAsync(roomId, (page - 1) * pageSize, pageSize, cancellationToken);
        var visibleMessages = messages.Where(message => !message.IsDeleted).ToArray();
        var senderIds = visibleMessages.Select(message => message.SenderId).Distinct().ToArray();
        var senders = await _users.ListByIdsAsync(senderIds, cancellationToken);
        var sendersById = senders.ToDictionary(user => user.Id);
        return visibleMessages
            .Select(message => MapMessage(
                message,
                sendersById.GetValueOrDefault(message.SenderId)))
            .ToList();
    }

    public async Task EnsureRoomAccessAsync(Guid roomId, CancellationToken cancellationToken)
    {
        await EnsureRoomAccessInternalAsync(roomId, cancellationToken);
    }

    public async Task EnsureRoomJoinAccessAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var room = await EnsureRoomAccessInternalAsync(roomId, cancellationToken);
        if (room.IsArchived)
        {
            throw new ConflictException("Archived chat rooms cannot be joined.");
        }
    }

    public async Task<ChatRoomDto> JoinRoomAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var room = await EnsureRoomAccessInternalAsync(roomId, cancellationToken);
        if (room.IsArchived)
        {
            throw new ConflictException("Archived chat rooms cannot be joined.");
        }

        if (room.Type == ChatRoomType.Course && room.CourseId.HasValue)
        {
            var course = await _courses.GetByIdAsync(room.CourseId.Value, true, cancellationToken);
            return MapRoom(room, course?.Name);
        }

        return MapRoom(room);
    }

    public async Task<ChatMessageDto> SendMessageAsync(Guid roomId, SendChatMessageRequest request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var room = await EnsureRoomAccessInternalAsync(roomId, cancellationToken);
        if (room.IsArchived)
        {
            throw new ConflictException("Archived chat rooms do not accept new messages.");
        }

        if (request.ReplyToMessageId is not null)
        {
            var reply = await _messages.GetByIdAsync(request.ReplyToMessageId.Value, cancellationToken);
            if (reply is null || reply.ChatRoomId != roomId || reply.IsDeleted)
            {
                throw new BadRequestException("The reply message is invalid for this room.");
            }
        }

        ChatMessage message;
        try
        {
            message = ChatMessage.CreateText(roomId, userId, request.Content ?? string.Empty, request.ReplyToMessageId);
        }
        catch (ArgumentException exception)
        {
            throw new BadRequestException(exception.Message);
        }

        _messages.Add(message);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        var sender = await _users.GetByIdAsync(userId, cancellationToken);
        return MapMessage(message, sender);
    }

    public async Task<ChatMessageDto> SendMessageWithAttachmentAsync(Guid roomId, string? content, Guid? replyToMessageId, string fileName, string contentType, long length, Stream stream, CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var room = await EnsureRoomAccessInternalAsync(roomId, cancellationToken);
        if (room.IsArchived) throw new ConflictException("Archived chat rooms do not accept new messages.");
        ValidateMessageContent(content);
        if (replyToMessageId is not null)
        {
            var reply = await _messages.GetByIdAsync(replyToMessageId.Value, cancellationToken);
            if (reply is null || reply.ChatRoomId != roomId || reply.IsDeleted) throw new BadRequestException("The reply message is invalid for this room.");
        }

        string? uploadedObjectKey = null;
        try
        {
            var storedFile = await _fileStorage.UploadAsync(new FileUpload(fileName, contentType, length, stream), $"chat/{roomId:N}", ChatAttachment.MaximumFileSizeInBytes, cancellationToken);
            uploadedObjectKey = storedFile.ObjectKey;
            var message = ChatMessage.CreateWithAttachment(roomId, userId, content, storedFile.FileName, storedFile.ObjectKey, storedFile.ContentType, storedFile.FileSize, replyToMessageId);
            _messages.Add(message);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var sender = await _users.GetByIdAsync(userId, cancellationToken);
            return MapMessage(message, sender);
        }
        catch
        {
            if (uploadedObjectKey is not null)
            {
                await CleanupFileBestEffortAsync(uploadedObjectKey);
            }
            throw;
        }
    }

    private async Task CleanupFileBestEffortAsync(string objectKey)
    {
        if (_fileCleanup is not null)
        {
            try
            {
                await _fileCleanup.EnqueueImmediatelyAsync(objectKey, CancellationToken.None);
                return;
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(
                    exception,
                    "Could not enqueue file cleanup for {ObjectKey}; falling back to direct deletion.",
                    objectKey);
            }
        }

        try
        {
            await _fileStorage.DeleteIfExistsAsync(objectKey, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Best-effort file cleanup failed for {ObjectKey}.", objectKey);
        }
    }

    public async Task<FileContent> GetAttachmentContentAsync(
        Guid messageId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var message = await _messages.GetByIdAsync(messageId, cancellationToken)
            ?? throw new NotFoundException("The chat message was not found.");
        await EnsureRoomAccessInternalAsync(message.ChatRoomId, cancellationToken);
        if (message.IsDeleted)
        {
            throw new NotFoundException("The chat attachment was not found.");
        }

        var attachment = message.Attachments.SingleOrDefault(item => item.Id == attachmentId)
            ?? throw new NotFoundException("The chat attachment was not found.");
        var content = await _fileStorage.OpenReadAsync(attachment.ObjectKey, cancellationToken);
        return new FileContent(content, attachment.FileName, attachment.ContentType);
    }

    public async Task<ChatMessageDto> EditMessageAsync(Guid messageId, EditChatMessageRequest request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var message = await _messages.GetByIdAsync(messageId, cancellationToken)
            ?? throw new NotFoundException("The chat message was not found.");
        var room = await EnsureRoomAccessInternalAsync(message.ChatRoomId, cancellationToken);
        if (message.SenderId != userId)
        {
            throw new ForbiddenException("Only the message sender can edit this message.");
        }
        if (room.IsArchived)
        {
            throw new ConflictException("Archived chat rooms cannot be modified.");
        }

        try
        {
            message.EditContent(request.Content);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new BadRequestException(exception.Message);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        var sender = await _users.GetByIdAsync(userId, cancellationToken);
        return MapMessage(message, sender);
    }

    public async Task<Guid> DeleteMessageAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var message = await _messages.GetByIdAsync(messageId, cancellationToken)
            ?? throw new NotFoundException("The chat message was not found.");
        var room = await EnsureRoomAccessInternalAsync(message.ChatRoomId, cancellationToken);
        var role = _currentUser.GetRequiredRole();
        if (message.SenderId != userId && !CanModerate(room, role))
        {
            throw new ForbiddenException("You cannot delete this message.");
        }

        message.Delete();
        foreach (var participant in room.Participants.Where(item => item.LastReadMessageId == message.Id))
        {
            participant.ClearReadReceipt();
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return message.ChatRoomId;
    }

    private static void ValidateMessageContent(string? content)
    {
        if (content is not null && content.Length > ChatMessage.MaximumContentLength)
        {
            throw new BadRequestException(
                $"Message content cannot exceed {ChatMessage.MaximumContentLength} characters.");
        }
    }

    public async Task MarkAsReadAsync(Guid roomId, Guid messageId, CancellationToken cancellationToken)
    {
        async Task MarkAsReadCore(CancellationToken transactionCancellationToken)
        {
            var userId = _currentUser.GetRequiredUserId();
            var room = await EnsureRoomAccessInternalAsync(roomId, transactionCancellationToken);
            var message = await _messages.GetByIdAsync(messageId, transactionCancellationToken)
                ?? throw new NotFoundException("The chat message was not found.");
            if (message.ChatRoomId != roomId || message.IsDeleted)
            {
                throw new BadRequestException("The read receipt target is invalid for this room.");
            }

            var participant = room.Participants.SingleOrDefault(item => item.UserId == userId)
                ?? throw new ConflictException("You are not a participant in this chat room.");
            if (participant.LastReadMessageId is not null)
            {
                var current = await _messages.GetByIdAsync(
                    participant.LastReadMessageId.Value,
                    transactionCancellationToken);
                if (current is not null &&
                    (current.CreatedAt > message.CreatedAt ||
                     current.CreatedAt == message.CreatedAt && current.Id.CompareTo(message.Id) >= 0))
                {
                    return;
                }
            }

            participant.MarkAsRead(messageId);
            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
        }

        if (_transactionManager is null)
        {
            await MarkAsReadCore(cancellationToken);
        }
        else
        {
            await _transactionManager.ExecuteSerializableAsync(MarkAsReadCore, cancellationToken);
        }
    }

    private async Task<Course> GetAccessibleCourseAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var course = await _courses.GetByIdAsync(courseId, true, cancellationToken)
            ?? throw new NotFoundException("The course was not found.");
        if (course.IsDeleted)
        {
            throw new NotFoundException("The course was not found.");
        }

        await EnsureCourseAuthorizationAsync(course, cancellationToken);
        return course;
    }

    private async Task<ChatRoom> EnsureRoomAccessInternalAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var room = await _rooms.GetByIdAsync(roomId, cancellationToken)
            ?? throw new NotFoundException("The chat room was not found.");
        if (room.Type == ChatRoomType.Course)
        {
            await EnsureCurrentCourseAccessAsync(room, cancellationToken);
            if (!room.IsArchived && room.Participants.All(participant => participant.UserId != userId))
            {
                room = await _rooms.EnsureCourseParticipantAsync(room.Id, userId, cancellationToken)
                    ?? throw new NotFoundException("The chat room was not found.");
            }
        }
        else if (!room.Participants.Any(participant => participant.UserId == userId))
        {
            throw new ForbiddenException("You are not a participant in this chat room.");
        }

        return room;
    }

    private async Task EnsureCurrentCourseAccessAsync(ChatRoom room, CancellationToken cancellationToken)
    {
        if (room.Type != ChatRoomType.Course)
        {
            return;
        }

        if (room.CourseId is null)
        {
            throw new NotFoundException("The course chat room is invalid.");
        }

        var course = await _courses.GetByIdAsync(room.CourseId.Value, true, cancellationToken)
            ?? throw new NotFoundException("The course was not found.");
        if (room.SynchronizeWithCourse(course))
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (course.IsDeleted)
        {
            throw new NotFoundException("The course was not found.");
        }

        await EnsureCourseAuthorizationAsync(course, cancellationToken);
    }

    private async Task EnsureCourseAuthorizationAsync(Course course, CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var role = _currentUser.GetRequiredRole();
        if (role == Role.Admin || course.TeacherId == userId)
        {
            return;
        }

        var enrollment = await _enrollments.GetByStudentAndCourseAsync(userId, course.Id, cancellationToken);
        if (role != Role.Student ||
            enrollment?.StudentId != userId ||
            enrollment.CourseId != course.Id ||
            enrollment.Status != EnrollmentStatus.Accepted)
        {
            throw new ForbiddenException("Only the course teacher or an accepted student can access course chat.");
        }
    }

    private static bool CanModerate(ChatRoom room, Role role) => role == Role.Admin || room.Type == ChatRoomType.Course && role == Role.Teacher;
    private static ChatRoomDto MapRoom(
        ChatRoom room,
        string? title = null,
        string? directParticipantName = null,
        string? directParticipantEmail = null) =>
        new(
            room.Id,
            room.Type.ToString(),
            room.CourseId,
            room.Participants.Select(participant => participant.UserId).ToArray(),
            room.CreatedAt,
            room.ArchivedAt,
            title,
            directParticipantName,
            directParticipantEmail);
    private static ChatMessageDto MapMessage(ChatMessage message, User? sender) => new(
        message.Id,
        message.ChatRoomId,
        message.SenderId,
        sender?.FullName,
        sender?.Role.ToString(),
        message.IsDeleted ? null : message.Content,
        message.ReplyToMessageId,
        message.CreatedAt,
        message.EditedAt,
        message.DeletedAt,
        message.IsDeleted
            ? []
            : message.Attachments
                .Select(attachment => new ChatAttachmentDto(
                    attachment.Id,
                    attachment.FileName,
                    attachment.ContentType,
                    attachment.FileSize))
                .ToArray());
}
