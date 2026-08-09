using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.ReactiveUI;
using Dock.Model.ReactiveUI.Controls;
using Dock.Settings;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Dock;

public sealed class ScratchpadDockFactory : Factory
{
    private readonly Func<ScriptTabViewModel> createTab;
    private readonly Action<ScriptTabViewModel, ScriptDocument> onDocumentCreated;

    private IDocumentDock? documentDock;
    private IRootDock? rootDock;
    private ModulesTool? modulesTool;

    public ScratchpadDockFactory(
        IScriptExecutionService scriptService,
        Func<ScriptTabViewModel?> getSelectedTab,
        Func<ScriptTabViewModel> createTab,
        Action<ScriptTabViewModel, ScriptDocument> onDocumentCreated)
    {
        this.createTab = createTab;
        this.onDocumentCreated = onDocumentCreated;
        ModulesSidebar = new ModulesSidebarViewModel(getSelectedTab, scriptService);
    }

    public ModulesSidebarViewModel ModulesSidebar { get; }

    public IDocumentDock? DocumentDock => documentDock;

    public IReadOnlyList<ScriptDocument> Documents =>
        documentDock?.VisibleDockables?.OfType<ScriptDocument>().ToList()
        ?? (IReadOnlyList<ScriptDocument>)Array.Empty<ScriptDocument>();

    public override IRootDock CreateLayout()
    {
        modulesTool = new ModulesTool(ModulesSidebar);

        var leftDock = new ToolDock
        {
            Id = "ModulesDock",
            Proportion = 0.22,
            Alignment = Alignment.Left,
            GripMode = GripMode.Visible,
            ActiveDockable = modulesTool,
            VisibleDockables = CreateList<IDockable>(modulesTool)
        };

        documentDock = new DocumentDock
        {
            Id = "Documents",
            IsCollapsable = false,
            CanCreateDocument = true,
            CanCloseLastDockable = false,
            EnableWindowDrag = true,
            DocumentFactory = CreateScriptDocument,
            ActiveDockable = null,
            VisibleDockables = CreateList<IDockable>()
        };

        var mainLayout = new ProportionalDock
        {
            Orientation = Orientation.Horizontal,
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(
                leftDock,
                new ProportionalDockSplitter(),
                documentDock)
        };

        rootDock = CreateRootDock();
        rootDock.IsCollapsable = false;
        rootDock.VisibleDockables = CreateList<IDockable>(mainLayout);
        rootDock.ActiveDockable = mainLayout;
        rootDock.DefaultDockable = mainLayout;
        rootDock.PinnedDock = null;

        return rootDock;
    }

    public ScriptDocument AddScriptDocument(ScriptTabViewModel tab)
    {
        if (documentDock is null)
            throw new InvalidOperationException("Document dock is not initialized.");

        var document = new ScriptDocument(tab);
        documentDock.AddDocument(document);
        onDocumentCreated(tab, document);
        return document;
    }

    public bool TryGetDocument(ScriptTabViewModel tab, out ScriptDocument document)
    {
        document = Documents.FirstOrDefault(doc => doc.Tab == tab)!;
        return document is not null;
    }

    public void ActivateScriptDocument(ScriptTabViewModel tab)
    {
        if (!TryGetDocument(tab, out var document) || documentDock is null)
            return;

        SetActiveDockable(document);
        SetFocusedDockable(documentDock, document);
    }

    public ScriptTabViewModel? GetActiveScriptTab() =>
        documentDock?.ActiveDockable is ScriptDocument document ? document.Tab : null;

    public override void InitLayout(IDockable layout)
    {
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => DockSettings.UseManagedWindows ? new ManagedHostWindow() : new HostWindow()
        };

        base.InitLayout(layout);
    }

    private IDockable CreateScriptDocument()
    {
        var tab = createTab();
        var document = new ScriptDocument(tab);
        onDocumentCreated(tab, document);
        return document;
    }
}
