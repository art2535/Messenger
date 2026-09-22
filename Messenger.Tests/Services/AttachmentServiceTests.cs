using FluentAssertions;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Moq.EntityFrameworkCore;

namespace Messenger.Tests.Services
{
    public class AttachmentServiceTests
    {
        private readonly Mock<GuapMessengerContext> _contextMock;
        private readonly AttachmentService _service;

        public AttachmentServiceTests()
        {
            _contextMock = new Mock<GuapMessengerContext>();
            _service = new AttachmentService(_contextMock.Object);
        }

        #region AddAttachmentAsync

        [Fact]
        public async Task AddAttachmentAsync_ShouldCallAddAsyncAndSaveChangesAsync()
        {
            var attachment = new Attachment
            {
                MessageId = Guid.NewGuid(),
                FileName = "document.pdf",
                Url = "/uploads/document.pdf",
                FileType = "application/pdf",
                SizeInBytes = 1024 * 1024
            };

            var mockDbSet = new Mock<DbSet<Attachment>>();
            _contextMock.Setup(c => c.Attachments).Returns(mockDbSet.Object);
            _contextMock.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(1);

            await _service.AddAttachmentAsync(attachment);

            mockDbSet.Verify(db => db.AddAsync(It.IsAny<Attachment>(), It.IsAny<CancellationToken>()), Times.Once());
            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
        }

        #endregion

        #region GetAttachmentsByMessageIdAsync

        [Fact]
        public async Task GetAttachmentsByMessageIdAsync_ShouldReturnAttachments()
        {
            var messageId = Guid.NewGuid();
            var expectedAttachments = new List<Attachment>
            {
                new Attachment { MessageId = messageId, FileName = "image1.jpg" },
                new Attachment { MessageId = messageId, FileName = "document.pdf" },
                new Attachment { MessageId = messageId, FileName = "video.mp4" }
            };

            _contextMock.Setup(c => c.Attachments).ReturnsDbSet(expectedAttachments);

            var result = await _service.GetAttachmentsByMessageIdAsync(messageId);

            result.Should().NotBeNull();
            result.Should().HaveCount(3);
            result.Should().BeEquivalentTo(expectedAttachments);
        }

        [Fact]
        public async Task GetAttachmentsByMessageIdAsync_WhenNoAttachments_ShouldReturnEmptyList()
        {
            var messageId = Guid.NewGuid();
            _contextMock.Setup(c => c.Attachments).ReturnsDbSet(new List<Attachment>());

            var result = await _service.GetAttachmentsByMessageIdAsync(messageId);

            result.Should().BeEmpty();
        }

        #endregion
    }
}