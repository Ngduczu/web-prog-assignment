using LMS.Api.application.common;

namespace LMS.Api.application.features.chats;

public sealed record ChatRoomDto(Guid Id, string Type, Guid? CourseId, Guid[] ParticipantIds, DateTime CreatedAt, DateTime? ArchivedAt);
public sealed record ChatAttachmentDto(Guid Id, string FileName, string ObjectKey, string ContentType, long FileSize);
public sealed record ChatMessageDto(Guid Id, Guid ChatRoomId, Guid SenderId, string? Content, Guid? ReplyToMessageId, DateTime CreatedAt, DateTime? EditedAt, DateTime? DeletedAt, IReadOnlyList<ChatAttachmentDto> Attachments);
public sealed record SendChatMessageRequest(string? Content, Guid? ReplyToMessageId);
public sealed record EditChatMessageRequest(string? Content);
public sealed record ChatPageQuery(int Page = 1, int PageSize = 20);
public interface IChatService
{
    Task<IReadOnlyList<ChatRoomDto>> ListRoomsAsync(CancellationToken cancellationToken);
    Task<ChatRoomDto> GetOrCreateCourseRoomAsync(Guid courseId, CancellationToken cancellationToken);
    Task<ChatRoomDto> GetOrCreateDirectRoomAsync(Guid otherUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessageDto>> ListMessagesAsync(Guid roomId, ChatPageQuery query, CancellationToken cancellationToken);
    Task EnsureRoomAccessAsync(Guid roomId, CancellationToken cancellationToken);
    Task<ChatMessageDto> SendMessageAsync(Guid roomId, SendChatMessageRequest request, CancellationToken cancellationToken);
    Task<ChatMessageDto> SendMessageWithAttachmentAsync(Guid roomId, string? content, Guid? replyToMessageId, string fileName, string contentType, long length, Stream stream, CancellationToken cancellationToken);
    Task<ChatMessageDto> EditMessageAsync(Guid messageId, EditChatMessageRequest request, CancellationToken cancellationToken);
    Task DeleteMessageAsync(Guid messageId, CancellationToken cancellationToken);
    Task MarkAsReadAsync(Guid roomId, Guid messageId, CancellationToken cancellationToken);
}