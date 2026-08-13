using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ScratchpadSharp.Core.Modules;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class DatabaseConnectionWindow : Window
{
    public DatabaseConnectionWindow()
    {
        InitializeComponent();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is DatabaseConnectionViewModel vm)
            vm.StorageProvider = StorageProvider;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DatabaseConnectionViewModel vm)
        {
            vm.SaveCommand.Execute(System.Reactive.Unit.Default);
            if (vm.WasSaved)
                Close();
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ConnectionStringFieldDescriptor field } ||
            DataContext is not DatabaseConnectionViewModel vm)
            return;

        await vm.BrowseDatabaseFileAsync(field);
    }

    private async void OnBrowsePrivateKeyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DatabaseConnectionViewModel vm)
            return;

        await vm.BrowsePrivateKeyAsync();
    }

    private void OnFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: ConnectionStringFieldDescriptor field } ||
            DataContext is not DatabaseConnectionViewModel vm)
            return;

        vm.OnFieldChanged(field);
    }

    private void OnFieldCheckChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: ConnectionStringFieldDescriptor field } ||
            DataContext is not DatabaseConnectionViewModel vm)
            return;

        field.Value = ((CheckBox)sender).IsChecked;
        vm.OnFieldChanged(field);
    }

    private void OnFieldSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: ConnectionStringFieldDescriptor field, SelectedItem: not null } ||
            DataContext is not DatabaseConnectionViewModel vm)
            return;

        field.Value = ((ComboBox)sender).SelectedItem?.ToString();
        vm.OnFieldChanged(field);
    }
}
