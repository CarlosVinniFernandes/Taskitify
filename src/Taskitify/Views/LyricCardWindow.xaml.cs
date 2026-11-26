using System.Windows;
using System.Windows.Input;
using Taskitify.ViewModels;

namespace Taskitify.Views;

/// <summary>Makes an image of a few lines of the song's lyrics, to copy or save and share.</summary>
public partial class LyricCardWindow : Window
{
    public LyricCardWindow(LyricCardViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && vm.CopyCommand.CanExecute(null))
                vm.CopyCommand.Execute(null);
        };
        Loaded += (_, _) => ScrollToSelection(vm);
    }

    /// <summary>The line the card starts with is the one playing: show it.</summary>
    private void ScrollToSelection(LyricCardViewModel vm)
    {
        if (vm.Lines.FirstOrDefault(l => l.IsSelected) is not { } line)
            return;
        var lineCount = Math.Max(1, vm.Lines.Count);
        var offset = LinesScroll.ExtentHeight * line.Index / lineCount;
        LinesScroll.ScrollToVerticalOffset(Math.Max(0, offset - (LinesScroll.ViewportHeight / 3)));
    }
}
