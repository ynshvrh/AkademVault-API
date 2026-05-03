using Microsoft.EntityFrameworkCore;
using AkademVault_API.Models;

namespace AkademVault_API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; } 
}