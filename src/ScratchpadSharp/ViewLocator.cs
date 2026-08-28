using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ScratchpadSharp.Dock;
using ScratchpadSharp.Views.Dock;

namespace ScratchpadSharp;

public sealed class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> Views = new()
    {
        [typeof(ScriptDocument)] = () => new ScriptDocumentView(),
        [typeof(ModulesTool)] = () => new ModulesToolView(),
        [typeof(QueriesTool)] = () => new QueriesToolView()
    };

    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        if (Views.TryGetValue(data.GetType(), out var factory))
            return factory();

        return null;
    }

    public bool Match(object? data) =>
        data is not null && Views.ContainsKey(data.GetType());
}
