using LMS.Api.application.features.chats;
using LMS.Api.presentation.controllers;
using LMS.Api.presentation.hubs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace LMS.Domain.Tests.Presentation;

public sealed class ChatsControllerTests
{
    [Fact]
    public async Task DeleteDirectConversationReturnsNoContent()
    {
        var roomId = Guid.NewGuid();
        var service = new Mock<IChatService>();

        var result = await new ChatsController().DeleteDirectConversation(
            roomId,
            service.Object,
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        service.Verify(item => item.DeleteDirectConversationAsync(
            roomId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestTextMessageBroadcastsToRoomAfterSave()
    {
        var roomId = Guid.NewGuid();
        var message = new ChatMessageDto(
            Guid.NewGuid(),
            roomId,
            Guid.NewGuid(),
            "Student name",
            "Student",
            "Hello",
            null,
            DateTime.UtcNow,
            null,
            null,
            []);
        var service = new Mock<IChatService>();
        service.Setup(item => item.SendMessageAsync(
                roomId,
                It.IsAny<SendChatMessageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);
        var client = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(item => item.Group(ChatHub.GroupName(roomId))).Returns(client.Object);
        var hub = new Mock<IHubContext<ChatHub>>();
        hub.SetupGet(item => item.Clients).Returns(clients.Object);
        var controller = new ChatsController();

        var result = await controller.SendMessage(
            roomId,
            new SendChatMessageRequest("Hello", null),
            service.Object,
            hub.Object,
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, objectResult.StatusCode);
        Assert.Same(message, objectResult.Value);
        client.Verify(item => item.SendCoreAsync(
            "MessageReceived",
            It.Is<object?[]>(arguments => arguments.Length == 1 && ReferenceEquals(arguments[0], message)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EditMessageBroadcastsUpdatedPayload()
    {
        var roomId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var message = new ChatMessageDto(
            messageId,
            roomId,
            Guid.NewGuid(),
            "Teacher name",
            "Teacher",
            "Updated",
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            null,
            []);
        var service = new Mock<IChatService>();
        service.Setup(item => item.EditMessageAsync(
                messageId,
                It.IsAny<EditChatMessageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);
        var (hub, client) = CreateHub(roomId);

        var result = await new ChatsController().EditMessage(
            messageId,
            new EditChatMessageRequest("Updated"),
            service.Object,
            hub.Object,
            CancellationToken.None);

        Assert.Same(message, Assert.IsType<OkObjectResult>(result.Result).Value);
        client.Verify(item => item.SendCoreAsync(
            "MessageUpdated",
            It.Is<object?[]>(arguments => arguments.Length == 1 && ReferenceEquals(arguments[0], message)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteMessageBroadcastsMessageId()
    {
        var roomId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var service = new Mock<IChatService>();
        service.Setup(item => item.DeleteMessageAsync(
                messageId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(roomId);
        var (hub, client) = CreateHub(roomId);

        var result = await new ChatsController().DeleteMessage(
            messageId,
            service.Object,
            hub.Object,
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        client.Verify(item => item.SendCoreAsync(
            "MessageDeleted",
            It.Is<object?[]>(arguments => arguments.Length == 1 && Equals(arguments[0], messageId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (Mock<IHubContext<ChatHub>> Hub, Mock<IClientProxy> Client) CreateHub(Guid roomId)
    {
        var client = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(item => item.Group(ChatHub.GroupName(roomId))).Returns(client.Object);
        var hub = new Mock<IHubContext<ChatHub>>();
        hub.SetupGet(item => item.Clients).Returns(clients.Object);
        return (hub, client);
    }
}
