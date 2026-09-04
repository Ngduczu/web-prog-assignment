using LMS.Api.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public  AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {}

    public DbSet<User> Users => Set<User>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly
        );

        base.OnModelCreating(modelBuilder);
    }
}
