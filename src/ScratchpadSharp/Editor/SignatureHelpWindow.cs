using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using ScratchpadSharp.ViewModels;
using ScratchpadSharp.Views;

namespace ScratchpadSharp.Editor;

/// <summary>
/// Signature Help Popup, based on CompletionWindowBase but positioned above the caret.
/// </summary>
public class SignatureHelpWindow : Popup, IStyleable
{
    private readonly SignatureHelpPopup _popupContent;
    public SignatureHelpViewModel ViewModel { get; }

    Type IStyleable.StyleKey => typeof(PopupRoot);

    /// <summary>
    /// Gets the parent TextArea.
    /// </summary>
    public TextArea TextArea { get; }

    private readonly Window _parentWindow;
    private TextDocument _document;

    /// <summary>
    /// Gets/Sets the start of the text range in which the completion window stays open.
    /// This text portion is used to determine the text used to select an entry in the completion list by typing.
    /// </summary>
    public int StartOffset { get; set; }

    /// <summary>
    /// Gets/Sets the end of the text range in which the completion window stays open.
    /// This text portion is used to determine the text used to select an entry in the completion list by typing.
    /// </summary>
    public int EndOffset { get; set; }

    /// <summary>
    /// Gets whether the window was opened above the current line.
    /// </summary>
    protected bool IsUp { get; private set; }



    public SignatureHelpWindow(TextArea textArea) : base()
    {
        ViewModel = new SignatureHelpViewModel();
        _popupContent = new SignatureHelpPopup { DataContext = ViewModel };
        Child = _popupContent;


        TextArea = textArea ?? throw new ArgumentNullException(nameof(textArea));
        _parentWindow = textArea.GetVisualRoot() as Window;


        AddHandler(PointerReleasedEvent, OnMouseUp, handledEventsToo: true);

        StartOffset = EndOffset = TextArea.Caret.Offset;

        PlacementTarget = TextArea.TextView;
        Placement = PlacementMode.AnchorAndGravity;
        PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft;
        PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight;
        // PlacementConstraintAdjustment =
        //     PopupPositionerConstraintAdjustment.FlipY |
        //     PopupPositionerConstraintAdjustment.SlideY;
        //Deactivated += OnDeactivated; //Not needed?

        Closed += (sender, args) => DetachEvents();

        AttachEvents();

        Initailize();
    }

    protected virtual void OnClosed()
    {
        DetachEvents();
    }

    private void Initailize()
    {
        if (_document != null && StartOffset != TextArea.Caret.Offset)
        {
            SetPosition(new TextViewPosition(_document.GetLocation(StartOffset)));
        }
        else
        {
            SetPosition(TextArea.Caret.Position);
        }
    }

    public void Show()
    {
        UpdatePosition();

        Open();
        Height = double.NaN;
        MinHeight = 0;
    }

    public void Hide()
    {
        Close();
        OnClosed();
    }

    #region Event Handlers

    private void AttachEvents()
    {
        ((ISetLogicalParent)this).SetParent(TextArea.GetVisualRoot() as ILogical);

        _document = TextArea.Document;
        if (_document != null)
        {
            _document.Changing += TextArea_Document_Changing;
        }

        // LostKeyboardFocus seems to be more reliable than PreviewLostKeyboardFocus - see SD-1729
        TextArea.LostFocus += TextAreaLostFocus;
        TextArea.TextView.ScrollOffsetChanged += TextViewScrollOffsetChanged;
        TextArea.DocumentChanged += TextAreaDocumentChanged;
        if (_parentWindow != null)
        {
            _parentWindow.PositionChanged += ParentWindow_LocationChanged;
            _parentWindow.Deactivated += ParentWindow_Deactivated;
        }

        // close previous completion windows of same type
        foreach (var x in TextArea.StackedInputHandlers.OfType<InputHandler>())
        {
            if (x.Window.GetType() == GetType())
                TextArea.PopStackedInputHandler(x);
        }

        _myInputHandler = new InputHandler(this);
        TextArea.PushStackedInputHandler(_myInputHandler);
    }

