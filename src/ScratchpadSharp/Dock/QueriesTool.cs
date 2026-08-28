using Dock.Model.ReactiveUI.Controls;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Dock;

public sealed class QueriesTool : Tool
{
    public QueriesTool(QueriesSidebarViewModel sidebar)
    {
        Sidebar = sidebar;
        Id = "Queries";
        Title = "Queries";
        CanClose = false;
        CanPin = true;
    }

    public QueriesSidebarViewModel Sidebar { get; }
}
