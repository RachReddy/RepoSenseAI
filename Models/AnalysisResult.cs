namespace RepoSenseAI.Models;

public class AnalysisResult
{
    public string RepoName { get; set; } = string.Empty;
    public string RepoUrl { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string TechStack { get; set; } = string.Empty;
    public string WhatsDoneWell { get; set; } = string.Empty;
    public string Improvements { get; set; } = string.Empty;
    public string MermaidDiagram { get; set; } = string.Empty;
}
