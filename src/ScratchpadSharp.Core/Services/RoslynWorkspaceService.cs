using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Completion;
using ScratchpadSharp.Shared.Models; // Updated to Shared
using ScratchpadSharp.Core.PackageManagement; // Added

namespace ScratchpadSharp.Core.Services;

public class RoslynWorkspaceService
{
    private static readonly Lazy<RoslynWorkspaceService> LazyInstance = new(() => new RoslynWorkspaceService());
    public static RoslynWorkspaceService Instance => LazyInstance.Value;

    private AdhocWorkspace? workspace;
    private readonly ConcurrentDictionary<string, RoslynSession> _sessions = new();
    private readonly SemaphoreSlim semaphore = new(1, 1);
    private readonly object initLock = new();
    private Task? initializationTask;
    private bool isInitialized;

    private RoslynWorkspaceService()
    {
    }

    public Task EnsureInitializedAsync()
    {
        lock (initLock)
        {
            return initializationTask ??= InitializeCoreAsync();
        }
    }

    public Task InitializeAsync() => EnsureInitializedAsync();

    private async Task InitializeCoreAsync()
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

            System.Diagnostics.Debug.WriteLine(
                $"[RoslynWorkspace] Loaded {assemblies.Distinct().Count()} MEF assemblies");
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

        if (_sessions.ContainsKey(tabId))
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

        var session = new RoslynSession(tabId, projectId, documentId);
        _sessions.TryAdd(tabId, session);

