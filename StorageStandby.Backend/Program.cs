using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StorageStandby.Backend.Core;
using StorageStandby.Backend.Data;
using StorageStandby.Backend.Models;
using StorageStandby.Backend.Services;
using StorageStandby.Backend.Workers;
using System.Linq;

// Backend ASP.NET Core C# project
// Uses built-in web server (Kestrel) to listen to a local pipe instead of an open network port

var builder = WebApplication.CreateBuilder(args);

// run as native Windows Service
builder.Host.UseWindowsService();

// configure Kestrel to listen to named pipe instead of traditional TCP port
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenNamedPipe("StorageStandbyPipe");
});

// Register Singleton State machine + long-running fileSystem watcher as a hosted background worker
builder.Services.AddSingleton<BackupEngineState>();
builder.Services.AddSingleton<TokenManager>();
builder.Services.AddSingleton<LocalFileSystemService>();
builder.Services.AddSingleton<SyncManager>();
builder.Services.AddSingleton<WatchedFolderService>();
// .NET automatically registers IServiceScopeFactory as a Singleton infrastructure service behind the scenes as soon as the service collection is created -- no need to declare!
builder.Services.AddDbContext<AppDbContext>(options =>
{
    // Tells EF Core to use SQLite and names the database file
    options.UseSqlite("Data Source=storagestandby.db");
});
builder.Services.AddScoped<GoogleDriveProvider>();
builder.Services.AddScoped<OneDriveProvider>();
builder.Services.AddHostedService<FileSystemWatcherWorker>(); // configures as a background service, akin to a singleton
builder.Services.AddMemoryCache();

// Automatically registers GoogleDriveProvider as a service AND manages its HttpClient
builder.Services.AddHttpClient<GoogleDriveProvider>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient<OneDriveProvider>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5));
// Register Providers typed client and configure HttpClientFactory handler rotation (prevents stale DNS)
foreach (Providers provider in Enum.GetValues<Providers>())
{
    if (provider == Providers.None) continue;

    builder.Services.AddHttpClient(ProviderMetadata.ProviderNames[provider])
        .SetHandlerLifetime(TimeSpan.FromMinutes(5));

}

// Register IDataProtectionProvider for encrypting sensitive data
builder.Services.AddDataProtection();

