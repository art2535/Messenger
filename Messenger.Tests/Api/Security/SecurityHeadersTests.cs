using Messenger.API.Extensions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Messenger.Tests.Api.Security;

public sealed class SecurityHeadersTests
{
    [Fact]
    public void ApplySecurityHeaders_SetsBaselineHeaders()
    {
        var context = new DefaultHttpContext();

        SecurityHeadersExtension.ApplySecurityHeaders(context.Response);

        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions.ToString());
        Assert.Equal("DENY", context.Response.Headers.XFrameOptions.ToString());
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"].ToString());
        Assert.Contains("camera=()", context.Response.Headers["Permissions-Policy"].ToString());
        Assert.DoesNotContain("Content-Security-Policy", context.Response.Headers.Keys);
    }

    [Fact]
    public void ApplySecurityHeaders_ProductionPolicy_RestrictsEmbeddingAndBaseUri()
    {
        var context = new DefaultHttpContext();

        SecurityHeadersExtension.ApplySecurityHeaders(
            context.Response,
            includeContentSecurityPolicy: true);

        var csp = context.Response.Headers.ContentSecurityPolicy.ToString();

        Assert.Contains("default-src 'none'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("base-uri 'none'", csp);
    }
}
