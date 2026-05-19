using Microsoft.EntityFrameworkCore;
using AkademVault_API.Models;

namespace AkademVault_API.Data;

// EF Core DbContext: holds DbSets for every domain entity and wires up FluentAPI relationships and indexes.
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Group> Groups { get; set; }
    public DbSet<JoinRequest> JoinRequests { get; set; }
    public DbSet<Assignment> Assignments { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<LectureMaterial> LectureMaterials { get; set; }
    public DbSet<MaterialComment> MaterialComments { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<Invitation> Invitations { get; set; }
    public DbSet<GroupInviteLink> GroupInviteLinks { get; set; }
    public DbSet<ScheduleEntry> ScheduleEntries { get; set; }
    public DbSet<MessageRead> MessageReads { get; set; }

    // Configures FK delete behaviours and indexes; deletion rules encode the domain invariants.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);


        // Deleting a Group releases its members rather than cascading them out.
        modelBuilder.Entity<User>()
            .HasOne(u => u.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(u => u.GroupId)
            .OnDelete(DeleteBehavior.SetNull);


        // Restrict: an Owner cannot be deleted while they still own a Group.
        modelBuilder.Entity<Group>()
            .HasOne(g => g.Owner)
            .WithMany()
            .HasForeignKey(g => g.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<Group>()
            .HasIndex(g => g.ShortCode)
            .IsUnique();


        modelBuilder.Entity<Assignment>()
            .HasOne(a => a.Group)
            .WithMany()
            .HasForeignKey(a => a.GroupId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<ChatMessage>()
            .HasOne(m => m.Group)
            .WithMany()
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<ChatMessage>()
            .HasOne(m => m.Sender)
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Restrict);


        // Composite index powers the paginated chat history query (WHERE GroupId ORDER BY SentAt DESC).
        modelBuilder.Entity<ChatMessage>()
            .HasIndex(m => new { m.GroupId, m.SentAt });


        modelBuilder.Entity<LectureMaterial>()
            .HasOne(m => m.Group)
            .WithMany()
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<LectureMaterial>()
            .HasOne(m => m.Uploader)
            .WithMany()
            .HasForeignKey(m => m.UploaderId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<LectureMaterial>()
            .HasIndex(m => new { m.GroupId, m.UploadedAt });


        modelBuilder.Entity<MaterialComment>()
            .HasOne(c => c.Material)
            .WithMany(m => m.Comments)
            .HasForeignKey(c => c.MaterialId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<MaterialComment>()
            .HasOne(c => c.Author)
            .WithMany()
            .HasForeignKey(c => c.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);


        // Self-FK for threaded replies; Restrict prevents accidental tree corruption on delete.
        modelBuilder.Entity<MaterialComment>()
            .HasOne(c => c.ParentComment)
            .WithMany(c => c.Replies)
            .HasForeignKey(c => c.ParentCommentId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<Notification>()
            .HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);


        // Composite index powers the inbox query (WHERE UserId AND IsRead ORDER BY CreatedAt DESC).
        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });


        modelBuilder.Entity<Invitation>()
            .HasOne(i => i.Group)
            .WithMany()
            .HasForeignKey(i => i.GroupId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<Invitation>()
            .HasOne(i => i.InvitedUser)
            .WithMany()
            .HasForeignKey(i => i.InvitedUserId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<Invitation>()
            .HasOne(i => i.InvitedBy)
            .WithMany()
            .HasForeignKey(i => i.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<Invitation>()
            .HasIndex(i => new { i.InvitedUserId, i.Status });


        modelBuilder.Entity<GroupInviteLink>()
            .HasOne(l => l.Group)
            .WithMany()
            .HasForeignKey(l => l.GroupId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<GroupInviteLink>()
            .HasIndex(l => l.Token)
            .IsUnique();


        modelBuilder.Entity<ScheduleEntry>()
            .HasOne(s => s.Group)
            .WithMany()
            .HasForeignKey(s => s.GroupId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<ScheduleEntry>()
            .HasIndex(s => new { s.GroupId, s.DayOfWeek, s.StartTime });


        // Composite PK (MessageId, UserId) — each user can mark a given message as read once.
        modelBuilder.Entity<MessageRead>()
            .HasKey(r => new { r.MessageId, r.UserId });

        modelBuilder.Entity<MessageRead>()
            .HasOne(r => r.Message)
            .WithMany(m => m.Reads)
            .HasForeignKey(r => r.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MessageRead>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MessageRead>()
            .HasIndex(r => r.UserId);
    }
}
