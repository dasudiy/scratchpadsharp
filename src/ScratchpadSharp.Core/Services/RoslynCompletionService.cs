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
    Task<ImmutableArray<CompletionItem>> GetCompletionsAsync(string code, int position, List<string> usings, Dictionary<string, string> nugetPackages, CancellationToken cancellationToken = default);
    void UpdateReferences(Dictionary<string, string> nugetPackages);
}

public class RoslynCompletionService : IRoslynCompletionService
{
    private readonly AdhocWorkspace workspace;
    private readonly ProjectId projectId;
    private readonly DocumentId documentId;
    private Document currentDocument;

    public RoslynCompletionService()
    {
        System.Diagnostics.Debug.WriteLine("[Completion] Initializing RoslynCompletionService...");
        
        // Load all necessary assemblies for Roslyn completion
        var assemblies = new List<Assembly>();
        
        // Add default assemblies
        assemblies.AddRange(MefHostServices.DefaultAssemblies);
        System.Diagnostics.Debug.WriteLine($"[Completion] DefaultAssemblies count: {MefHostServices.DefaultAssemblies.Length}");
        
        // Explicitly load language-specific assemblies
        try
        {
            assemblies.Add(typeof(CompletionService).Assembly); // Microsoft.CodeAnalysis.Features
            assemblies.Add(typeof(CSharpCompilation).Assembly); // Microsoft.CodeAnalysis.CSharp
            assemblies.Add(typeof(Compilation).Assembly); // Microsoft.CodeAnalysis
            
            // Load CSharp.Features and CSharp.Workspaces explicitly
            assemblies.Add(Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features"));
            assemblies.Add(Assembly.Load("Microsoft.CodeAnalysis.CSharp.Workspaces"));
            assemblies.Add(Assembly.Load("Microsoft.CodeAnalysis.Workspaces"));
            
            System.Diagnostics.Debug.WriteLine($"[Completion] Total assemblies for MEF: {assemblies.Distinct().Count()}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Completion] Error loading assemblies: {ex.Message}");
        }
        
        var host = MefHostServices.Create(assemblies.Distinct().ToArray());
        workspace = new AdhocWorkspace(host);
        
        System.Diagnostics.Debug.WriteLine($"[Completion] Workspace language services count: {workspace.Services.SupportedLanguages.Count()}");

        // Create project
        projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name: "ScratchpadProject",
            assemblyName: "ScratchpadAssembly",
            language: LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: false),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: MetadataReferenceProvider.GetDefaultReferences());

        workspace.AddProject(projectInfo);

        // Create initial document
        documentId = DocumentId.CreateNewId(projectId);
        var documentInfo = DocumentInfo.Create(
            documentId,
            name: "Script.cs",
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(""), VersionStamp.Create())));

        workspace.AddDocument(documentInfo);
        currentDocument = workspace.CurrentSolution.GetDocument(documentId)!;
    }

    public void UpdateReferences(Dictionary<string, string> nugetPackages)
    {
        // Get updated references including NuGet packages
        var references = MetadataReferenceProvider.GetReferencesWithPackages(nugetPackages);
        
        // Update project with new references
        var project = workspace.CurrentSolution.GetProject(projectId);
        if (project != null)
        {
            var updatedProject = project.WithMetadataReferences(references);
            workspace.TryApplyChanges(updatedProject.Solution);
        }
    }

    private void UpdateDocument(string code, List<string> usings)
    {
        // Build using directives from provided list
        var usingStatements = string.Join(Environment.NewLine, usings.Select(u => $"using {u};"));
        var fullCode = usingStatements + "\n\n" + code;
        var sourceText = SourceText.From(fullCode);
        
        // Apply to workspace first
        var solution = workspace.CurrentSolution.WithDocumentText(documentId, sourceText);
        if (workspace.TryApplyChanges(solution))
        {
            currentDocument = workspace.CurrentSolution.GetDocument(documentId)!;
        }
        else
        {
            // Fallback: update document directly
            currentDocument = currentDocument.WithText(sourceText);
        }
    }

    public async Task<ImmutableArray<CompletionItem>> GetCompletionsAsync(
        string code, 
        int position,
        List<string> usings,
        Dictionary<string, string> nugetPackages,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Update references if packages are provided
            if (nugetPackages?.Count > 0)
            {
                UpdateReferences(nugetPackages);
            }
            
            // Update document with current code (which adds using directives)
            UpdateDocument(code, usings);

            // Ensure document is up to date
            var document = workspace.CurrentSolution.GetDocument(documentId);
            if (document == null)
            {
                System.Diagnostics.Debug.WriteLine("[Completion] Document is null");
                return ImmutableArray<CompletionItem>.Empty;
            }

            // Adjust position to account for added using directives
            var text = await document.GetTextAsync(cancellationToken);
            var lines = text.Lines;
            var adjustedPosition = position;
            
            // Calculate offset: number of using lines + 2 blank lines
            var usingLinesCount = usings.Count;
            if (lines.Count > usingLinesCount)
            {
                adjustedPosition = position + lines[usingLinesCount + 1].Start;
            }

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

    public void Dispose()
    {
        workspace?.Dispose();
    }
}
