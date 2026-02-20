using System.Text.Json;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using InfraLLM.Core.Enums;
using InfraLLM.Core.Models;

namespace InfraLLM.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<Host> Hosts => Set<Host>();
    public DbSet<HostNote> HostNotes => Set<HostNote>();
    public DbSet<Credential> Credentials => Set<Credential>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<UserPolicy> UserPolicies => Set<UserPolicy>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<PromptSettings> PromptSettings => Set<PromptSettings>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<CommandExecution> CommandExecutions => Set<CommandExecution>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<McpServer> McpServers => Set<McpServer>();
    public DbSet<AccessToken> AccessTokens => Set<AccessToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Reusable value converter + comparer for List<string> <-> jsonb
        var stringListConverter = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<List<string>>(v, JsonSerializerOptions.Default) ?? new List<string>());

        var stringListComparer = new ValueComparer<List<string>>(
            (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList());

        var guidListConverter = new ValueConverter<List<Guid>, string>(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<List<Guid>>(v, JsonSerializerOptions.Default) ?? new List<Guid>());

        var guidListComparer = new ValueComparer<List<Guid>>(
            (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList());

        builder.Entity<Organization>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.HasMany(x => x.Hosts).WithOne(x => x.Organization).HasForeignKey(x => x.OrganizationId);
            e.HasMany(x => x.Policies).WithOne(x => x.Organization).HasForeignKey(x => x.OrganizationId);
            e.HasMany(x => x.Members).WithOne(x => x.Organization).HasForeignKey(x => x.OrganizationId);
        });

        builder.Entity<OrganizationMember>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.User).WithMany(x => x.OrganizationMemberships).HasForeignKey(x => x.UserId);
            e.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique();
        });

        builder.Entity<Host>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.Hostname).HasMaxLength(255).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.Environment).HasMaxLength(50);
            e.Property(x => x.AllowInsecureSsl).HasDefaultValue(false);
            e.Property(x => x.Tags)
                .HasColumnType("jsonb")
                .HasConversion(stringListConverter)
                .Metadata.SetValueComparer(stringListComparer);
            e.HasOne(x => x.Credential).WithMany().HasForeignKey(x => x.CredentialId);
            e.HasIndex(x => x.OrganizationId);
            e.HasIndex(x => x.Environment);
            e.HasIndex(x => x.Status);
        });

        builder.Entity<HostNote>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Content).HasColumnType("text").IsRequired();
            e.Property(x => x.UpdatedByUserId).HasMaxLength(255).IsRequired();
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasOne(x => x.Host).WithMany().HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.OrganizationId, x.HostId }).IsUnique();
        });

        builder.Entity<Credential>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.CredentialType).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.EncryptedValue).IsRequired();
            e.HasIndex(x => x.OrganizationId);
        });

        builder.Entity<Policy>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.AllowedCommandPatterns)
                .HasColumnType("jsonb")
                .HasConversion(stringListConverter)
                .Metadata.SetValueComparer(stringListComparer);
            e.Property(x => x.DeniedCommandPatterns)
                .HasColumnType("jsonb")
                .HasConversion(stringListConverter)
                .Metadata.SetValueComparer(stringListComparer);
            e.HasIndex(x => x.OrganizationId);
        });

        builder.Entity<UserPolicy>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).HasMaxLength(255).IsRequired();
            e.HasOne(x => x.Policy).WithMany(x => x.UserPolicies).HasForeignKey(x => x.PolicyId);
            e.HasOne(x => x.Host).WithMany().HasForeignKey(x => x.HostId);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.HostId);
        });

        builder.Entity<Session>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(255);
            e.Property(x => x.IsJobRunSession).HasDefaultValue(false);
            e.Property(x => x.TotalCost).HasPrecision(10, 6);
            e.Property(x => x.HostIds)
                .HasColumnType("jsonb")
                .HasConversion(guidListConverter)
                .Metadata.SetValueComparer(guidListComparer);
            e.HasMany(x => x.Messages).WithOne(x => x.Session).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.OrganizationId, x.UserId });
        });

        builder.Entity<PromptSettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).HasMaxLength(255).IsRequired();
            e.Property(x => x.DefaultModel).HasMaxLength(100);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique();
        });

        builder.Entity<Message>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Role).HasMaxLength(50).IsRequired();
            e.Property(x => x.Content).IsRequired();
            e.HasIndex(x => new { x.SessionId, x.CreatedAt });
        });

        builder.Entity<Job>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.TriggerType).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.Prompt).HasColumnType("text");
            e.Property(x => x.CronSchedule).HasMaxLength(120);
            e.Property(x => x.WebhookSecret).HasMaxLength(255);
            e.Property(x => x.UserId).HasMaxLength(255).IsRequired();
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasMany(x => x.Runs).WithOne(x => x.Job).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.OrganizationId, x.UserId });
        });

        builder.Entity<JobRun>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TriggeredBy).HasMaxLength(50).IsRequired();
            e.Property(x => x.Status).HasMaxLength(50).IsRequired();
            e.Property(x => x.Payload).HasColumnType("text");
            e.Property(x => x.Response).HasColumnType("text");
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId);
            e.HasIndex(x => new { x.JobId, x.CreatedAt });
        });

        builder.Entity<CommandExecution>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Command).IsRequired();
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId);
            e.HasOne(x => x.Host).WithMany().HasForeignKey(x => x.HostId);
            e.HasIndex(x => x.SessionId);
            e.HasIndex(x => x.HostId);
            e.HasIndex(x => x.UserId);
        });

        builder.Entity<AuditLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.MetadataJson).HasColumnType("jsonb");
            e.HasOne(x => x.Execution).WithMany().HasForeignKey(x => x.ExecutionId);
            e.HasIndex(x => new { x.OrganizationId, x.Timestamp });
            e.HasIndex(x => new { x.UserId, x.Timestamp });
            e.HasIndex(x => new { x.HostId, x.Timestamp });
            e.HasIndex(x => new { x.EventType, x.Timestamp });
            e.HasIndex(x => x.Timestamp);
        });

        // Dictionary<string,string> value converter for EnvironmentVariables
        var stringDictConverter = new ValueConverter<Dictionary<string, string>, string>(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new Dictionary<string, string>());

        var stringDictComparer = new ValueComparer<Dictionary<string, string>>(
            (c1, c2) => c1 != null && c2 != null && c1.Count == c2.Count &&
                        c1.All(kv => c2.ContainsKey(kv.Key) && c2[kv.Key] == kv.Value),
            c => c.Aggregate(0, (a, kv) => HashCode.Combine(a, kv.Key.GetHashCode(), kv.Value.GetHashCode())),
            c => new Dictionary<string, string>(c));

        builder.Entity<McpServer>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.TransportType).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.BaseUrl).HasMaxLength(2048);
            e.Property(x => x.Command).HasMaxLength(1024);
            e.Property(x => x.Arguments).HasMaxLength(2048);
            e.Property(x => x.WorkingDirectory).HasMaxLength(1024);
            e.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
            e.Property(x => x.IsEnabled).HasDefaultValue(true);
            e.Property(x => x.EnvironmentVariables)
                .HasColumnType("jsonb")
                .HasConversion(stringDictConverter)
                .Metadata.SetValueComparer(stringDictComparer);
            e.HasIndex(x => x.OrganizationId);
            e.HasIndex(x => new { x.OrganizationId, x.IsEnabled });
        });

        builder.Entity<AccessToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired(); // SHA-256 hex = 64 chars
            e.Property(x => x.UserId).HasMaxLength(255).IsRequired();
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.IsActive });
            e.HasIndex(x => x.OrganizationId);
        });
    }
}
