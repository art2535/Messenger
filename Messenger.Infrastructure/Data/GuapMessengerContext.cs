using MassTransit;
using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Data;

public partial class GuapMessengerContext : DbContext
{
    public GuapMessengerContext()
    {
    }

    public GuapMessengerContext(DbContextOptions<GuapMessengerContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AccountSetting> AccountSettings { get; set; }

    public virtual DbSet<Attachment> Attachments { get; set; }

    public virtual DbSet<Blacklist> Blacklists { get; set; }

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

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        modelBuilder.Entity<AccountSetting>(entity =>
        {
            entity.HasKey(e => e.SettingId).HasName("Account_Settings_pkey");

            entity.Property(e => e.SettingId).ValueGeneratedNever();
        });

        modelBuilder.Entity<Attachment>(entity =>
        {
            entity.HasKey(e => e.AttachmentId).HasName("Attachments_pkey");

            entity.Property(e => e.AttachmentId).ValueGeneratedNever();

            entity.HasOne(d => d.Message).WithMany(p => p.Attachments).HasConstraintName("fk_attachment_message");
        });

        modelBuilder.Entity<Blacklist>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.BlockedUserId }).HasName("Blacklist_pkey");

            entity.Property(e => e.BlockDate).HasDefaultValueSql("now()");

            entity.HasOne(d => d.BlockedUser).WithMany(p => p.BlacklistBlockedUsers).HasConstraintName("fk_blocked_to");

            entity.HasOne(d => d.User).WithMany(p => p.BlacklistUsers).HasConstraintName("fk_blocked_by");
        });

        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(e => e.ChatId).HasName("Chats_pkey");

            entity.Property(e => e.ChatId).ValueGeneratedNever();
            entity.Property(e => e.CreationDate).HasDefaultValueSql("now()");

            entity.HasOne(d => d.User).WithMany(p => p.Chats).HasConstraintName("fk_chat_creator");
        });

        modelBuilder.Entity<ChatParticipant>(entity =>
        {
            entity.HasKey(e => new { e.ChatId, e.UserId }).HasName("Chat_Participants_pkey");

            entity.Property(e => e.JoinDate).HasDefaultValueSql("now()");
            entity.Property(e => e.Role).HasDefaultValueSql("'participant'::character varying");

            entity.HasOne(d => d.Chat).WithMany(p => p.ChatParticipants).HasConstraintName("fk_participant_chat");

            entity.HasOne(d => d.User).WithMany(p => p.ChatParticipants).HasConstraintName("fk_participant_user");
        });

        modelBuilder.Entity<Login>(entity =>
        {
            entity.HasKey(e => e.LoginId).HasName("Logins_pkey");

            entity.Property(e => e.LoginId).ValueGeneratedNever();

            entity.HasOne(d => d.User).WithMany(p => p.Logins).HasConstraintName("fk_login_user");
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.MessageId).HasName("Messages_pkey");

            entity.Property(e => e.MessageId).ValueGeneratedNever();

            entity.HasIndex(e => new { e.ChatId, e.SequenceNumber })
                .IsUnique()
                .HasDatabaseName("IX_Messages_ChatId_SequenceNumber");

            entity.HasIndex(e => new { e.ChatId, e.SendTime })
                .HasDatabaseName("IX_Messages_ChatId_SendTime");

            entity.HasIndex(e => new { e.ChatId, e.ReadTime })
                .HasDatabaseName("IX_Messages_ChatId_ReadTime");

            entity.HasOne(d => d.Chat).WithMany(p => p.Messages).HasConstraintName("fk_chat_messages");

            entity.HasOne(d => d.Sender).WithMany(p => p.MessageSenders).HasConstraintName("fk_sender");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.NotificationId).HasName("Notifications_pkey");

            entity.Property(e => e.NotificationId).ValueGeneratedNever();
            entity.Property(e => e.CreationDate).HasDefaultValueSql("now()");
            entity.Property(e => e.Read).HasDefaultValue(false);

            entity.HasOne(d => d.User).WithMany(p => p.Notifications).HasConstraintName("fk_notification_user");
        });

        modelBuilder.Entity<Reaction>(entity =>
        {
            entity.HasKey(e => e.ReactionId).HasName("Reactions_pkey");

            entity.Property(e => e.ReactionId).ValueGeneratedNever();

            entity.HasIndex(e => new { e.MessageId, e.UserId })
                .IsUnique()
                .HasDatabaseName("IX_Reactions_MessageId_UserId");

            entity.HasOne(d => d.Message).WithMany(p => p.Reactions).HasConstraintName("fk_reaction_message");

            entity.HasOne(d => d.User).WithMany(p => p.Reactions).HasConstraintName("fk_reaction_user");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("Roles_pkey");

            entity.Property(e => e.RoleId).ValueGeneratedNever();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("Users_pkey");

            entity.Property(e => e.UserId).ValueGeneratedNever();

            entity.HasOne(d => d.Account).WithMany(p => p.Users)
                .HasPrincipalKey(p => p.AccountId)
                .HasForeignKey(d => d.AccountId)
                .HasConstraintName("fk_account");

            entity.HasMany(d => d.Roles).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "UserRole",
                    r => r.HasOne<Role>().WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("fk_userrole_role"),
                    l => l.HasOne<User>().WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("fk_userrole_user"),
                    j =>
                    {
                        j.HasKey("UserId", "RoleId").HasName("UserRoles_pkey");
                        j.ToTable("User_Roles");
                        j.IndexerProperty<Guid>("UserId").HasColumnName("User_ID");
                        j.IndexerProperty<Guid>("RoleId").HasColumnName("Role_ID");
                    });
        });

        modelBuilder.Entity<UserStatus>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("User_Statuses_pkey");

            entity.Property(e => e.UserId).ValueGeneratedNever();

            entity.HasOne(d => d.User).WithOne(p => p.UserStatus).HasConstraintName("fk_status_user");
        });

        modelBuilder.Entity<Broadcast>(entity =>
        {
            entity.HasKey(e => e.BroadcastId).HasName("Broadcasts_pkey");

            entity.Property(e => e.BroadcastId)
                .HasColumnName("Broadcast_ID")
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.CreatedAt)
                .HasColumnName("Created_At")
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp with time zone");

            entity.Property(e => e.SenderId).HasColumnName("Sender_ID");

            entity.Property(e => e.TotalRecipients).HasColumnName("Total_Recipients");

            entity.HasOne(d => d.Sender)
                .WithMany(p => p.BroadcastsCreated)
                .HasForeignKey(d => d.SenderId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_broadcast_sender");
        });

        modelBuilder.Entity<BroadcastRecipient>(entity =>
        {
            entity.HasKey(e => new { e.BroadcastId, e.UserId }).HasName("Broadcast_Recipients_pkey");

            entity.Property(e => e.BroadcastId).HasColumnName("Broadcast_ID");
            entity.Property(e => e.UserId).HasColumnName("User_ID");

            entity.Property(e => e.SentAt)
                .HasColumnName("Sent_At")
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp with time zone");

            entity.Property(e => e.DeliveredAt)
                .HasColumnName("Delivered_At")
                .HasColumnType("timestamp with time zone");

            entity.Property(e => e.ReadAt)
                .HasColumnName("Read_At")
                .HasColumnType("timestamp with time zone");

            entity.HasOne(d => d.Broadcast)
                .WithMany(p => p.Recipients)
                .HasForeignKey(d => d.BroadcastId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_recipient_broadcast");

            entity.HasOne(d => d.User)
                .WithMany(p => p.BroadcastRecipients)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_recipient_user");
        });

        modelBuilder.Entity<PushSubscription>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PushSubscriptions_pkey");

            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.HasOne(d => d.User)
                .WithMany(p => p.PushSubscriptions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_push_subscription_user");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
