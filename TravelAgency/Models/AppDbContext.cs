namespace TravelAgency.Models;

using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    // כאן  טבלאות
    // לדוגמה:
    
    public DbSet<User> Users { get; set; }
}
