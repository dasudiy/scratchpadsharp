using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class EditorTabHeader : UserControl
{
    public static readonly StyledProperty<bool> IsCloseVisibleProperty =
        AvaloniaProperty.Register<EditorTabHeader, bool>(nameof(IsCloseVisible));

    private bool isHovered;
    private ScriptTabViewModel? boundTab;

    public bool IsCloseVisible
    {
        get => GetValue(IsCloseVisibleProperty);
        set => SetValue(IsCloseVisibleProperty, value);
    }

    public EditorTabHeader()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeTab();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnsubscribeTab();
        boundTab = DataContext as ScriptTabViewModel;
        if (boundTab != null)
            boundTab.PropertyChanged += OnTabPropertyChanged;
        UpdateCloseVisibility();
    }

    private void UnsubscribeTab()
    {
        if (boundTab != null)
        {
            boundTab.PropertyChanged -= OnTabPropertyChanged;
            boundTab = null;
        }
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptTabViewModel.IsSelected))
            UpdateCloseVisibility();
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        isHovered = true;
        UpdateCloseVisibility();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        isHovered = false;
        UpdateCloseVisibility();
    }

    private void UpdateCloseVisibility() =>
        IsCloseVisible = isHovered || boundTab?.IsSelected == true;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.Source is Button)
            return;

        if (DataContext is not ScriptTabViewModel tab)
            return;

        var window = this.FindAncestorOfType<Window>();
        if (window?.DataContext is MainWindowViewModel vm)
            vm.SelectedTab = tab;
    }
}
