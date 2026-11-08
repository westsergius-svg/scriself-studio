namespace ScriSelfStudio.App.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using ScriSelfStudio.Presentation.Export;

/// <summary>Отрисовка экспортных абзацев без редакторских меток сцен.</summary>
public sealed class ExportPreviewText : TextBlock
{
    public static readonly StyledProperty<ExportPreviewBlock?> BlockProperty =
        AvaloniaProperty.Register<ExportPreviewText, ExportPreviewBlock?>(nameof(Block));

    public ExportPreviewBlock? Block
    {
        get => GetValue(BlockProperty);
        set => SetValue(BlockProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != BlockProperty) return;
        Inlines?.Clear();
        if (Block is not { } block) return;
        FontFamily = new FontFamily(block.FontFamily);
        FontSize = block.FontSize;
        LineHeight = block.LineHeight;
        TextAlignment = block.Alignment switch
        {
            ScriSelfStudio.Core.Text.TextAlignment.Center => TextAlignment.Center,
            ScriSelfStudio.Core.Text.TextAlignment.Right => TextAlignment.Right,
            ScriSelfStudio.Core.Text.TextAlignment.Justify => TextAlignment.Justify,
            _ => TextAlignment.Left,
        };
        Inlines ??= new InlineCollection();
        foreach (var run in block.Runs)
        {
            var inline = new Run(run.Text)
            {
                FontWeight = run.Bold ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = run.Italic ? FontStyle.Italic : FontStyle.Normal,
            };
            if (run.Underline || run.Strikethrough)
            {
                var decorations = new TextDecorationCollection();
                if (run.Underline) decorations.AddRange(Avalonia.Media.TextDecorations.Underline);
                if (run.Strikethrough) decorations.AddRange(Avalonia.Media.TextDecorations.Strikethrough);
                inline.TextDecorations = decorations;
            }
            Inlines.Add(inline);
        }
    }
}
