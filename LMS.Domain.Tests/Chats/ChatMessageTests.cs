using LMS.Api.domain.Entities.Chats;

namespace LMS.Domain.Tests.Chats;

public class ChatMessageTests
{
    [Fact]
    public void TextMessageAcceptsEmojiContent()
    {
        var message = ChatMessage.CreateText(Guid.NewGuid(), Guid.NewGuid(), "Hello 👋");

        Assert.Equal("Hello 👋", message.Content);
        Assert.Empty(message.Attachments);
    }

    [Fact]
    public void EmptyTextMessageIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            ChatMessage.CreateText(Guid.NewGuid(), Guid.NewGuid(), "   "));
    }

    [Fact]
    public void AttachmentMessageAcceptsFileSmallerThanTwentyMegabytes()
    {
        var message = ChatMessage.CreateWithAttachment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "image.png",
            "chat/image.png",
            "image/png",
            ChatAttachment.MaximumFileSizeInBytes - 1);

        Assert.Single(message.Attachments);
        Assert.Null(message.Content);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ChatAttachment.MaximumFileSizeInBytes)]
    public void InvalidAttachmentSizeIsRejected(long fileSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ChatMessage.CreateWithAttachment(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                "file.pdf",
                "chat/file.pdf",
                "application/pdf",
                fileSize));
    }

    [Fact]
    public void DeletedMessageCannotBeEdited()
    {
        var message = ChatMessage.CreateText(Guid.NewGuid(), Guid.NewGuid(), "Message");
        message.Delete();

        Assert.Throws<InvalidOperationException>(() => message.EditContent("Updated"));
    }

    [Fact]
    public void ContentLongerThanDatabaseColumnIsRejectedForCreateAndEdit()
    {
        var content = new string('x', ChatMessage.MaximumContentLength + 1);
        var message = ChatMessage.CreateText(Guid.NewGuid(), Guid.NewGuid(), "Message");

        Assert.Throws<ArgumentException>(() =>
            ChatMessage.CreateText(Guid.NewGuid(), Guid.NewGuid(), content));
        Assert.Throws<ArgumentException>(() => message.EditContent(content));
        Assert.Equal("Message", message.Content);
    }
}
