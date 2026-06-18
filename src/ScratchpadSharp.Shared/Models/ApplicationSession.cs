using System.Collections.Generic;

namespace ScratchpadSharp.Shared.Models;

public class ApplicationSession
{
    public int SelectedTabIndex { get; set; }

    public List<TabSessionState> Tabs { get; set; } = [];
}

public class TabSessionState
{
    public string? SourcePath { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = "Untitled";

    public ScriptConfig? Config { get; set; }

    public PackageManifest? Manifest { get; set; }
}
