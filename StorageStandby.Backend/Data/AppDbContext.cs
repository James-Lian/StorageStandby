using Microsoft.EntityFrameworkCore; // EF Core - allows C# developers to work with databases instead of manually writing SQL
using StorageStandby.Backend.Models;
using System;
using System.IO;

namespace StorageStandby.Backend.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<WatchedFolder> WatchedFolders { get; set; }
        public DbSet<CloudToken> CloudTokens { get; set; }
        public DbSet<SystemSettings> SystemSettings { get; set; }

        // Yo reconsider this portion:
        public DbSet<FileSyncRecord> FileSyncRecords { get; set; }

        // The constructor passes configuration options (like the file path) to the base EF Core engine
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<SystemSettings>()
                .HasData(new SystemSettings { Id = 1 }); // ensure only one row will always be created
            // Configure CloudToken to use composite primary key on (ProviderName, AccountId)
            modelBuilder.Entity<CloudToken>()
                .HasKey(ct => new { ct.ProviderName, ct.AccountId });
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                // creates the DB in C:\Users\[User]\AppData\Local\StorageStandby
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appFolder = Path.Combine(folder, "StorageStandby");
                Directory.CreateDirectory(appFolder);

                // Appending Journal Mode=WAL enables Write-Ahead Logging automatically
                string dbPath = Path.Combine(appFolder, "backup_config.db");
                optionsBuilder.UseSqlite($"Data Source={dbPath};Journal Mode=WAL;");
            }
        }
    }
}
