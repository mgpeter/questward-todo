using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TodoApp.Data;

/// <summary>
/// Used only by `dotnet ef` at design time so migrations can be created without booting the API.
/// The connection string is never used to run the app.
/// </summary>
public class TodoDbContextFactory : IDesignTimeDbContextFactory<TodoDbContext>
{
    public TodoDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("TODOAPP_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=questward;Username=questward;Password=questward";

        var options = new DbContextOptionsBuilder<TodoDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TodoDbContext(options);
    }
}
