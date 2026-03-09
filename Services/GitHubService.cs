using Octokit;

namespace RepoSenseAI.Services;

public class GitHubService
{
    private readonly GitHubClient _client;
    private readonly ILogger<GitHubService> _logger;

    // File extensions to skip (binaries, images, locks)
    private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".svg", ".pdf",
        ".zip", ".tar", ".gz", ".exe", ".dll", ".so", ".dylib",
        ".min.js", ".map", ".lock", ".woff", ".woff2", ".ttf", ".eot"
    };

    // Folders to skip entirely
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", ".git", "dist", "build",
        "vendor", ".github", "coverage", "__pycache__", ".next",
        "out", "target", "packages"
    };

    // High priority files to always include if they exist
    private static readonly List<string> PriorityFileNames = new()
    {
        "README.md", "README.txt", "readme.md",
        "Program.cs", "Startup.cs",
        "package.json", "requirements.txt", "go.mod",
        "pom.xml", "Cargo.toml", "composer.json",
        "Dockerfile", "docker-compose.yml", "docker-compose.yaml",
        "appsettings.json", "application.properties", "application.yml",
        "index.js", "index.ts", "app.js", "app.ts", "server.js", "main.py",
        "main.go", "Main.java"
    };

    public GitHubService(IConfiguration config, ILogger<GitHubService> logger)
    {
        _logger = logger;
        var token = config["GitHub:Token"] ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");

        _client = new GitHubClient(new ProductHeaderValue("RepoSenseAI"));

        if (!string.IsNullOrWhiteSpace(token))
            _client.Credentials = new Credentials(token);
    }

    public async Task<RepoContext> FetchRepoContextAsync(string repoUrl)
    {
        var (owner, repoName) = ParseRepoUrl(repoUrl);

        var repo = await _client.Repository.Get(owner, repoName);
        var defaultBranch = repo.DefaultBranch;

        // Get full file tree
        var treeResponse = await _client.Git.Tree.GetRecursive(owner, repoName, defaultBranch);
        var allFiles = treeResponse.Tree
            .Where(t => t.Type.Value == TreeType.Blob)
            .Select(t => t.Path)
            .ToList();

        // Build readable file tree string
        var filteredTree = FilterTree(allFiles);
        var fileTreeString = string.Join("\n", filteredTree);

        // Fetch key file contents
        var keyFiles = await FetchKeyFilesAsync(owner, repoName, allFiles);

        return new RepoContext
        {
            Owner = owner,
            RepoName = repoName,
            Description = repo.Description ?? "No description provided",
            Stars = repo.StargazersCount,
            Forks = repo.ForksCount,
            OpenIssues = repo.OpenIssuesCount,
            Language = repo.Language ?? "Unknown",
            CreatedAt = repo.CreatedAt.ToString("MMM yyyy"),
            UpdatedAt = repo.UpdatedAt.ToString("MMM yyyy"),
            HasWiki = repo.HasWiki,
            FileTree = fileTreeString,
            TotalFileCount = allFiles.Count,
            KeyFiles = keyFiles
        };
    }

    private List<string> FilterTree(List<string> allFiles)
    {
        return allFiles
            .Where(path =>
            {
                var parts = path.Split('/');
                // Skip if any part of the path is a skipped folder
                if (parts.Any(p => SkippedFolders.Contains(p))) return false;
                // Skip by extension
                var ext = Path.GetExtension(path);
                if (SkippedExtensions.Contains(ext)) return false;
                return true;
            })
            .Take(200) // Cap tree size
            .ToList();
    }

    private async Task<Dictionary<string, string>> FetchKeyFilesAsync(string owner, string repo, List<string> allFiles)
    {
        var result = new Dictionary<string, string>();
        var filesToFetch = new List<string>();

        // First: add priority files that exist in the repo
        foreach (var priority in PriorityFileNames)
        {
            var match = allFiles.FirstOrDefault(f =>
                Path.GetFileName(f).Equals(priority, StringComparison.OrdinalIgnoreCase) &&
                !IsInSkippedFolder(f));
            if (match != null && !filesToFetch.Contains(match))
                filesToFetch.Add(match);
        }

        // Then: add .csproj files (project definitions)
        var csprojFiles = allFiles
            .Where(f => f.EndsWith(".csproj") && !IsInSkippedFolder(f))
            .Take(3);
        filesToFetch.AddRange(csprojFiles.Where(f => !filesToFetch.Contains(f)));

        // Cap at 12 files total
        filesToFetch = filesToFetch.Take(12).ToList();

        foreach (var filePath in filesToFetch)
        {
            try
            {
                var contents = await _client.Repository.Content.GetAllContents(owner, repo, filePath);
                if (contents.Count > 0 && contents[0].Content != null)
                {
                    // Trim content to 4000 chars to manage token usage
                    var content = contents[0].Content;
                    if (content.Length > 4000)
                        content = content[..4000] + "\n... (truncated)";

                    result[filePath] = content;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not fetch file {File}: {Message}", filePath, ex.Message);
            }
        }

        return result;
    }

    private static bool IsInSkippedFolder(string path)
    {
        return path.Split('/').Any(p => SkippedFolders.Contains(p));
    }

    private static (string owner, string repo) ParseRepoUrl(string url)
    {
        // Handle formats:
        // https://github.com/owner/repo
        // https://github.com/owner/repo.git
        // github.com/owner/repo
        url = url.Trim().TrimEnd('/');

        if (url.EndsWith(".git"))
            url = url[..^4];

        var uri = new Uri(url.StartsWith("http") ? url : "https://" + url);
        var parts = uri.AbsolutePath.Trim('/').Split('/');

        if (parts.Length < 2)
            throw new ArgumentException("Invalid GitHub repository URL.");

        return (parts[0], parts[1]);
    }
}

public class RepoContext
{
    public string Owner { get; set; } = string.Empty;
    public string RepoName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Stars { get; set; }
    public int Forks { get; set; }
    public int OpenIssues { get; set; }
    public string Language { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public bool HasWiki { get; set; }
    public int TotalFileCount { get; set; }
    public string FileTree { get; set; } = string.Empty;
    public Dictionary<string, string> KeyFiles { get; set; } = new();
}
