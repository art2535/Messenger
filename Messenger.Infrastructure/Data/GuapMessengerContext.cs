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
            entity.HasKey(e => e.AccountId).HasName("Account_Settings_pkey");
        });

        modelBuilder.Entity<Attachment>(entity =>
        {
            entity.HasKey(e => e.AttachmentId).HasName("Attachments_pkey");

            entity.Property(e => e.AttachmentId).ValueGeneratedNever();

            entity.HasIndex(e => e.MessageId)
                .HasDatabaseName("IX_Attachments_MessageId");

            entity.HasOne(d => d.Message).WithMany(p => p.Attachments).HasConstraintName("fk_attachment_message");
        });

        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(e => e.ChatId).HasName("Chats_pkey");

            entity.Property(e => e.ChatId).ValueGeneratedNever();
            entity.Property(e => e.CreationDate)
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("now() at time zone 'utc'");

            entity.HasOne(d => d.User).WithMany(p => p.Chats).HasConstraintName("fk_chat_creator");
        });

        modelBuilder.Entity<ChatParticipant>(entity =>
        {
            entity.HasKey(e => new { e.ChatId, e.UserId }).HasName("Chat_Participants_pkey");

            entity.Property(e => e.JoinDate)
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("now() at time zone 'utc'");
            entity.Property(e => e.Role).HasDefaultValueSql("'participant'::character varying");

            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("IX_ChatParticipants_UserId");

            entity.HasOne(d => d.Chat).WithMany(p => p.ChatParticipants).HasConstraintName("fk_participant_chat");

            entity.HasOne(d => d.User).WithMany(p => p.ChatParticipants).HasConstraintName("fk_participant_user");
        });

        modelBuilder.Entity<Login>(entity =>
        {
            entity.HasKey(e => e.LoginId).HasName("Logins_pkey");

            entity.Property(e => e.LoginId).ValueGeneratedNever();
            entity.Property(e => e.LoginTime).HasColumnType("timestamp with time zone");
            entity.Property(e => e.LogoutTime).HasColumnType("timestamp with time zone");

            entity.HasOne(d => d.User).WithMany(p => p.Logins).HasConstraintName("fk_login_user");
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.MessageId).HasName("Messages_pkey");

            entity.Property(e => e.MessageId).ValueGeneratedNever();
            entity.Property(e => e.SendTime).HasColumnType("timestamp with time zone");
            entity.Property(e => e.ReadTime).HasColumnType("timestamp with time zone");

            entity.HasIndex(e => new { e.ChatId, e.SequenceNumber })
                .IsUnique()
                .HasDatabaseName("IX_Messages_ChatId_SequenceNumber");

            entity.HasIndex(e => new { e.ChatId, e.SendTime })
                .HasDatabaseName("IX_Messages_ChatId_SendTime");

            entity.HasIndex(e => new { e.ChatId, e.ReadTime })
                .HasDatabaseName("IX_Messages_ChatId_ReadTime");

            entity.Property(e => e.IsPinned)
                .HasColumnName("Is_Pinned")
                .HasDefaultValue(false);

            entity.Property(e => e.PinnedAt)
                .HasColumnName("Pinned_At")
                .HasColumnType("timestamp with time zone");

            entity.HasIndex(e => new { e.ChatId, e.IsPinned })
                .HasDatabaseName("IX_Messages_ChatId_IsPinned")
                .HasFilter("\"Is_Pinned\" = true");

            entity.HasOne(d => d.Chat).WithMany(p => p.Messages).HasConstraintName("fk_chat_messages");

            entity.HasOne(d => d.Sender).WithMany(p => p.MessageSenders).HasConstraintName("fk_sender");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.Property(e => e.CreationDate).HasColumnType("timestamp with time zone");
            entity.Property(e => e.ReadAt).HasColumnType("timestamp with time zone");
            entity.HasKey(e => e.NotificationId).HasName("Notifications_pkey");

            entity.Property(e => e.NotificationId).ValueGeneratedNever();
            entity.Property(e => e.CreationDate)
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("now() at time zone 'utc'");
            entity.Property(e => e.Read).HasDefaultValue(false);

            entity.HasIndex(e => new { e.UserId, e.Read })
                .HasDatabaseName("IX_Notifications_UserId_Read");

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

        modelBuilder.Entity<Role>().HasData(
            new Role
            {
                RoleId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
                Name = "Абитуриент",
                Description = "Роль абитуриента (ROLE_ENTRANT)"
            },
            new Role
            {
                RoleId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901"),
                Name = "Студент",
                Description = "Роль студента (ROLE_STUDENT)"
            },
            new Role
            {
                RoleId = Guid.Parse("c3d4e5f6-a7b8-9012-cdef-123456789012"),
                Name = "Преподаватель",
                Description = "Роль преподавателя (ROLE_TEACHER)"
            },
            new Role
            {
                RoleId = Guid.Parse("d4e5f6a7-b8c9-0123-defa-234567890123"),
                Name = "Сотрудник",
                Description = "Роль сотрудника (ROLE_EMPLOYEE)"
            },
            new Role
            {
                RoleId = Guid.Parse("e5f6a7b8-c9d0-1234-efab-345678901234"),
                Name = "Администратор",
                Description = "Полный доступ к системе (ROLE_ADMIN)"
            },
            new Role
            {
                RoleId = Guid.Parse("f6a7b8c9-d0e1-2345-fabc-456789012345"),
                Name = "Пользователь",
                Description = "Стандартный доступ пользователя (ROLE_USER)"
            }
        );

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("Users_pkey");

            entity.Property(e => e.UserId).ValueGeneratedNever();

            entity.HasOne(d => d.Account)
                .WithOne(p => p.User)
                .HasForeignKey<AccountSetting>(a => a.AccountId)   // shared PK
                .HasConstraintName("fk_account")
                .OnDelete(DeleteBehavior.Cascade);

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
            entity.Property(e => e.LastActivity).HasColumnType("timestamp with time zone");
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

            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("IX_PushSubscriptions_UserId");

            entity.HasOne(d => d.User)
                .WithMany(p => p.PushSubscriptions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_push_subscription_user");
        });

        modelBuilder.Entity<Log>(entity =>
        {
            entity.ToTable("Logs", "logging");

            entity.HasKey(e => e.Id).HasName("logs_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("Id")
                .HasColumnType("uuid")
                .HasDefaultValueSql("gen_random_uuid()")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.Message).HasColumnName("Message");
            entity.Property(e => e.MessageTemplate).HasColumnName("MessageTemplate");
            entity.Property(e => e.Level)
                .HasColumnName("Level")
                .HasMaxLength(32);
            entity.Property(e => e.Timestamp)
                .HasColumnName("Timestamp")
                .HasColumnType("timestamp with time zone");
            entity.Property(e => e.Exception).HasColumnName("Exception");
            entity.Property(e => e.LogEvent)
                .HasColumnName("LogEvent")
                .HasColumnType("jsonb");
            entity.Property(e => e.Application)
                .HasColumnName("Application")
                .HasMaxLength(128);
            entity.Property(e => e.MachineName)
                .HasColumnName("MachineName")
                .HasMaxLength(128);
            entity.Property(e => e.Environment)
                .HasColumnName("Environment")
                .HasMaxLength(64);

            entity.HasIndex(e => e.Timestamp)
                .HasDatabaseName("IX_logs_timestamp");
            entity.HasIndex(e => e.Level)
                .HasDatabaseName("IX_logs_level");
            entity.HasIndex(e => e.Application)
                .HasDatabaseName("IX_logs_application");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
