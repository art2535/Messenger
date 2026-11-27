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
    public virtual DbSet<ChatFolder> ChatFolders { get; set; }
    public virtual DbSet<ChatFolderChat> ChatFolderChats { get; set; }
    public virtual DbSet<MessageReport> MessageReports { get; set; }
    public virtual DbSet<UserMute> UserMutes { get; set; }
    public virtual DbSet<ModerationLog> ModerationLogs { get; set; }
    public virtual DbSet<Core.Models.ScheduledMessage> ScheduledMessages { get; set; }

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

        modelBuilder.Entity<Login>(entity =>
        {
            entity.HasKey(e => e.LoginId).HasName("Logins_pkey");

            entity.Property(e => e.LoginId).ValueGeneratedNever();
            entity.Property(e => e.LoginTime).HasColumnType("timestamp with time zone");
            entity.Property(e => e.LogoutTime).HasColumnType("timestamp with time zone");

            entity.HasOne(d => d.User).WithMany(p => p.Logins).HasConstraintName("fk_login_user");
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

        modelBuilder.Entity<Core.Models.ScheduledMessage>(entity =>
        {
            entity.ToTable("Scheduled_Messages");
            entity.HasKey(e => e.ScheduledMessageId).HasName("Scheduled_Messages_pkey");

            entity.Property(e => e.ScheduledMessageId)
                .HasColumnName("Scheduled_Message_ID")
                .ValueGeneratedNever();

            entity.Property(e => e.ChatId).HasColumnName("Chat_ID");
            entity.Property(e => e.SenderId).HasColumnName("Sender_ID");
            entity.Property(e => e.MessageText).HasColumnName("Message_Text");
            entity.Property(e => e.HasAttachments).HasColumnName("Has_Attachments");
            entity.Property(e => e.AttachmentsJson).HasColumnName("Attachments_Json");
            entity.Property(e => e.ScheduledAt)
                .HasColumnName("Scheduled_At")
                .HasColumnType("timestamp with time zone");
            entity.Property(e => e.CreatedAt)
                .HasColumnName("Created_At")
                .HasColumnType("timestamp with time zone");
            entity.Property(e => e.UpdatedAt)
                .HasColumnName("Updated_At")
                .HasColumnType("timestamp with time zone");
            entity.Property(e => e.Status)
                .HasColumnName("Status")
                .HasMaxLength(32);
            entity.Property(e => e.ResultMessageId).HasColumnName("Result_Message_ID");
            entity.Property(e => e.ErrorMessage).HasColumnName("Error_Message");
            entity.Property(e => e.ReplyToMessageId).HasColumnName("Reply_To_Message_ID");

            entity.HasIndex(e => e.ChatId).HasDatabaseName("IX_Scheduled_Messages_Chat_ID");
            entity.HasIndex(e => new { e.SenderId, e.Status })
                .HasDatabaseName("IX_Scheduled_Messages_Sender_ID_Status");
            entity.HasIndex(e => new { e.Status, e.ScheduledAt })
                .HasDatabaseName("IX_Scheduled_Messages_Status_Scheduled_At");

            entity.HasOne(d => d.Chat)
                .WithMany()
                .HasForeignKey(d => d.ChatId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_scheduled_message_chat");

            entity.HasOne(d => d.Sender)
                .WithMany()
                .HasForeignKey(d => d.SenderId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_scheduled_message_sender");
        });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GuapMessengerContext).Assembly);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
