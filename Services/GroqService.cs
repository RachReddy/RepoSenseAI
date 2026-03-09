using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RepoSenseAI.Models;

namespace RepoSenseAI.Services;

public class GroqService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<GroqService> _logger;

    private const string GroqUrl = "https://api.groq.com/openai/v1/chat/completions";
    private const string Model = "llama-3.3-70b-versatile";

    public GroqService(IConfiguration config, ILogger<GroqService> logger)
    {
        _logger = logger;
        _apiKey = config["Groq:ApiKey"] ?? Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("AI API key is not configured.");

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<AnalysisResult> AnalyzeRepoAsync(RepoContext context)
    {
        var prompt = BuildPrompt(context);

        _logger.LogInformation("Sending repo context to Groq for analysis: {Repo}", context.RepoName);

        var requestBody = new
        {
            model = Model,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You are a senior software architect. You analyze GitHub repositories and return structured JSON analysis. Return ONLY raw JSON, no markdown, no code blocks."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            },
            temperature = 0.4,
            max_tokens = 2048
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(GroqUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("Groq API error {Status}: {Body}", response.StatusCode, errorBody);
            throw new Exception($"Groq API returned {response.StatusCode}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("Groq response received for: {Repo}", context.RepoName);

        var rawText = ExtractTextFromResponse(responseJson);
        return ParseResponse(rawText, context);
    }

    private static string ExtractTextFromResponse(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    private static string BuildPrompt(RepoContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Repository: {context.Owner}/{context.RepoName}");
        sb.AppendLine($"Description: {context.Description}");
        sb.AppendLine($"Primary Language: {context.Language}");
        sb.AppendLine($"Stars: {context.Stars} | Forks: {context.Forks} | Open Issues: {context.OpenIssues}");
        sb.AppendLine($"Created: {context.CreatedAt} | Last Updated: {context.UpdatedAt}");
        sb.AppendLine($"Total Files: {context.TotalFileCount}");
        sb.AppendLine();
        sb.AppendLine("=== FILE STRUCTURE ===");
        sb.AppendLine(context.FileTree);
        sb.AppendLine();

        if (context.KeyFiles.Count > 0)
        {
            sb.AppendLine("=== KEY FILE CONTENTS ===");
            foreach (var (path, fileContent) in context.KeyFiles)
            {
                sb.AppendLine($"--- {path} ---");
                sb.AppendLine(fileContent);
                sb.AppendLine();
            }
        }

        sb.AppendLine("=== INSTRUCTIONS ===");
        sb.AppendLine("You are a senior engineer doing a thorough code review. Be specific, opinionated, and technical. Reference actual file names and patterns you see.");
        sb.AppendLine("Return ONLY a valid JSON object. No extra text, no markdown, no code blocks. Just raw JSON.");
        sb.AppendLine("CRITICAL: improvements and whatsDoneWell must be JSON arrays of plain strings. No numbers like 1. 2. 3. inside the array.");
        sb.AppendLine("Use this exact JSON structure. Return raw JSON only, no markdown:");
        sb.AppendLine("""
{
  "summary": "3 sentences: what it does, who it is for, and one non-obvious technical observation about this specific codebase",
  "architecture": "Name the exact architectural pattern. Describe each layer and what it does. Explain one specific interesting decision visible in the code.",
  "techStack": "Comma-separated list of all detected technologies, frameworks, libraries, databases, CI/CD tools",
  "improvements": ["specific improvement referencing an actual file or pattern seen in the code", "another specific improvement", "another", "another"],
  "whatsDoneWell": ["specific strength with file or pattern reference", "another specific strength", "another specific strength"],
  "mermaidDiagram": "graph TD\n    A[Node1] --> B[Node2]\n    B -->|label| C[Node3]\n    C --> D[Node4]"
}
""");
        sb.AppendLine("For mermaidDiagram: use ONLY --> and -->|label| arrows. No semicolons. No special characters in node labels. No subgraphs.");

        return sb.ToString();
    }

    private AnalysisResult ParseResponse(string rawText, RepoContext context)
    {
        var result = new AnalysisResult
        {
            RepoName = $"{context.Owner}/{context.RepoName}",
            RepoUrl = $"https://github.com/{context.Owner}/{context.RepoName}"
        };

        try
        {
            var json = rawText.Trim();

            // Strip markdown code blocks if present
            if (json.StartsWith("```"))
            {
                var firstNewline = json.IndexOf('\n');
                var lastBlock = json.LastIndexOf("```");
                if (firstNewline > 0 && lastBlock > firstNewline)
                    json = json[(firstNewline + 1)..lastBlock].Trim();
            }

            // Escape real newlines inside JSON string values
            json = SanitizeJsonNewlines(json);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            result.Summary = GetString(root, "summary");
            result.Architecture = GetString(root, "architecture");
            result.TechStack = GetString(root, "techStack");
            result.WhatsDoneWell = GetImprovements(root, "whatsDoneWell");
            result.Improvements = GetImprovements(root, "improvements");
            result.MermaidDiagram = GetString(root, "mermaidDiagram");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not parse AI JSON response: {Error}", ex.Message);
            result.Summary = rawText;
            result.Architecture = "Could not parse structured response.";
            result.TechStack = context.Language;
            result.Improvements = "Please try again.";
            result.MermaidDiagram = "graph TD\n    A[Repository] --> B[Analysis Failed]";
        }

        return result;
    }

    // Walks through the JSON and escapes real newlines/carriage returns inside string values
    private static string SanitizeJsonNewlines(string json)
    {
        var sb = new StringBuilder(json.Length);
        bool inString = false;
        bool escaped = false;

        foreach (char c in json)
        {
            if (escaped)
            {
                sb.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                sb.Append(c);
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                sb.Append(c);
                continue;
            }

            if (inString && c == '\n')
            {
                sb.Append("\\n");
                continue;
            }

            if (inString && c == '\r')
                continue;

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string GetString(JsonElement root, string key)
    {
        return root.TryGetProperty(key, out var val) ? val.GetString() ?? string.Empty : string.Empty;
    }

    // Handles improvements as either a JSON array or a plain string
    private static string GetImprovements(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var val)) return string.Empty;

        if (val.ValueKind == JsonValueKind.Array)
        {
            var items = val.EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            return string.Join("\n", items);
        }

        return val.GetString() ?? string.Empty;
    }
}