        System.Diagnostics.Debug.WriteLine($"[RoslynWorkspace] Created project for tab '{tabId}'");
    }

    public void RemoveProject(string tabId)
    {
        if (!_sessions.TryRemove(tabId, out var session))
            return;

        if (workspace != null)
        {
            var solution = workspace.CurrentSolution.RemoveProject(session.ProjectId);
            workspace.TryApplyChanges(solution);
        }

        System.Diagnostics.Debug.WriteLine($"[RoslynWorkspace] Removed project for tab '{tabId}'");
    }

    public Document GetDocument(string tabId)
    {
        if (!isInitialized || workspace == null)
            throw new InvalidOperationException("Workspace not initialized.");

        if (!_sessions.TryGetValue(tabId, out var session))
            throw new ArgumentException($"No session found for tab '{tabId}'");

        var document = workspace.CurrentSolution.GetDocument(session.DocumentId);
        if (document == null)
            throw new InvalidOperationException($"Document for tab '{tabId}' not found in solution.");

        return document;
    }

    public async Task UpdateDocumentAsync(string tabId, string code, List<string> usings)
    {
        await semaphore.WaitAsync();
        try
        {
            if (!_sessions.TryGetValue(tabId, out var session))
                return;

            // Check loop optimization
            if (string.Equals(session.LastCode, code, StringComparison.Ordinal) &&
                AreUsingsEqual(session.LastUsings, usings))
            {
                return;
            }

            var document = GetDocument(tabId);

            var scriptDocument = ScriptDocumentBuilder.Build(code, usings);
            var sourceText = SourceText.From(scriptDocument.FullText);
            var newDocument = document.WithText(sourceText);
            var newSolution = newDocument.Project.Solution;

            if (workspace!.TryApplyChanges(newSolution))
            {
                session.LastCode = code;
                session.LastUsings = new List<string>(usings);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[RoslynWorkspace] Failed to apply changes for tab '{tabId}'");
            }
        }
        finally
        {
            semaphore.Release();
        }
    }


    public async Task UpdateReferencesFromManifestAsync(string tabId, PackageManifest manifest, string projectRoot)
    {
        await semaphore.WaitAsync();
        try
        {
            if (!_sessions.TryGetValue(tabId, out var session)) return;

            var project = workspace!.CurrentSolution.GetProject(session.ProjectId);
            if (project == null) return;

            var references = new List<MetadataReference>();
            references.AddRange(MetadataReferenceProvider.GetDefaultReferences());

            // Get Global Packages Folder
            var settings = NuGet.Configuration.Settings.LoadDefaultSettings(null);
            var globalCache = NuGet.Configuration.SettingsUtility.GetGlobalPackagesFolder(settings);

            foreach (var assembly in manifest.ResolvedState.Assemblies)
            {
                var physicalPath = Storage.PackageService.GetPhysicalPath(assembly, projectRoot, globalCache);

                if (System.IO.File.Exists(physicalPath))
                {
                    references.Add(MetadataReference.CreateFromFile(physicalPath));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[RoslynWorkspace] Manifest reference not found: {physicalPath}");
                }
            }

            var updatedProject = project.WithMetadataReferences(references);

            var compilationOptions = ((CSharpCompilationOptions)project.CompilationOptions!)
                .WithAllowUnsafe(manifest.Compilation.AllowUnsafe);

            if (Enum.TryParse<NullableContextOptions>(manifest.Compilation.Nullable, true, out var nullableOptions))
            {
                compilationOptions = compilationOptions.WithNullableContextOptions(nullableOptions);
            }

            updatedProject = updatedProject.WithCompilationOptions(compilationOptions);

            if (workspace.TryApplyChanges(updatedProject.Solution))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[RoslynWorkspace] Updated references from Manifest for tab '{tabId}'");
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<PackageManifest> ArbitrateManifestAsync(string tabId, ScriptConfig config,
        ResolvedEnvironment env, string projectRoot, PackageManifest? existingManifest = null)
    {
        // 1. Create/Update Manifest Model
        var manifest = existingManifest ?? new PackageManifest();

        // Ensure core project settings are set if new or missing
        if (string.IsNullOrEmpty(manifest.Project.EntryPoint)) manifest.Project.EntryPoint = "script.cs";
        if (string.IsNullOrEmpty(manifest.Project.TargetFramework)) manifest.Project.TargetFramework = "net8.0";

        // Update Resolves State (Package Version Locking)
        // Note: env (ResolvedEnvironment) has List<ResolvedAsset> Assemblies and Dict<string, List<ResolvedAsset>> NativeAssets
        // PackageManifest has similar structure now.
        manifest.ResolvedState.Assemblies = env.Assemblies;
        manifest.ResolvedState.NativeAssets = env.NativeAssets;

        await UpdateReferencesFromManifestAsync(tabId, manifest, projectRoot);

        return manifest;
    }

    public async Task UpdateReferencesAsync(string tabId, List<string>? nugetPackages)
    {
        await semaphore.WaitAsync();
        try
        {
            if (!_sessions.TryGetValue(tabId, out var session))
                return;

            // Check if packages are the same
            if (ArePackagesEqual(session.LastAppliedPackages, nugetPackages))
            {
                return;
            }

            var project = workspace!.CurrentSolution.GetProject(session.ProjectId);
            if (project == null)
                return;

            var references = MetadataReferenceProvider.GetReferencesWithPackages(nugetPackages);
            var updatedProject = project.WithMetadataReferences(references);

            if (workspace.TryApplyChanges(updatedProject.Solution))
            {
                session.LastAppliedPackages = nugetPackages != null
                    ? [..nugetPackages]
                    : [];
                System.Diagnostics.Debug.WriteLine($"[RoslynWorkspace] Updated references for tab '{tabId}'");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[RoslynWorkspace] Failed to update references for tab '{tabId}'");
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private bool AreUsingsEqual(List<string>? u1, List<string>? u2)
    {
        if (u1 == u2) return true;
        if (u1 == null || u2 == null) return false;
        return u1.SequenceEqual(u2);
    }

    private bool ArePackagesEqual(List<string>? p1, List<string>? p2)
    {
        if (p1 == p2) return true;
        if (p1 == null || p2 == null) return false;
        if (p1.Count != p2.Count) return false;

        var set = new HashSet<string>(p2, StringComparer.OrdinalIgnoreCase);
        return p1.All(set.Contains);
    }

    public int CalculateAdjustedPosition(int editorPosition, string editorCode, List<string> configUsings)
    {
        var doc = ScriptDocumentBuilder.Build(editorCode, configUsings);
        return ScriptDocumentBuilder.ToDocumentPosition(doc, editorPosition);
    }

    public ScriptDocumentBuilder.ScriptDocument BuildScriptDocument(string editorCode, List<string> configUsings)
    {
        return ScriptDocumentBuilder.Build(editorCode, configUsings);
    }

    public bool IsInitialized => isInitialized;
}