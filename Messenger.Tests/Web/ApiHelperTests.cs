using FluentAssertions;
using Messenger.Web.Helpers;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Messenger.Tests.Web
{
    public class ApiHelperTests
    {
        private static ApiHelper CreateHelper(Dictionary<string, string?> values)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var factory = new Mock<IHttpClientFactory>();
            return new ApiHelper(factory.Object, config);
        }

        [Fact]
        public void GetAPIUrl_WithoutPath_ShouldBuildBaseVersionedUrl()
        {
            var helper = CreateHelper(new Dictionary<string, string?>
            {
                ["URL:API:HTTPS"] = "https://api.example.com/",
                ["URL:API:Version"] = "2.0"
            });

            helper.GetApiUrl().Should().Be("https://api.example.com/api/v2.0");
        }

        [Fact]
        public void GetAPIUrl_WithPath_ShouldAppendTrimmedPath()
        {
            var helper = CreateHelper(new Dictionary<string, string?>
            {
                ["URL:API:HTTPS"] = "https://api.example.com",
                ["URL:API:Version"] = "1.0"
            });

            helper.GetApiUrl("/chats/123").Should().Be("https://api.example.com/api/v1.0/chats/123");
            helper.GetApiUrl("messages").Should().Be("https://api.example.com/api/v1.0/messages");
        }

        [Fact]
        public void GetAPIUrl_DefaultVersion_ShouldBe1_0()
        {
            var helper = CreateHelper(new Dictionary<string, string?>
            {
                ["URL:API:HTTPS"] = "https://localhost:5001"
            });

            helper.GetApiUrl("users").Should().Be("https://localhost:5001/api/v1.0/users");
        }
    }
}
