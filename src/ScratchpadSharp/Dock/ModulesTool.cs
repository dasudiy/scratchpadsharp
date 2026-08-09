using Dock.Model.ReactiveUI.Controls;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Dock;

public sealed class ModulesTool : Tool
{
    public ModulesTool(ModulesSidebarViewModel sidebar)
    {
        Sidebar = sidebar;
        Id = "Modules";
        Title = "Modules";
        CanClose = false;
        CanPin = true;
    }

    public ModulesSidebarViewModel Sidebar { get; }
}
