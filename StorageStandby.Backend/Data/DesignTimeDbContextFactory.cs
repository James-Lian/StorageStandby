using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using StorageStandby.Backend.Data;

namespace StorageStandby.Backend.Data
{
    // This class is ONLY used by the command line tools when you run "dotnet ef migrations add"
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

            // Tell the tool to use SQLite and point it to a local file for the schema generation
            optionsBuilder.UseSqlite("Data Source=backupmanager.db");

            return new AppDbContext(optionsBuilder.Options);
        }
    }
}