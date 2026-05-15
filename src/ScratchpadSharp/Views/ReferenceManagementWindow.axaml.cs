using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ScratchpadSharp.ViewModels;
using System;

namespace ScratchpadSharp.Views;

public partial class ReferenceManagementWindow : Window
{
    public ReferenceManagementWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnAddReferenceClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ReferenceManagementViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Assembly",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Assemblies") { Patterns = new[] { "*.dll" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        });

        foreach (var file in files)
        {
            vm.AddReferenceFromFile(file.Path.LocalPath);
        }
    }
}
