using MassTransit;
using Messenger.API.Consumers;
using Messenger.Application.DTOs;
using Messenger.Application.DTOs.Messages;
using Messenger.Application.Interfaces;
using Messenger.Application.Messages;
using Messenger.Core.Enums;
using Messenger.Realtime.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Messenger.Tests.Api;

public sealed class ChatMessageSentConsumerTests
{
    [Fact]
    public async Task Consume_WhenPushDeliveryFails_MarksMessageFailedAndRethrowsForBrokerRetry()
    {
        var messageId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var message = new ChatMessageSent
        {
            MessageId = messageId,
            ChatId = chatId,
            SenderId = senderId,
            SenderName = "Test sender",
            MessageText = null,
            SentAt = DateTime.UtcNow
        };

        var savedMessage = new MessageDto
        {
            MessageId = messageId,
            ChatId = chatId,
            SenderId = senderId,
            SentAt = message.SentAt,
            SequenceNumber = 1
        };

        var messages = new Mock<IMessageService>();
        messages.Setup(service => service.SendMessageAsync(
                messageId, chatId, senderId, null, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<MessageDto>.Success(savedMessage));
        messages.Setup(service => service.UpdateDeliveryStatusAsync(
                messageId, It.IsAny<MessageDeliveryStatus>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var push = new Mock<IPushSubscriptionService>();
        push.Setup(service => service.SendPushForMessageAsync(messageId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Push provider unavailable"));

        var proxy = new Mock<IClientProxy>();
        proxy.Setup(client => client.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(client => client.Group(It.IsAny<string>())).Returns(proxy.Object);
        var hub = new Mock<IHubContext<ChatHub>>();
        hub.SetupGet(value => value.Clients).Returns(clients.Object);

        var consumeContext = new Mock<ConsumeContext<ChatMessageSent>>();
        consumeContext.SetupGet(value => value.Message).Returns(message);
        consumeContext.SetupGet(value => value.CancellationToken).Returns(CancellationToken.None);

        var consumer = new ChatMessageSentConsumer(
            hub.Object,
            messages.Object,
            Mock.Of<IAttachmentService>(),
            Mock.Of<IEncryptionService>(),
            Mock.Of<IChatService>(),
            NullLogger<ChatMessageSentConsumer>.Instance,
            push.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(consumeContext.Object));

        messages.Verify(service => service.UpdateDeliveryStatusAsync(
            messageId, MessageDeliveryStatus.Delivered, It.IsAny<CancellationToken>()), Times.Once);
        messages.Verify(service => service.UpdateDeliveryStatusAsync(
            messageId, MessageDeliveryStatus.Failed, It.IsAny<CancellationToken>()), Times.Once);
        push.Verify(service => service.SendPushForMessageAsync(
            messageId, It.IsAny<CancellationToken>()), Times.Once);
        proxy.Verify(client => client.SendCoreAsync(
            "MessageSendingStatus", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
