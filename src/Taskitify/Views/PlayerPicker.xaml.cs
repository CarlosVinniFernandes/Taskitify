using System.Windows;
using System.Windows.Controls;

namespace Taskitify.Views;

public partial class PlayerPicker : UserControl
{
    public static readonly DependencyProperty PreviewTextProperty =
        DependencyProperty.Register(nameof(PreviewText), typeof(string), typeof(PlayerPicker));
    public static readonly DependencyProperty PreviewCaptionProperty =
        DependencyProperty.Register(nameof(PreviewCaption), typeof(string), typeof(PlayerPicker));

    public PlayerPicker() => InitializeComponent();

    public string? PreviewText
    {
        get => (string?)GetValue(PreviewTextProperty);
        set => SetValue(PreviewTextProperty, value);
    }

    public string? PreviewCaption
    {
        get => (string?)GetValue(PreviewCaptionProperty);
        set => SetValue(PreviewCaptionProperty, value);
    }
}
