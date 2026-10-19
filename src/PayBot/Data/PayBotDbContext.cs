using Microsoft.EntityFrameworkCore;
using PayBot.Domain;

namespace PayBot.Data;

public sealed class PayBotDbContext(DbContextOptions<PayBotDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<WhatsAppGroup> Groups => Set<WhatsAppGroup>();
    public DbSet<LedgerEntry> Transactions => Set<LedgerEntry>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<ConversationSession> Sessions => Set<ConversationSession>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(entry =>
        {
            entry.ToTable("tenants");
            entry.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entry.Property(x => x.NormalizedName).HasMaxLength(80).IsRequired();
            entry.HasIndex(x => x.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<WhatsAppGroup>(entry =>
        {
            entry.ToTable("groups");
            entry.Property(x => x.Jid).HasMaxLength(128).IsRequired();
            entry.HasIndex(x => x.Jid).IsUnique();
            entry.HasOne(x => x.Tenant)
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LedgerEntry>(entry =>
        {
            entry.ToTable("transactions");
            entry.HasKey(x => x.Id);
            entry.Property(x => x.Amount).HasPrecision(14, 2);
            entry.Property(x => x.Description).HasMaxLength(200);
            entry.Property(x => x.WhatsAppMessageId).HasMaxLength(128).IsRequired();
            entry.HasIndex(x => x.WhatsAppMessageId).IsUnique();
            entry.HasIndex(x => x.LocalDate);
            entry.HasIndex(x => new { x.TenantId, x.LocalDate });
            entry.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            entry.HasOne(x => x.Card)
                .WithMany()
                .HasForeignKey(x => x.CardId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Card>(entry =>
        {
            entry.ToTable("cards");
            entry.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entry.Property(x => x.NormalizedName).HasMaxLength(80).IsRequired();
            entry.HasIndex(x => new { x.TenantId, x.Kind, x.NormalizedName }).IsUnique();
            entry.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConversationSession>(entry =>
        {
            entry.ToTable("sessions");
            entry.Property(x => x.GroupJid).HasMaxLength(128).IsRequired();
            entry.HasIndex(x => x.GroupJid).IsUnique();
            entry.Property(x => x.DraftJson).IsRequired();
        });

        modelBuilder.Entity<ProcessedMessage>(entry =>
        {
            entry.ToTable("processed_messages");
            entry.HasKey(x => x.MessageId);
            entry.Property(x => x.MessageId).HasMaxLength(128);
        });
    }
}
