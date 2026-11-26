using System.Globalization;
using System.Windows.Data;
using System.Windows;
using System.Windows.Input;
using Taskitify.Infrastructure;
using Taskitify.ViewModels;

namespace Taskitify.Views;

/// <summary>Makes an image of a few lines of the song's lyrics, to copy or save and share.</summary>
public partial class LyricCardWindow : Window
{
    public LyricCardWindow(LyricCardViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        // Blurs, shadows and a full-size card redraw on every change: on the GPU while open.
        GpuRendering.Acquire();
        Closed += (_, _) =>
        {
            vm.Dispose();
            GpuRendering.Release();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && Keyboard.FocusedElement is not System.Windows.Controls.TextBox
                     && vm.CopyCommand.CanExecute(null))
            {
                vm.CopyCommand.Execute(null);
                e.Handled = true;
            }
        };
        Loaded += (_, _) => ScrollToSelection(vm);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LyricCardViewModel.StyleSearch))
                GalleryScroll.ScrollToTop();
        };
    }

    /// <summary>The line the card starts with is the one playing: show it.</summary>
    private void ScrollToSelection(LyricCardViewModel vm)
    {
        if (vm.Lines.FirstOrDefault(l => l.IsSelected) is not { } line)
            return;
        var offset = LinesScroll.ExtentHeight * line.Index / Math.Max(1, vm.Lines.Count);
        LinesScroll.ScrollToVerticalOffset(Math.Max(0, offset - (LinesScroll.ViewportHeight / 3)));
    }
}

public sealed class StudioColumnCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double width ? Math.Clamp((int)(width / 150), 3, 6) : 3;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
