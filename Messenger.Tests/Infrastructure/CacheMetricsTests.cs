using System.Diagnostics.Metrics;
using Messenger.Infrastructure.Services;
using Xunit;

namespace Messenger.Tests.Infrastructure;

public sealed class CacheMetricsTests
{
    [Fact]
    public void RecordMethods_EmitCountersWithExtractedKeyPrefixes()
    {
        var measurements = new List<(string Name, long Value, string? Prefix)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Messenger.Cache")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            string? prefix = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "prefix")
                    prefix = tag.Value?.ToString();
            }
            measurements.Add((instrument.Name, measurement, prefix));
        });
        listener.Start();

        CacheMetrics.RecordHitForKey("chat:123:messages");
        CacheMetrics.RecordMissForKey("user:123:chats");
        CacheMetrics.RecordErrorForKey("unprefixed");
        CacheMetrics.RecordHit();
        listener.RecordObservableInstruments();

        Assert.Contains(measurements, item => item.Name == "messenger.cache.hits" && item.Value == 1 && item.Prefix == "chat");
        Assert.Contains(measurements, item => item.Name == "messenger.cache.misses" && item.Value == 1 && item.Prefix == "user");
        Assert.Contains(measurements, item => item.Name == "messenger.cache.errors" && item.Value == 1 && item.Prefix == "unprefixed");
        Assert.Contains(measurements, item => item.Name == "messenger.cache.hits" && item.Value == 1 && item.Prefix == "unknown");
    }
}
