using System.ComponentModel.DataAnnotations;
using Messenger.API.Options;
using Messenger.API.Services;
using Messenger.Core.Configuration;
using Messenger.Web.Services;
using Xunit;

namespace Messenger.Tests.Configuration;

public sealed class ConfigurationOptionsTests
{
    [Fact]
    public void IdentityProviderOptions_RequiresIssuerAndClient()
    {
        var options = new IdentityProviderOptions
        {
            Instance = "https://sso.example.test/realms/",
            TenantId = "",
            ClientId = ""
        };

        var errors = Validate(options);
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(IdentityProviderOptions.TenantId)));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(IdentityProviderOptions.ClientId)));
    }

    [Fact]
    public void SignalROptions_RejectsInvalidLimits()
    {
        var options = new SignalROptions
        {
            ChannelPrefix = "",
            MaximumReceiveMessageSize = 0,
            ClientTimeoutSeconds = 1,
            KeepAliveSeconds = 0
        };

        Assert.NotEmpty(Validate(options));
    }

    [Fact]
    public void SessionCleanupOptions_RejectsTooShortIdleTimeout()
    {
        var errors = Validate(new SessionCleanupOptions { IdleMinutes = 1, IntervalMinutes = 5 });
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(SessionCleanupOptions.IdleMinutes)));
    }

    [Fact]
    public void LogRetentionOptions_RejectsInvalidBatchSize()
    {
        var errors = Validate(new LogRetentionOptions { BatchSize = 10 });
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(LogRetentionOptions.BatchSize)));
    }

    [Fact]
    public void TokenRefreshOptions_RejectsNonPositiveIntervals()
    {
        var errors = Validate(new TokenRefreshOptions { CheckIntervalSeconds = 0 });
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(TokenRefreshOptions.CheckIntervalSeconds)));
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}