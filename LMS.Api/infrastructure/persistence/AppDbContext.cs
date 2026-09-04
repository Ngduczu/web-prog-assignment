using LMS.Api.domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public  AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {}

    public DbSet<Users> Users => Set<Users>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly
        );

        base.OnModelCreating(modelBuilder);
    }
}