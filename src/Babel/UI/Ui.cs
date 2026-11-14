using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Babel.UI;

/// <summary>The colors and building blocks of the windows.</summary>
public static class Ui
{
    public static readonly SolidColorBrush Background = Brush("#0F1923");
    public static readonly SolidColorBrush Card = Brush("#16232F");
    public static readonly SolidColorBrush Line = Brush("#24384A");
    public static readonly SolidColorBrush Text = Brush("#E6EEF5");
    public static readonly SolidColorBrush Muted = Brush("#8FA3B5");
    public static readonly SolidColorBrush Accent = Brush("#4FC3F7");
    public static readonly SolidColorBrush AccentDark = Brush("#1E88B8");
    public static readonly SolidColorBrush Green = Brush("#3DBE6B");
    public static readonly SolidColorBrush Amber = Brush("#F5B942");
    public static readonly SolidColorBrush Red = Brush("#EF6B6B");

    public static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");

    public static TextBlock Label(string text, double size = 13.5, Brush? color = null, FontWeight? weight = null)
    {
        var t = new TextBlock
        {
            Text = text,
            FontFamily = Font,
            FontSize = size,
            Foreground = color ?? Text,
            FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
        };
        t.LineHeight = size * 1.45;
        return t;
    }

    public static Border Button(string text, Brush color, Action? onClick, double size = 14.5, Brush? foreground = null)
    {
        var label = Label(text, size, foreground ?? Brushes.White, FontWeights.SemiBold);
        label.TextAlignment = TextAlignment.Center;
        var b = new Border
        {
            Background = onClick == null ? Line : color,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(16, 10, 16, 10),
            Child = label,
            Cursor = onClick == null ? Cursors.Arrow : Cursors.Hand,
            Focusable = onClick != null,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        if (onClick != null)
        {
            b.MouseEnter += (_, _) => b.Opacity = 0.88;
            b.MouseLeave += (_, _) => b.Opacity = 1;
            b.MouseLeftButtonUp += (_, _) => onClick();
            b.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) onClick(); };
        }
        return b;
    }

    public static TextBlock Link(string text, Action onClick, double size = 13)
    {
        var t = Label(text, size, Accent);
        t.TextDecorations = TextDecorations.Underline;
        t.Cursor = Cursors.Hand;
        t.MouseLeftButtonUp += (_, _) => onClick();
        System.Windows.Automation.AutomationProperties.SetName(t, text);
        return t;
    }

    public static Border Box(UIElement child, Brush? border = null, Thickness? margin = null) => new()
    {
        Background = Card,
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(16, 13, 16, 14),
        BorderBrush = border ?? Card,
        BorderThickness = new Thickness(1),
        Margin = margin ?? new Thickness(0, 0, 0, 12),
        Child = child,
    };

    public static CheckBox Check(string text, bool value, Action<bool> changed)
    {
        var c = new CheckBox
        {
            Content = Label(text, 13.5),
            IsChecked = value,
            Margin = new Thickness(0, 4, 0, 4),
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        c.Checked += (_, _) => changed(true);
        c.Unchecked += (_, _) => changed(false);
        return c;
    }

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
