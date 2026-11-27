using MassTransit;
using Messenger.Core.Models;
using Messenger.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Data;

public partial class GuapMessengerContext : DbContext
{
    public GuapMessengerContext(DbContextOptions<GuapMessengerContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AccountSetting> AccountSettings { get; set; }
    public virtual DbSet<Attachment> Attachments { get; set; }
    public virtual DbSet<Chat> Chats { get; set; }
    public virtual DbSet<ChatParticipant> ChatParticipants { get; set; }
    public virtual DbSet<Login> Logins { get; set; }
    public virtual DbSet<Message> Messages { get; set; }
    public virtual DbSet<Notification> Notifications { get; set; }
    public virtual DbSet<Reaction> Reactions { get; set; }
    public virtual DbSet<Role> Roles { get; set; }
    public virtual DbSet<User> Users { get; set; }
    public virtual DbSet<UserStatus> UserStatuses { get; set; }
    public virtual DbSet<Broadcast> Broadcasts { get; set; }
    public virtual DbSet<BroadcastRecipient> BroadcastRecipients { get; set; }
    public virtual DbSet<PushSubscription> PushSubscriptions { get; set; }
    public virtual DbSet<Log> Logs { get; set; }
    public virtual DbSet<ChatFolder> ChatFolders { get; set; }
    public virtual DbSet<ChatFolderChat> ChatFolderChats { get; set; }
    public virtual DbSet<MessageReport> MessageReports { get; set; }
    public virtual DbSet<UserMute> UserMutes { get; set; }
    public virtual DbSet<ModerationLog> ModerationLogs { get; set; }
    public virtual DbSet<Core.Models.ScheduledMessage> ScheduledMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GuapMessengerContext).Assembly);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
