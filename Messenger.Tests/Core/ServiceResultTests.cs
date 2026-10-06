using FluentAssertions;
using Messenger.Core.DTOs;
using Messenger.Core.Models;

namespace Messenger.Tests.Core
{
    public class ServiceResultTests
    {
        [Fact]
        public void Success_ShouldSetDataAndFlag()
        {
            var message = new Message { MessageId = Guid.NewGuid(), MessageText = "ok" };
            var result = ServiceResult<Message>.Success(message);

            result.isSuccess.Should().BeTrue();
            result.data.Should().BeSameAs(message);
            result.error.Should().BeNull();
            result.innerError.Should().BeNull();
        }

        [Fact]
        public void Failure_ShouldSetErrorsAndClearData()
        {
            var result = ServiceResult<Message>.Failure("fail", "inner");

            result.isSuccess.Should().BeFalse();
            result.data.Should().BeNull();
            result.error.Should().Be("fail");
            result.innerError.Should().Be("inner");
        }

        [Fact]
        public void Failure_WithoutInner_ShouldLeaveInnerNull()
        {
            var result = ServiceResult<string>.Failure("only-outer");

            result.isSuccess.Should().BeFalse();
            result.error.Should().Be("only-outer");
            result.innerError.Should().BeNull();
        }
    }
}
