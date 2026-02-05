using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace ScratchpadSharp.Core.Services;

public interface IRoslynCompletionService
{
    Task<ImmutableArray<CompletionItem>> GetCompletionsAsync(string tabId, string code, int position, List<string> usings, Dictionary<string, string> nugetPackages, CancellationToken cancellationToken = default);
    Task UpdateReferencesAsync(string tabId, Dictionary<string, string> nugetPackages);
}

public class RoslynCompletionService : IRoslynCompletionService
{
    public async Task UpdateReferencesAsync(string tabId, Dictionary<string, string> nugetPackages)
    {
        await RoslynWorkspaceService.Instance.UpdateReferencesAsync(tabId, nugetPackages);
    }

    public async Task<ImmutableArray<CompletionItem>> GetCompletionsAsync(
        string tabId,
        string code, 
        int position,
        List<string> usings,
        Dictionary<string, string> nugetPackages,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!RoslynWorkspaceService.Instance.IsInitialized)
            {
                System.Diagnostics.Debug.WriteLine("[Completion] Workspace not initialized yet");
                return ImmutableArray<CompletionItem>.Empty;
            }

            // Update references if packages are provided
            if (nugetPackages?.Count > 0)
            {
                await UpdateReferencesAsync(tabId, nugetPackages);
            }
            
            // Update document with current code
            await RoslynWorkspaceService.Instance.UpdateDocumentAsync(tabId, code, usings);

            // Get the current document
            var document = RoslynWorkspaceService.Instance.GetDocument(tabId);

            // Calculate adjusted position
            var adjustedPosition = RoslynWorkspaceService.Instance.CalculateAdjustedPosition(position, usings);

            // Get completion service
            var completionService = CompletionService.GetService(document);
            if (completionService == null)
            {
                System.Diagnostics.Debug.WriteLine("[Completion] CompletionService is null");
                return ImmutableArray<CompletionItem>.Empty;
            }

            // Get completions at adjusted position
            var completions = await completionService.GetCompletionsAsync(
                document, 
                adjustedPosition, 
                cancellationToken: cancellationToken);

            if (completions == null || completions.ItemsList.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Completion] No completions at position {position}");
                return ImmutableArray<CompletionItem>.Empty;
            }

            System.Diagnostics.Debug.WriteLine($"[Completion] Found {completions.ItemsList.Count} items");
            return completions.ItemsList.ToImmutableArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Completion] Error: {ex.Message}");
            return ImmutableArray<CompletionItem>.Empty;
        }
    }
}
