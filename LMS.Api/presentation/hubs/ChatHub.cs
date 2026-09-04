using LMS.Api.application.features.chats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace LMS.Api.presentation.hubs;

[Authorize]
public sealed class ChatHub : Hub
{
    private readonly IChatService _chatService;

    public ChatHub(IChatService chatService)
    {
        _chatService = chatService;
    }

    public async Task JoinRoom(Guid roomId)
    {
        await _chatService.EnsureRoomAccessAsync(roomId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(roomId), Context.ConnectionAborted);
    }

    public Task LeaveRoom(Guid roomId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(roomId), Context.ConnectionAborted);

    public async Task SendMessage(Guid roomId, SendChatMessageRequest request)
    {
        var message = await _chatService.SendMessageAsync(roomId, request, Context.ConnectionAborted);
        await Clients.Group(GroupName(roomId)).SendAsync("MessageReceived", message, Context.ConnectionAborted);
    }

    private static string GroupName(Guid roomId) => $"chat-room:{roomId:N}";
}