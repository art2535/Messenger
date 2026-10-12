using System.Diagnostics.Metrics;

namespace Messenger.Core.Metrics
{
    public static class MessengerMetrics
    {
        private static readonly Meter s_meter = new("Messenger", "1.0.0");

        public static readonly UpDownCounter<long> SignalRActiveConnections =
            s_meter.CreateUpDownCounter<long>(
                "messenger.signalr.active_connections",
                unit: "{connection}",
                description: "Current active SignalR connections");

        public static readonly Counter<long> MessagesSent =
            s_meter.CreateCounter<long>(
                "messenger.messages.sent_total",
                unit: "{message}",
                description: "Total messages sent");

        public static readonly Counter<long> NotificationsSent =
            s_meter.CreateCounter<long>(
                "messenger.notifications.sent_total",
                unit: "{notification}",
                description: "Total notifications sent");

        public static readonly Histogram<double> HubActionDuration =
            s_meter.CreateHistogram<double>(
                "messenger.hub.action_duration",
                unit: "s",
                description: "Hub action processing duration in seconds");

        public static readonly Counter<long> SignalRErrors =
            s_meter.CreateCounter<long>(
                "messenger.signalr.errors_total",
                unit: "{error}",
                description: "SignalR errors");
    }
}