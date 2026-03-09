using Microsoft.AspNetCore.Mvc;
using RepoSenseAI.Models;
using RepoSenseAI.Services;

namespace RepoSenseAI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalysisController : ControllerBase
{
    private readonly GitHubService _gitHubService;
    private readonly GroqService _groqService;
    private readonly ILogger<AnalysisController> _logger;

    public AnalysisController(GitHubService gitHubService, GroqService geminiService, ILogger<AnalysisController> logger)
    {
        _gitHubService = gitHubService;
        _groqService = geminiService;
        _logger = logger;
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] AnalysisRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RepoUrl))
            return BadRequest(new { error = "Repository URL is required." });

        if (!request.RepoUrl.Contains("github.com"))
            return BadRequest(new { error = "Only GitHub repository URLs are supported." });

        try
        {
            _logger.LogInformation("Analyzing repository: {Url}", request.RepoUrl);

            var repoContext = await _gitHubService.FetchRepoContextAsync(request.RepoUrl);
            var analysisResult = await _groqService.AnalyzeRepoAsync(repoContext);

            return Ok(analysisResult);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Octokit.NotFoundException)
        {
            return NotFound(new { error = "Repository not found. Make sure it is public and the URL is correct." });
        }
        catch (Octokit.RateLimitExceededException)
        {
            return StatusCode(429, new { error = "GitHub API rate limit reached. Please try again in a few minutes." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing repository {Url}", request.RepoUrl);
            return StatusCode(500, new { error = "Something went wrong. Please try again." });
        }
    }
}
