using Messenger.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

internal sealed class LogConfiguration : IEntityTypeConfiguration<Log>
{
    public void Configure(EntityTypeBuilder<Log> entity)
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
    }
}
