using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace Facet.Theme;

/// <summary>
/// Facet's hover tooltips: centred on the element they describe (above it, or below when there's no room
/// above — e.g. a dock at the top of the screen), and laid out as a bold headline, details, then the
/// click / scroll hints in a quieter colour.
/// </summary>
internal static class ToolTips
{
    private const double Gap = 6; // between the element and the tooltip's card (its shadow margin adds the rest)

    /// <summary>Placement for every ToolTip (set by the ToolTip style): centred above, else centred below.</summary>
    public static readonly CustomPopupPlacementCallback Centered = Place;

    // sizes and offsets arrive in physical pixels
    private static CustomPopupPlacement[] Place(Size popup, Size target, Point offset)
    {
        var gap = Gap * Scale();
        var x = (target.Width - popup.Width) / 2;
        return
        [
            new CustomPopupPlacement(new Point(x, -popup.Height - gap), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(x, target.Height + gap), PopupPrimaryAxis.Horizontal),
        ];
    }

    private static double Scale() =>
        Shell.DockWindow.Instance is { } dock && PresentationSource.FromVisual(dock)?.CompositionTarget is { } target
            ? target.TransformToDevice.M11
            : 1.0;
}

/// <summary>
/// Plain-text tooltips become a small layout. Facet's tooltips read "Headline · detail · click to …", with
/// more lines after a line break: the first piece is the headline, the other pieces and lines are details,
/// and pieces that say what clicking / scrolling does are hints at the bottom. Anything else shows as is.
/// </summary>
internal sealed class ToolTipFormatter : IValueConverter
{
    private static readonly string[] HintStarts = ["click", "right-click", "middle-click", "scroll", "drag", "double-click"];
    private static readonly string[] Separators = [" · ", " — "];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text)) return value;

        var details = new List<string>();
        var hints = new List<string>();
        string? headline = null;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = line.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var lineDetails = new List<string>();
            foreach (var piece in pieces)
            {
                if (IsHint(piece)) hints.Add(Capitalise(piece));
                else if (headline is null) headline = piece;
                else lineDetails.Add(piece);
            }
            if (lineDetails.Count > 0) details.Add(string.Join(" · ", lineDetails));
        }
        headline ??= hints.Count > 0 ? null : text.Trim();

        var panel = new StackPanel { MaxWidth = 300 };
        if (headline is not null)
            panel.Children.Add(new TextBlock { Text = headline, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        foreach (var detail in details)
            panel.Children.Add(Line(detail, "MenuForeground", 0.85, top: 1));
        for (var i = 0; i < hints.Count; i++)
            panel.Children.Add(Line(hints[i], "SurfaceSubtle", 1, top: i == 0 && panel.Children.Count > 0 ? 4 : 1, size: 11));
        return panel;
    }

    private static TextBlock Line(string text, string brush, double opacity, double top, double size = 12)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = opacity, FontSize = size, Margin = new Thickness(0, top, 0, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    private static bool IsHint(string piece) =>
        HintStarts.Any(h => piece.StartsWith(h, StringComparison.OrdinalIgnoreCase)
                            && (piece.Length == h.Length || !char.IsLetter(piece[h.Length])));

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
