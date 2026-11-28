using Messenger.Application.Common;
using Xunit;

namespace Messenger.Tests.Application.Common;

public sealed class UtcDateTimeTests
{
    [Fact]
    public void AsUtc_PreservesUtcValueAndKind()
    {
        var value = new DateTime(2026, 11, 28, 12, 30, 0, DateTimeKind.Utc);

        var result = UtcDateTime.AsUtc(value);

        Assert.Equal(value, result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void AsUtc_AssumesUnspecifiedValueIsAlreadyUtc()
    {
        var value = new DateTime(2026, 11, 28, 12, 30, 0, DateTimeKind.Unspecified);

        var result = UtcDateTime.AsUtc(value);

        Assert.Equal(value.Ticks, result.Ticks);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void AsUtc_ConvertsLocalValueToUtc()
    {
        var value = new DateTime(2026, 11, 28, 12, 30, 0, DateTimeKind.Local);

        var result = UtcDateTime.AsUtc(value);

        Assert.Equal(value.ToUniversalTime(), result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void AsUtc_NullableOverloadPreservesNullAndConvertsValue()
    {
        DateTime? missing = null;
        DateTime? value = new DateTime(2026, 11, 28, 12, 30, 0, DateTimeKind.Unspecified);

        Assert.Null(UtcDateTime.AsUtc(missing));
        Assert.Equal(DateTimeKind.Utc, UtcDateTime.AsUtc(value)!.Value.Kind);
    }
}
