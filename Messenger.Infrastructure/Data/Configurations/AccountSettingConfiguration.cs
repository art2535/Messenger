using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class AccountSettingConfiguration : IEntityTypeConfiguration<AccountSetting>
{
    public void Configure(EntityTypeBuilder<AccountSetting> builder)
    {
        builder.ToTable("Account_Settings");
        builder.HasKey(e => e.AccountId).HasName("Account_Settings_pkey");
        builder.HasIndex(e => e.AccountId).IsUnique()
            .HasDatabaseName("Account_Settings_Account_ID_key");

        builder.Property(e => e.AccountId).HasColumnName("Account_ID");
        builder.Property(e => e.Avatar);
        builder.Property(e => e.PushEnabled).HasColumnName("Push_Enabled");
        builder.Property(e => e.NotifyMessages).HasColumnName("Notify_Messages");
        builder.Property(e => e.NotifyGroupChats).HasColumnName("Notify_GroupChats");
        builder.Property(e => e.NotifyMentions).HasColumnName("Notify_Mentions");
        builder.Property(e => e.NotifyReactions).HasColumnName("Notify_Reactions");
        builder.Property(e => e.NotifySystem).HasColumnName("Notify_System");
        builder.Property(e => e.NotifySound).HasColumnName("Notify_Sound");
        builder.Property(e => e.SuppressOpenChat).HasColumnName("Suppress_Open_Chat");
        builder.Property(e => e.NotifyPreview).HasColumnName("Notify_Preview");
        builder.Property(e => e.Theme).HasMaxLength(16).HasColumnName("Theme");
        builder.Property(e => e.LargeFont).HasColumnName("Large_Font");
        builder.Property(e => e.CompactList).HasColumnName("Compact_List");
        builder.Property(e => e.ShowOnline).HasColumnName("Show_Online");
        builder.Property(e => e.ShowTyping).HasColumnName("Show_Typing");
        builder.Property(e => e.ReadReceipts).HasColumnName("Read_Receipts");
        builder.Property(e => e.IdleLogout).HasColumnName("Idle_Logout");

        builder.HasOne(e => e.User)
            .WithOne(user => user.Account)
            .HasForeignKey<AccountSetting>(e => e.AccountId)
            .HasConstraintName("fk_account")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
