using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ScratchpadSharp.Core.Services;

public class RoslynSession
{
    public string TabId { get; }
    public ProjectId ProjectId { get; }
    public DocumentId DocumentId { get; }

    // Caching fields to prevent redundant updates
    public Dictionary<string, string>? LastAppliedPackages { get; set; }
    public string? LastCode { get; set; }
    public List<string>? LastUsings { get; set; }

    public RoslynSession(string tabId, ProjectId projectId, DocumentId documentId)
    {
        TabId = tabId;
        ProjectId = projectId;
        DocumentId = documentId;
    }
}
