using LMS.Api.application.features.chats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using LMS.Api.presentation.hubs;

namespace LMS.Api.presentation.controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public sealed class ChatsController : ControllerBase
{
    [HttpGet("rooms")]
    public async Task<ActionResult<IReadOnlyList<ChatRoomDto>>> ListRooms(
        [FromServices] IChatService service,
        CancellationToken cancellationToken) => Ok(await service.ListRoomsAsync(cancellationToken));

    [HttpPost("courses/{courseId:guid}")]
    public async Task<ActionResult<ChatRoomDto>> GetOrCreateCourseRoom(
        Guid courseId,
        [FromServices] IChatService service,
        CancellationToken cancellationToken) => Ok(await service.GetOrCreateCourseRoomAsync(courseId, cancellationToken));

    [HttpPost("direct/{userId:guid}")]
    public async Task<ActionResult<ChatRoomDto>> GetOrCreateDirectRoom(
        Guid userId,
        [FromServices] IChatService service,
        CancellationToken cancellationToken) => Ok(await service.GetOrCreateDirectRoomAsync(userId, cancellationToken));

    [HttpGet("rooms/{roomId:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> ListMessages(
        Guid roomId,
        [FromQuery] ChatPageQuery query,
        [FromServices] IChatService service,
        CancellationToken cancellationToken) => Ok(await service.ListMessagesAsync(roomId, query, cancellationToken));

    [HttpPost("rooms/{roomId:guid}/messages")]
    public async Task<ActionResult<ChatMessageDto>> SendMessage(
        Guid roomId,
        [FromBody] SendChatMessageRequest request,
        [FromServices] IChatService service,
        [FromServices] IHubContext<ChatHub> hub,
        CancellationToken cancellationToken)
    {
        var message = await service.SendMessageAsync(roomId, request, cancellationToken);
        await hub.Clients.Group(ChatHub.GroupName(roomId))
            .SendAsync("MessageReceived", message, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, message);
    }

    [HttpPost("rooms/{roomId:guid}/messages/attachment")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<ChatMessageDto>> SendMessageWithAttachment(
        Guid roomId,
        [FromForm] string? content,
        [FromForm] Guid? replyToMessageId,
        IFormFile file,
        [FromServices] IChatService service,
        [FromServices] IHubContext<ChatHub> hub,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0) return BadRequest("A non-empty file is required.");
        var message = await service.SendMessageWithAttachmentAsync(roomId, content, replyToMessageId, file.FileName, file.ContentType, file.Length, file.OpenReadStream(), cancellationToken);
        await hub.Clients.Group(ChatHub.GroupName(roomId)).SendAsync("MessageReceived", message, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, message);
    }

    [HttpGet("messages/{messageId:guid}/attachments/{attachmentId:guid}/content")]
    public async Task<FileStreamResult> GetAttachmentContent(
        Guid messageId,
        Guid attachmentId,
        [FromQuery] bool download,
        [FromServices] IChatService service,
        CancellationToken cancellationToken)
    {
        var file = await service.GetAttachmentContentAsync(messageId, attachmentId, cancellationToken);
        return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true);
    }

    [HttpPatch("messages/{messageId:guid}")]
    public async Task<ActionResult<ChatMessageDto>> EditMessage(
        Guid messageId,
        [FromBody] EditChatMessageRequest request,
        [FromServices] IChatService service,
        CancellationToken cancellationToken) => Ok(await service.EditMessageAsync(messageId, request, cancellationToken));

    [HttpDelete("messages/{messageId:guid}")]
    public async Task<IActionResult> DeleteMessage(
        Guid messageId,
        [FromServices] IChatService service,
        CancellationToken cancellationToken)
    {
        await service.DeleteMessageAsync(messageId, cancellationToken);
        return NoContent();
    }

    [HttpPost("rooms/{roomId:guid}/read/{messageId:guid}")]
    public async Task<IActionResult> MarkAsRead(
        Guid roomId,
        Guid messageId,
        [FromServices] IChatService service,
        CancellationToken cancellationToken)
    {
        await service.MarkAsReadAsync(roomId, messageId, cancellationToken);
        return NoContent();
    }
}
