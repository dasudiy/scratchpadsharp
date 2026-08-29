using System;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is ConfirmViewModel { ShowInput: true })
        {
            InputBox.Focus();
            InputBox.SelectAll();
            return;
        }

        OkButton.Focus();
    }

    private void ConfirmAndClose()
    {
        if (DataContext is not ConfirmViewModel vm)
            return;

        vm.ConfirmCommand.Execute(Unit.Default);
        if (vm.WasConfirmed)
            Close();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConfirmAndClose();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        ConfirmAndClose();
        e.Handled = true;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => ConfirmAndClose();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    public static async Task<bool> ConfirmAsync(Window? owner, string title, string prompt)
    {
        if (owner == null)
            return false;

        var vm = new ConfirmViewModel(title, prompt);
        var window = new ConfirmWindow { DataContext = vm };
        await window.ShowDialog(owner);
        return vm.WasConfirmed;
    }

    public static async Task<string?> PromptAsync(Window? owner, string title, string prompt, string? defaultValue)
    {
        if (owner == null)
            return null;

        var vm = new ConfirmViewModel(title, prompt, defaultValue, showInput: true);
        var window = new ConfirmWindow { DataContext = vm };
        await window.ShowDialog(owner);
        return vm.WasConfirmed ? vm.InputText.Trim() : null;
    }
}
