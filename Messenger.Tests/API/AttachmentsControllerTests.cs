using FluentAssertions;
using Messenger.API.Controllers;
using Messenger.API.Responses;
using Messenger.Core.DTOs.Attachments;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Messenger.Tests.API
{
    public class AttachmentsControllerTests
    {
        private readonly Mock<IAttachmentService> _service = new();
        private readonly AttachmentsController _controller;

        public AttachmentsControllerTests()
        {
            _controller = new AttachmentsController(_service.Object);
        }

        [Fact]
        public async Task GetAttachmentsByMessageAsync_ReturnsOkWithData()
        {
            var messageId = Guid.NewGuid();
            var list = new List<Attachment>
            {
                new Attachment { AttachmentId = Guid.NewGuid(), MessageId = messageId, FileName = "a.pdf", Url = "/a" }
            };
            _service.Setup(s => s.GetAttachmentsByMessageIdAsync(messageId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(list);

            var result = await _controller.GetAttachmentsByMessageAsync(messageId);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var body = ok.Value.Should().BeOfType<GetAttachmentsSuccessResponse>().Subject;
            body.IsSuccess.Should().BeTrue();
            body.Data.Should().BeEquivalentTo(list);
        }

        [Fact]
        public async Task GetAttachmentsByMessageAsync_OnException_Returns500()
        {
            _service.Setup(s => s.GetAttachmentsByMessageIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("db down"));

            var result = await _controller.GetAttachmentsByMessageAsync(Guid.NewGuid());

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(500);
            obj.Value.Should().BeOfType<ErrorResponse>()
                .Which.Error.Should().Be("db down");
        }

        [Fact]
        public async Task CreateAttachmentAsync_ReturnsOkAndCallsService()
        {
            var messageId = Guid.NewGuid();
            var request = new CreateAttachmentRequest
            {
                FileName = "doc.pdf",
                FileType = "application/pdf",
                SizeInBytes = 1024,
                Url = "/uploads/doc.pdf"
            };

            var result = await _controller.CreateAttachmentAsync(messageId, request);

            result.Should().BeOfType<OkObjectResult>()
                .Which.Value.Should().BeOfType<CreateAttachmentSuccessResponse>()
                .Which.IsSuccess.Should().BeTrue();

            _service.Verify(s => s.AddAttachmentAsync(
                It.Is<Attachment>(a => a.MessageId == messageId && a.FileName == "doc.pdf"),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
