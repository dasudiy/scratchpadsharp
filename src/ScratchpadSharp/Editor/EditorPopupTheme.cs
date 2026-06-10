using Avalonia.Media;

namespace ScratchpadSharp.Editor;

internal static class EditorPopupTheme
{
    public const double ListWidth = 420;
    public const double ListMaxHeight = 280;
    public const double SignatureWidth = 560;
    public const double SignatureMaxHeight = 240;
    public const double DescriptionMaxWidth = 300;
    public const double PopupGap = 2;
    public const double ItemFontSize = 12;
    public const double MetaFontSize = 11;
    public const double CodeFontSize = 11;

    public static readonly FontFamily CodeFont = new("Cascadia Code, Consolas, monospace");

    public static readonly IBrush TextPrimary = new SolidColorBrush(Color.Parse("#1B1F23"));
    public static readonly IBrush TextSecondary = new SolidColorBrush(Color.Parse("#586069"));
    public static readonly IBrush TextMuted = new SolidColorBrush(Color.Parse("#8B929A"));
    public static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#2F81F7"));
    public static readonly IBrush AccentSubtle = new SolidColorBrush(Color.Parse("#DDF4FF"));
    public static readonly IBrush Border = new SolidColorBrush(Color.Parse("#DFE2E6"));
    public static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#D4920B"));
}