// Configure CORS based on environment
builder.Services.AddCors(options =>
{
    if (builder.Environment.IsDevelopment())
    {
        // Development: allow Vite dev server
        options.AddPolicy("AllowFrontend", policy =>
        {
            policy.WithOrigins("http://localhost:5173", "http://storagestandby.local")
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    }
    else
    {
        // Production: allow virtual domain
        options.AddPolicy("AllowFrontend", policy =>
        {
            policy.WithOrigins("http://storagestandby.local")
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    }
});

// DEV: AddSingleton - one instance for the entire app lifetime
// DEV: AddScoped - one instance per request (for example, one instance is used throughout one entire HTTPReq)
// DEV: AddTransient - a new instance is created every time it's requested (for example, if you inject it into multiple classes, each class gets its own instance)

var app = builder.Build();

// Enable CORS
app.UseCors("AllowFrontend");

// Initialize Database and load saved folders into State Engine
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var state = scope.ServiceProvider.GetRequiredService<BackupEngineState>();
    var googleDriveProvider = scope.ServiceProvider.GetRequiredService<GoogleDriveProvider>();
    var tokenManager = scope.ServiceProvider.GetRequiredService<TokenManager>();

    try
    {
        await db.Database.MigrateAsync(); // Applies pending migrations auto
    }
    catch (Exception ex)
    {
        // Log migration failures but don't crash the app
        app.Services.GetRequiredService<ILogger<Program>>()
            .LogError(ex, "Database migration failed.");
        throw;
    }

    // automatically creates SQLite file if it doesn't exist
    //db.Database.EnsureCreated(); // <-- dev-only

    // log saved folders into state machine
    var savedFolders = db.WatchedFolders.Select(f => f.LocalPath).ToList();
    state.ActiveWatchedPaths.AddRange(savedFolders);
}

// -------------------------------------------------------------------
// REACT VITE MINIMAL API ENDPOINTS (http://storagestandby.local) - these are consumed by the React frontend via fetch()
// -------------------------------------------------------------------

// -------------------------------------------------------------------
// OPTIONS: catch-all CORS preflight fallback
// The Desktop app's WebResourceRequested handler now forwards Origin +
// Access-Control-Request-* headers down the pipe, so the CORS middleware
// (app.UseCors("AllowFrontend") above) handles real preflight requests and
// short-circuits them with a 204 + the proper Access-Control-Allow-* headers.
// This catch-all only catches non-preflight OPTIONS calls (e.g. from tools like
// curl) that the middleware passes through. Specific GET/POST routes always win
// via route precedence.
app.MapMethods("/api/{**path}", new[] { "OPTIONS" }, () => Results.NoContent());

// -------------------------------------------------------------------
// AUTH: OPTIONS
// app.MapMethods("/api/auth/{provider}/{action}", new[] { "OPTIONS" }, (string provider, string action) =>
// {
//     return Results.Ok();
// });

// -------------------------------------------------------------------
// RFC 7807 ProblemDetails EXAMPLE...
// for structured error responses
//var problemDetails = new ProblemDetails
//{
//    Type = "https://example.com/errors/missing-token",
//    Title = "Token is missing",
//    Status = StatusCodes.Status400BadRequest,
//    Detail = "The 'token' field is required in the request body.",
//    Instance = "/api/auth/google/revoke"
//};


// GET: React polling for dashboard telemetry
app.MapGet("/api/status", (BackupEngineState state) => Results.Ok(new
{
    Status = state.Status,
    IsPaused = state._isSyncPaused,
    CurrentOperation = state.CurrentOperation,
    LastSyncedFile = state.LastSyncedFile,
    QueueCount = state.ActiveUploadQueueCount,
    WatchedFolders = state.ActiveWatchedPaths
}));

// POST: React toggling the pause button
app.MapPost("/api/sync/pause", (bool pauseState, BackupEngineState state) =>
{
    state._isSyncPaused = pauseState;
    state.Status = pauseState ? "Paused" : "Running";
    return Results.Ok(new { Paused = state._isSyncPaused });
});

app.MapPost("/api/sync/trigger", () =>
{
    // TODO: Trigger a manual sync operation in your backup engine
    return Results.Ok(new { Message = "Sync initiated successfully." });
});

// POST: React adding a new folder to track
app.MapPost("/api/folders/add", async (
    WatchedFolder request, 
    AppDbContext db, 
    BackupEngineState state) =>
{
    // check if it already exists
    if (db.WatchedFolders.Any(f => f.LocalPath == request.LocalPath))
    {
        return Results.Conflict(new { Message = "Folder is already being tracked." });
    }

    db.WatchedFolders.Add(request);
    await db.SaveChangesAsync();

    state.NotifyNewFolderAdded(request.LocalPath);

    return Results.Ok(new { Message = "Folder added successfully." });
});

// -------------------------------------------------------------------
// (!!) LOCAL FILESYSTEM REST API ENDPOINTS (!!)
// -------------------------------------------------------------------

app.MapGet("/api/fileSystem/getsize/{localPath}", (
    string localPath,
    LocalFileSystemService fileSystem) =>
{
    return fileSystem.GetPathSize(localPath);
});

app.MapGet("/api/fileSystem/isfolder/{localPath}", (
    string localPath,
    LocalFileSystemService fileSystem) =>
{
    return fileSystem.IsFolder(localPath);
});

// Hmm... Ecosia spat out a bunch
app.MapGet("/api/fileSystem/folder/getchildrencount", (
    string localPath,
    LocalFileSystemService fileSystem) =>
{
    return fileSystem.GetChildrenCount(localPath);
});

// -------------------------------------------------------------------
// (!!) EF CORE-BASED REST API DB ENDPOINTS (!!)
// -------------------------------------------------------------------

// -------------------------------------------------------------------
// DB: FOLDERS
app.MapGet("/api/folders", (AppDbContext db) =>
{
    var folders = db.WatchedFolders.ToList();
    return Results.Ok(folders);
});

app.MapGet("/api/folders/{localPath}", async (
    string localPath, 
    AppDbContext db) =>
{
    var folder = await db.WatchedFolders.FirstOrDefaultAsync(f => f.LocalPath == localPath);
    if (folder == null)
    {
        return Results.NotFound(new { Message = "Folder not found." });
    }
    return Results.Ok(folder);
});

// public record IgnoreRulesChangeRequest(string OldIgnoreRules, string NewIgnoreRules);
app.MapPost("/api/folders/{watchedFolderId:long}/ignore-rules/reconcile", async (
    long watchedFolderId,
    // IgnoreRulesChangeRequest request,
    FileSystemWatcherWorker watcherWorker,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var folder = await db.WatchedFolders.FindAsync([watchedFolderId], cancellationToken);
    if (folder is null)
    {
        return Results.NotFound(new { Message = "Folder not found." });
    }

    await watcherWorker.ReconcileIgnoreRulesAsync(
        watchedFolderId,
        request.OldIgnoreRules,
        request.NewIgnoreRules,
        cancellationToken);

    folder.IgnoreRules = request.NewIgnoreRules;
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(new { Message = "Ignore rules reconciled successfully." });
});

// -------------------------------------------------------------------
// DB: SETTINGS
app.MapGet("/api/settings", (AppDbContext db) =>
{
    var settings = db.SystemSettings.FirstOrDefault();
    if (settings == null)
    {
        return Results.NotFound(new { Message = "Settings not found." });
    }
    return Results.Ok(settings);
});

app.MapGet("/api/settings/globalignore", (AppDbContext db) =>
{
    var settings = db.SystemSettings.FirstOrDefault();
    if (settings == null)
    {
        return Results.NotFound(new { Message = "Settings not found." });
    }
    return Results.Ok(settings.GlobalIgnoreRules);
});

app.MapPost("/api/settings/globalignore", async (string ignoreRules, AppDbContext db) =>
{
    var settings = await db.SystemSettings.FirstOrDefaultAsync();
    if (settings == null)
    {
        return Results.NotFound(new { Message = "Settings not found." });
    }
    settings.GlobalIgnoreRules = ignoreRules;
    db.SaveChanges();
    return Results.Ok(new { Message = "Global ignore rules updated successfully." });
});

// -------------------------------------------------------------------
// DB: SYNC EVENTS
app.MapGet("/api/syncevents", async (AppDbContext db) =>
{
    var eventsByMostRecent = await db.Set<SyncEvent>().OrderByDescending(e => e.CompletedTimestamp).ToListAsync();
    return Results.Ok(eventsByMostRecent);
});

// -------------------------------------------------------------------
// (!!) AUTH-RELATED REST API ENDPOINTS (!!)
// -------------------------------------------------------------------

// -------------------------------------------------------------------
// AUTH: GOOGLE REST API
app.MapPost("/api/auth/google/start", async (
    // dependency injection through builder.Services.AddSingleton/AddScoped
    GoogleDriveProvider googleDriveProvider // adds modularity over direct reference
) =>
{
    Console.WriteLine("[API] /auth/google/start endpoint hit");
    // generate secure OAuth authorization URL and return it to the frontend
    // React opens that URL in user's default browser (window.open(url))
    // user logs in, and is redirected to temporary local port that C# backend spins up specifically to catch auth token, e.g. localhost:8080/callback
    // C# encrypts and saves the token to SQLite
    AuthResult result = await googleDriveProvider.StartOAuthAsync();

    if (!result.Success)
    {
        return Results.Problem(detail: result.Message, statusCode: 400);
    }

    return Results.Ok(result);
});

app.MapPost("/api/auth/google/revoke", async (
    RevokeReq request,
    GoogleDriveProvider googleDriveProvider
) =>
{
    Console.WriteLine("[API] /auth/google/revoke endpoint hit");
    if (request == null || string.IsNullOrEmpty(request.AccountId))
    {
        return Results.BadRequest("AccountId field is missing.");
    }

    AuthResult result = await googleDriveProvider.RevokeOAuthAsync(request.AccountId);

    if (!result.Success)
    {
        return Results.Problem(detail: result.Message, statusCode: 400);
    }

    return Results.Ok(result);
});

app.MapGet("/api/auth/google/accounts", async (
    GoogleDriveProvider googleDriveProvider) =>
{
    Console.WriteLine("[API] /auth/google/accounts endpoint hit");
    try
    {
        var accounts = await googleDriveProvider.GetConnectedAccounts();
        return Results.Ok(accounts);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {ex.Message}");
        return Results.Problem(detail: "Failed to fetch accounts: " + ex.Message, statusCode: 500);
    }
});

// -------------------------------------------------------------------
// AUTH: MICROSOFT REST API
app.MapPost("/api/auth/microsoft/start", async () =>
{ 

    return Results.Ok(); 
});

app.MapGet("/api/auth/microsoft/accounts", () =>
{

    return Results.Ok(new List<object>());
});


// -------------------------------------------------------------------
// AUTH: DROPBOX REST API
app.MapPost("/api/auth/dropbox/start", async () =>
{

    return Results.Ok();
});

app.MapGet("/api/auth/dropbox/accounts", () =>
{

    return Results.Ok(new List<object>());
});

await app.RunAsync();

// -------------------------------------------------------------------
// TYPES
// -------------------------------------------------------------------

public class RevokeReq
{
    public string AccountId { get; set; }
}