using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class AccountSettingConfiguration : IEntityTypeConfiguration<AccountSetting>
{
    public void Configure(EntityTypeBuilder<AccountSetting> builder)
    {
        builder.HasKey(e => e.AccountId).HasName("Account_Settings_pkey");
    }
}
