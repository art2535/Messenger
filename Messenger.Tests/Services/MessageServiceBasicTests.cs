using FluentAssertions;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.EntityFrameworkCore;

namespace Messenger.Tests.Services
{
    public class MessageServiceBasicTests
    {
        private readonly Mock<GuapMessengerContext> _contextMock;
        private readonly Mock<IEncryptionService> _encryptionMock;
        private readonly Mock<ICacheService> _cacheMock;
        private readonly Mock<ILogger<MessageService>> _loggerMock;
        private MessageService? _service;

        public MessageServiceBasicTests()
        {
            _contextMock = new Mock<GuapMessengerContext>();
            _encryptionMock = new Mock<IEncryptionService>();
            _cacheMock = new Mock<ICacheService>();
            _loggerMock = new Mock<ILogger<MessageService>>();

            _contextMock.Setup(c => c.Messages).ReturnsDbSet(new List<Message>());
            _encryptionMock.Setup(e => e.TryDecryptSafe(It.IsAny<string?>())).Returns<string?>(s => s ?? "");
            _cacheMock.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _cacheMock.Setup(c => c.RemoveAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        }

        private MessageService CreateService()
        {
            var ctors = typeof(MessageService).GetConstructors();
            var ctor = ctors.OrderByDescending(c => c.GetParameters().Length).First();
            var args = new List<object?>();
            foreach (var p in ctor.GetParameters())
            {
                if (p.ParameterType == typeof(GuapMessengerContext))
                    args.Add(_contextMock.Object);
                else if (p.ParameterType == typeof(IEncryptionService))
                    args.Add(_encryptionMock.Object);
                else if (p.ParameterType == typeof(ICacheService))
                    args.Add(_cacheMock.Object);
                else if (p.ParameterType == typeof(ILogger<MessageService>))
                    args.Add(_loggerMock.Object);
                else
                    args.Add(null);
            }
            return (MessageService)ctor.Invoke(args.ToArray());
        }

        [Fact]
        public async Task GetMessageByIdGlobalAsync_WhenExists_ShouldReturn()
        {
            var messageId = Guid.NewGuid();
            var msg = new Message
            {
                MessageId = messageId,
                ChatId = Guid.NewGuid(),
                SenderId = Guid.NewGuid(),
                MessageText = "hello",
                SendTime = DateTime.UtcNow
            };
            _contextMock.Setup(c => c.Messages).ReturnsDbSet(new List<Message> { msg });
            var service = CreateService();

            var result = await service.GetMessageByIdGlobalAsync(messageId);

            result.Should().NotBeNull();
            result!.MessageId.Should().Be(messageId);
        }

        [Fact]
        public async Task GetMessageByIdGlobalAsync_WhenMissing_ShouldReturnNull()
        {
            _contextMock.Setup(c => c.Messages).ReturnsDbSet(new List<Message>());
            var service = CreateService();

            var result = await service.GetMessageByIdGlobalAsync(Guid.NewGuid());

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetMessageByIdAsync_ShouldFilterByChat()
        {
            var chatId = Guid.NewGuid();
            var messageId = Guid.NewGuid();
            var messages = new List<Message>
            {
                new Message { MessageId = messageId, ChatId = chatId, MessageText = "in-chat", SenderId = Guid.NewGuid() },
                new Message { MessageId = messageId, ChatId = Guid.NewGuid(), MessageText = "other", SenderId = Guid.NewGuid() }
            };
            var mid = Guid.NewGuid();
            messages = new List<Message>
            {
                new Message { MessageId = mid, ChatId = chatId, MessageText = "in-chat", SenderId = Guid.NewGuid() },
                new Message { MessageId = Guid.NewGuid(), ChatId = Guid.NewGuid(), MessageText = "other", SenderId = Guid.NewGuid() }
            };
            _contextMock.Setup(c => c.Messages).ReturnsDbSet(messages);
            var service = CreateService();

            var result = await service.GetMessageByIdAsync(chatId, mid);
            result.Should().NotBeNull();
            result!.MessageText.Should().Be("in-chat");

            var missing = await service.GetMessageByIdAsync(Guid.NewGuid(), mid);
            missing.Should().BeNull();
        }

        [Fact]
        public async Task GetMessagesAsync_ShouldReturnChatMessages()
        {
            var chatId = Guid.NewGuid();
            var messages = new List<Message>
            {
                new Message { MessageId = Guid.NewGuid(), ChatId = chatId, MessageText = "m1", SenderId = Guid.NewGuid(), SendTime = DateTime.UtcNow.AddMinutes(-1) },
                new Message { MessageId = Guid.NewGuid(), ChatId = chatId, MessageText = "m2", SenderId = Guid.NewGuid(), SendTime = DateTime.UtcNow },
                new Message { MessageId = Guid.NewGuid(), ChatId = Guid.NewGuid(), MessageText = "other", SenderId = Guid.NewGuid() }
            };
            _contextMock.Setup(c => c.Messages).ReturnsDbSet(messages);
            var service = CreateService();

            var result = (await service.GetMessagesAsync(chatId)).ToList();

            result.Should().HaveCount(2);
            result.Should().OnlyContain(m => m.ChatId == chatId);
        }
    }
}