    /// <summary>
    /// Detaches events from the text area.
    /// </summary>
    protected virtual void DetachEvents()
    {
        ((ISetLogicalParent)this).SetParent(null);

        if (_document != null)
        {
            _document.Changing -= TextArea_Document_Changing;
        }
        TextArea.LostFocus -= TextAreaLostFocus;
        TextArea.TextView.ScrollOffsetChanged -= TextViewScrollOffsetChanged;
        TextArea.DocumentChanged -= TextAreaDocumentChanged;
        if (_parentWindow != null)
        {
            _parentWindow.PositionChanged -= ParentWindow_LocationChanged;
            _parentWindow.Deactivated -= ParentWindow_Deactivated;
        }
        TextArea.PopStackedInputHandler(_myInputHandler);
    }

    #region InputHandler

    private InputHandler _myInputHandler;

    /// <summary>
    /// A dummy input handler (that justs invokes the default input handler).
    /// This is used to ensure the completion window closes when any other input handler
    /// becomes active.
    /// </summary>
    private sealed class InputHandler : TextAreaStackedInputHandler
    {
        internal readonly SignatureHelpWindow Window;

        public InputHandler(SignatureHelpWindow window)
            : base(window.TextArea)
        {
            Debug.Assert(window != null);
            Window = window;
        }

        public override void Detach()
        {
            base.Detach();
            Window.Hide();
        }

        public override void OnPreviewKeyDown(KeyEventArgs e)
        {
            // prevents crash when typing deadchar while CC window is open
            if (e.Key == Key.DeadCharProcessed)
                return;
            e.Handled = RaiseEventPair(Window, null, KeyDownEvent,
                                       new KeyEventArgs { Key = e.Key });
        }

        public override void OnPreviewKeyUp(KeyEventArgs e)
        {
            if (e.Key == Key.DeadCharProcessed)
                return;
            e.Handled = RaiseEventPair(Window, null, KeyUpEvent,
                new KeyEventArgs { Key = e.Key });
        }
    }
    #endregion

    private void TextViewScrollOffsetChanged(object sender, EventArgs e)
    {
        ILogicalScrollable textView = TextArea;
        var visibleRect = new Rect(textView.Offset.X, textView.Offset.Y, textView.Viewport.Width, textView.Viewport.Height);
        //close completion window when the user scrolls so far that the anchor position is leaving the visible area
        if (visibleRect.Contains(_visualLocation) || visibleRect.Contains(_visualLocationTop))
        {
            UpdatePosition();
        }
        else
        {
            Hide();
        }
    }

    private void TextAreaDocumentChanged(object sender, EventArgs e)
    {
        Hide();
    }

    private void TextAreaLostFocus(object sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(CloseIfFocusLost, DispatcherPriority.Background);
    }

    private void ParentWindow_Deactivated(object sender, EventArgs e)
    {
        Hide();
    }

    private void ParentWindow_LocationChanged(object sender, EventArgs e)
    {
        UpdatePosition();
    }

    /// <inheritdoc/>
    private void OnDeactivated(object sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(CloseIfFocusLost, DispatcherPriority.Background);
    }

    #endregion

