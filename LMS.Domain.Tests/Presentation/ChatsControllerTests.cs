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
    public async Task RestTextMessageBroadcastsToRoomAfterSave()
    {
        var roomId = Guid.NewGuid();
        var message = new ChatMessageDto(
            Guid.NewGuid(),
            roomId,
            Guid.NewGuid(),
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
}
