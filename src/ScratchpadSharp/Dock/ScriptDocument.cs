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
        Title = tab.Title;
        CanClose = true;

        tab.PropertyChanged += OnTabPropertyChanged;
    }

    public ScriptTabViewModel Tab { get; }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptTabViewModel.Title))
            Title = Tab.Title;
    }
}
