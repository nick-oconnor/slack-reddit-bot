namespace SlackRedditBot.Web.Models
{
    using Microsoft.EntityFrameworkCore;

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions options) : base(options)
        {
        }

        public DbSet<Instance> Instances { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Instance>()
                .ToTable("instances")
                .HasKey(i => i.TeamId);
            modelBuilder.Entity<Instance>()
                .Property(i => i.TeamId)
                .HasColumnName("team_id")
                .HasMaxLength(9)
                .IsFixedLength()
                .IsRequired();
            modelBuilder.Entity<Instance>()
                .Property(i => i.AccessToken)
                .HasColumnName("access_token")
                .HasMaxLength(76)
                .IsFixedLength()
                .IsRequired();
        }
    }
}
