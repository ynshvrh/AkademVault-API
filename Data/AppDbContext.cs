using Microsoft.EntityFrameworkCore;
using AkademVault_API.Models;

namespace AkademVault_API.Data;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);


        modelBuilder.Entity<User>()
            .HasOne(u => u.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(u => u.GroupId)
            .OnDelete(DeleteBehavior.SetNull);


        modelBuilder.Entity<Group>()
            .HasOne(g => g.Owner)
            .WithMany()
            .HasForeignKey(g => g.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);


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


        modelBuilder.Entity<MaterialComment>()
            .HasOne(c => c.ParentComment)
            .WithMany(c => c.Replies)
            .HasForeignKey(c => c.ParentCommentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
