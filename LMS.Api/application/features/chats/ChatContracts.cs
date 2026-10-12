using LMS.Api.application.common;
using LMS.Api.application.abstractions.storage;

namespace LMS.Api.application.features.chats;

public sealed record ChatRoomDto(
    Guid Id,
    string Type,
    Guid? CourseId,
    Guid[] ParticipantIds,
    DateTime CreatedAt,
    DateTime? ArchivedAt,
    string? Title = null,
    string? DirectParticipantName = null,
    string? DirectParticipantEmail = null,
    bool HasUnread = false,
    ChatRoomLatestMessageDto? LatestMessage = null);
public sealed record ChatRoomLatestMessageDto(Guid Id, Guid SenderId, string? Content, DateTime CreatedAt, bool HasAttachments);
public sealed record ChatAttachmentDto(Guid Id, string FileName, string ContentType, long FileSize);
public sealed record ChatMessageDto(Guid Id, Guid ChatRoomId, Guid SenderId, string? SenderFullName, string? SenderRole, string? Content, Guid? ReplyToMessageId, DateTime CreatedAt, DateTime? EditedAt, DateTime? DeletedAt, IReadOnlyList<ChatAttachmentDto> Attachments);
public sealed record SendChatMessageRequest(string? Content, Guid? ReplyToMessageId);
public sealed record EditChatMessageRequest(string? Content);
public sealed record ChatPageQuery(int Page = 1, int PageSize = 20);
public sealed record ChatRealtimeTokenDto(string AccessToken, DateTime ExpiresAt);
public interface IChatService
{
    Task<IReadOnlyList<ChatRoomDto>> ListRoomsAsync(CancellationToken cancellationToken);
    Task<ChatRealtimeTokenDto> CreateRealtimeTokenAsync(CancellationToken cancellationToken);
    Task<ChatRoomDto> GetOrCreateCourseRoomAsync(Guid courseId, CancellationToken cancellationToken);
    Task<ChatRoomDto> GetOrCreateDirectRoomAsync(Guid otherUserId, CancellationToken cancellationToken);
    Task DeleteDirectConversationAsync(Guid roomId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessageDto>> ListMessagesAsync(Guid roomId, ChatPageQuery query, CancellationToken cancellationToken);
    Task EnsureRoomAccessAsync(Guid roomId, CancellationToken cancellationToken);
    Task EnsureRoomJoinAccessAsync(Guid roomId, CancellationToken cancellationToken);
    Task<ChatRoomDto> JoinRoomAsync(Guid roomId, CancellationToken cancellationToken);
    Task<ChatMessageDto> SendMessageAsync(Guid roomId, SendChatMessageRequest request, CancellationToken cancellationToken);
    Task<ChatMessageDto> SendMessageWithAttachmentAsync(Guid roomId, string? content, Guid? replyToMessageId, string fileName, string contentType, long length, Stream stream, CancellationToken cancellationToken);
    Task<FileContent> GetAttachmentContentAsync(Guid messageId, Guid attachmentId, CancellationToken cancellationToken);
    Task<ChatMessageDto> EditMessageAsync(Guid messageId, EditChatMessageRequest request, CancellationToken cancellationToken);
    Task<Guid> DeleteMessageAsync(Guid messageId, CancellationToken cancellationToken);
    Task MarkAsReadAsync(Guid roomId, Guid messageId, CancellationToken cancellationToken);
}
