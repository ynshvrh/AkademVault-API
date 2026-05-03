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
    }
}