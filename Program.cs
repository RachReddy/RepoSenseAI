using RepoSenseAI.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddScoped<GitHubService>();
builder.Services.AddScoped<GroqService>();

// Listen on the PORT environment variable (required for Render deployment)
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

app.UseDefaultFiles();   // serves index.html by default
app.UseStaticFiles();    // serves wwwroot files

app.MapControllers();

app.Run();
