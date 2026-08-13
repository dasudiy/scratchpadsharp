using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ConfirmViewModel { WasConfirmed: true })
            Close();
    }

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
