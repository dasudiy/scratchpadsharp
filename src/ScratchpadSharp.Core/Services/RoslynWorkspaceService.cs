using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Completion;

namespace ScratchpadSharp.Core.Services;

public class RoslynWorkspaceService
{
    private static readonly Lazy<RoslynWorkspaceService> instance = new(() => new RoslynWorkspaceService());
    public static RoslynWorkspaceService Instance => instance.Value;

    private AdhocWorkspace? workspace;
    private readonly Dictionary<string, ProjectId> tabMappings = new();
    private readonly SemaphoreSlim semaphore = new(1, 1);
    private bool isInitialized;

    private RoslynWorkspaceService() { }

    public async Task InitializeAsync()
    {
        if (isInitialized)
            return;


        System.Diagnostics.Debug.WriteLine("[RoslynWorkspace] Initializing workspace...");

        var assemblies = new List<Assembly>();
        assemblies.AddRange(MefHostServices.DefaultAssemblies);

        try
        {
            assemblies.Add(typeof(CompletionService).Assembly);
            assemblies.Add(typeof(CSharpCompilation).Assembly);
            assemblies.Add(typeof(Compilation).Assembly);
            assemblies.Add(Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features"));
            assemblies.Add(Assembly.Load("Microsoft.CodeAnalysis.CSharp.Workspaces"));
            assemblies.Add(Assembly.Load("Microsoft.CodeAnalysis.Workspaces"));

            System.Diagnostics.Debug.WriteLine($"[RoslynWorkspace] Loaded {assemblies.Distinct().Count()} MEF assemblies");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RoslynWorkspace] Error loading assemblies: {ex.Message}");
        }

        var host = MefHostServices.Create(assemblies.Distinct().ToArray());
        workspace = new AdhocWorkspace(host);

        System.Diagnostics.Debug.WriteLine("[RoslynWorkspace] Workspace initialized");
        isInitialized = true;

        // Warm up Roslyn with a dummy completion request
        await WarmUpAsync().ConfigureAwait(false);
    }

    private async Task WarmUpAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[RoslynWorkspace] Warming up Roslyn...");

            var warmupProjectId = ProjectId.CreateNewId("warmup");
            var projectInfo = ProjectInfo.Create(
                warmupProjectId,
                VersionStamp.Create(),
                name: "WarmupProject",
                assemblyName: "WarmupAssembly",
                language: LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
                metadataReferences: MetadataReferenceProvider.GetDefaultReferences());

            workspace!.AddProject(projectInfo);

            var documentId = DocumentId.CreateNewId(warmupProjectId);
            var documentInfo = DocumentInfo.Create(
                documentId,
                name: "Warmup.cs",
                loader: TextLoader.From(TextAndVersion.Create(
                    SourceText.From("System.Console."),
                    VersionStamp.Create())));

            workspace.AddDocument(documentInfo);

            var document = workspace.CurrentSolution.GetDocument(documentId);
            if (document != null)
            {
                var completionService = CompletionService.GetService(document);
                if (completionService != null)
                {
                    await completionService.GetCompletionsAsync(document, 15);
                }
            }

            // Remove warmup project by clearing the solution
            var solution = workspace.CurrentSolution.RemoveProject(warmupProjectId);
            workspace.TryApplyChanges(solution);

            System.Diagnostics.Debug.WriteLine("[RoslynWorkspace] Warmup complete");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RoslynWorkspace] Warmup error: {ex.Message}");
        }
    }

    public void CreateProject(string tabId)
    {
        if (!isInitialized || workspace == null)
            throw new InvalidOperationException("Workspace not initialized. Call InitializeAsync first.");

        if (tabMappings.ContainsKey(tabId))
            throw new InvalidOperationException($"Project for tab '{tabId}' already exists.");

        var projectId = ProjectId.CreateNewId(tabId);
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name: $"ScratchpadProject_{tabId}",
            assemblyName: $"ScratchpadAssembly_{tabId}",
            language: LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: false).WithXmlReferenceResolver(
                    XmlFileResolver.Default
                ),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: MetadataReferenceProvider.GetDefaultReferences());

        workspace.AddProject(projectInfo);

        var documentId = DocumentId.CreateNewId(projectId);
        var documentInfo = DocumentInfo.Create(
            documentId,
            name: "Script.cs",
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(""), VersionStamp.Create())));

        workspace.AddDocument(documentInfo);

        tabMappings[tabId] = projectId;

        System.Diagnostics.Debug.WriteLine($"[RoslynWorkspace] Created project for tab '{tabId}'");
    }

    public void RemoveProject(string tabId)
    {
        if (!tabMappings.TryGetValue(tabId, out var projectId))
            return;

        if (workspace != null)
        {
            var solution = workspace.CurrentSolution.RemoveProject(projectId);
            workspace.TryApplyChanges(solution);
        }

        tabMappings.Remove(tabId);

        System.Diagnostics.Debug.WriteLine($"[RoslynWorkspace] Removed project for tab '{tabId}'");
    }

    public Document GetDocument(string tabId)
    {
        if (!isInitialized || workspace == null)
            throw new InvalidOperationException("Workspace not initialized.");

        if (!tabMappings.TryGetValue(tabId, out var projectId))
            throw new ArgumentException($"No project found for tab '{tabId}'");

        var project = workspace.CurrentSolution.GetProject(projectId);
        if (project == null)
            throw new InvalidOperationException($"Project for tab '{tabId}' not found in solution.");

        var document = project.Documents.FirstOrDefault();
        if (document == null)
            throw new InvalidOperationException($"No document found in project for tab '{tabId}'");

        return document;
    }

    public async Task UpdateDocumentAsync(string tabId, string code, List<string> usings)
    {
        await semaphore.WaitAsync();
        try
        {
            var document = GetDocument(tabId);

            var usingStatements = string.Join(Environment.NewLine, usings.Select(u => $"using {u};"));
            var fullCode = usingStatements + (usingStatements.Length > 0 ? "\n\n" : "") + code;

            var sourceText = SourceText.From(fullCode);
            var newDocument = document.WithText(sourceText);
            var newSolution = newDocument.Project.Solution;

            if (!workspace!.TryApplyChanges(newSolution))
            {
                System.Diagnostics.Debug.WriteLine($"[RoslynWorkspace] Failed to apply changes for tab '{tabId}'");
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task UpdateReferencesAsync(string tabId, Dictionary<string, string> nugetPackages)
    {
        await semaphore.WaitAsync();
        try
        {
            if (!tabMappings.TryGetValue(tabId, out var projectId))
                return;

            var project = workspace!.CurrentSolution.GetProject(projectId);
            if (project == null)
                return;

            var references = MetadataReferenceProvider.GetReferencesWithPackages(nugetPackages);
            var updatedProject = project.WithMetadataReferences(references);

            if (!workspace.TryApplyChanges(updatedProject.Solution))
            {
                System.Diagnostics.Debug.WriteLine($"[RoslynWorkspace] Failed to update references for tab '{tabId}'");
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    public int CalculateAdjustedPosition(int position, List<string> usings)
    {
        return position + GetUsingsOffset(usings);
    }

    public int GetUsingsOffset(List<string> usings)
    {
        if (usings == null || usings.Count == 0)
            return 0;

        var usingStatements = string.Join(Environment.NewLine, usings.Select(u => $"using {u};"));
        // +2 for the blank lines that UpdateDocumentAsync adds:
        // var fullCode = usingStatements + (usingStatements.Length > 0 ? "\n\n" : "") + code;
        return usingStatements.Length + (usingStatements.Length > 0 ? 2 : 0);
    }

    public bool IsInitialized => isInitialized;
}
