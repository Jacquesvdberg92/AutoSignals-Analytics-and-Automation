using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Seed roles
        builder.Entity<IdentityRole>().HasData(
            new IdentityRole { Name = "Free User", NormalizedName = "FREE USER" },
            new IdentityRole { Name = "Tester", NormalizedName = "TESTER" },
            new IdentityRole { Name = "Subscriber", NormalizedName = "SUBSCRIBER" },
            new IdentityRole { Name = "VIP", NormalizedName = "VIP" },
            new IdentityRole { Name = "Admin", NormalizedName = "ADMIN" }
        );
    }
}