    /// <summary>
    /// Raises a tunnel/bubble event pair for a control.
    /// </summary>
    /// <param name="target">The control for which the event should be raised.</param>
    /// <param name="previewEvent">The tunneling event.</param>
    /// <param name="event">The bubbling event.</param>
    /// <param name="args">The event args to use.</param>
    /// <returns>The <see cref="RoutedEventArgs.Handled"/> value of the event args.</returns>
    [SuppressMessage("Microsoft.Design", "CA1030:UseEventsWhereAppropriate")]
    protected static bool RaiseEventPair(Control target, RoutedEvent previewEvent, RoutedEvent @event, RoutedEventArgs args)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (args == null)
            throw new ArgumentNullException(nameof(args));
        if (previewEvent != null)
        {
            args.RoutedEvent = previewEvent;
            target.RaiseEvent(args);
        }
        args.RoutedEvent = @event ?? throw new ArgumentNullException(nameof(@event));
        target.RaiseEvent(args);
        return args.Handled;
    }

    // Special handler: handledEventsToo
    private void OnMouseUp(object sender, PointerReleasedEventArgs e)
    {
        ActivateParentWindow();
    }

    /// <summary>
    /// Activates the parent window.
    /// </summary>
    protected virtual void ActivateParentWindow()
    {
        _parentWindow?.Activate();
    }

    private void CloseIfFocusLost()
    {
        if (CloseOnFocusLost)
        {
            Debug.WriteLine("CloseIfFocusLost: this.IsFocues=" + IsFocused + " IsTextAreaFocused=" + IsTextAreaFocused);
            if (!IsFocused && !IsTextAreaFocused)
            {
                Hide();
            }
        }
    }

    /// <summary>
    /// Gets whether the completion window should automatically close when the text editor looses focus.
    /// </summary>
    protected virtual bool CloseOnFocusLost => true;

    private bool IsTextAreaFocused
    {
        get
        {
            if (_parentWindow != null && !_parentWindow.IsActive)
                return false;
            return TextArea.IsFocused;
        }
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            e.Handled = true;
            Hide();
        }
    }

    private Point _visualLocation;
    private Point _visualLocationTop;

    /// <summary>
    /// Positions the completion window at the specified position.
    /// </summary>
    protected void SetPosition(TextViewPosition position)
    {
        var textView = TextArea.TextView;

        _visualLocation = textView.GetVisualPosition(position, VisualYPosition.LineBottom);
        _visualLocationTop = textView.GetVisualPosition(position, VisualYPosition.LineTop);

        UpdatePosition();
    }

    /// <summary>
    /// Updates the position of the CompletionWindow based on the parent TextView position and the screen working area.
    /// It ensures that the CompletionWindow is completely visible on the screen.
    /// </summary>
    protected void UpdatePosition()
    {
        var textView = TextArea.TextView;

        var position = _visualLocation - textView.ScrollOffset;

        this.HorizontalOffset = position.X;
        var rect = TextArea.Caret.CalculateCaretRectangle();
        //TODO don't know how to set it properly, it should be above the caret, hardcoded for now
        this.VerticalOffset = position.Y - rect.Height - 200;
    }

    // TODO: check if needed
    ///// <inheritdoc/>
    //protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    //{
    //    base.OnRenderSizeChanged(sizeInfo);
    //    if (sizeInfo.HeightChanged && IsUp)
    //    {
    //        this.Top += sizeInfo.PreviousSize.Height - sizeInfo.NewSize.Height;
    //    }
    //}

    /// <summary>
    /// Gets/sets whether the completion window should expect text insertion at the start offset,
    /// which not go into the completion region, but before it.
    /// </summary>
    /// <remarks>This property allows only a single insertion, it is reset to false
    /// when that insertion has occurred.</remarks>
    public bool ExpectInsertionBeforeStart { get; set; }

    private void TextArea_Document_Changing(object sender, DocumentChangeEventArgs e)
    {
        if (e.Offset + e.RemovalLength == StartOffset && e.RemovalLength > 0)
        {
            Hide(); // removal immediately in front of completion segment: close the window
                    // this is necessary when pressing backspace after dot-completion
        }
        if (e.Offset == StartOffset && e.RemovalLength == 0 && ExpectInsertionBeforeStart)
        {
            StartOffset = e.GetNewOffset(StartOffset, AnchorMovementType.AfterInsertion);
            ExpectInsertionBeforeStart = false;
        }
        else
        {
            StartOffset = e.GetNewOffset(StartOffset, AnchorMovementType.BeforeInsertion);
        }
        EndOffset = e.GetNewOffset(EndOffset, AnchorMovementType.AfterInsertion);
    }
}
