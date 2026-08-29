using System;
using System.ComponentModel;
using Dock.Model.ReactiveUI.Controls;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Dock;

public sealed class ScriptDocument : Document
{
    public ScriptDocument(ScriptTabViewModel tab)
    {
        Tab = tab;
        Id = tab.TabId;
        Title = FormatTitle(tab);
        CanClose = true;

        tab.PropertyChanged += OnTabPropertyChanged;
    }

    public ScriptTabViewModel Tab { get; }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScriptTabViewModel.Title) or nameof(ScriptTabViewModel.IsDirty))
            Title = FormatTitle(Tab);
    }

    private static string FormatTitle(ScriptTabViewModel tab) =>
        tab.IsDirty ? $"{tab.Title}*" : tab.Title;
}
