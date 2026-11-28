using System.Collections.ObjectModel;
using System.Windows.Input;
using Taskitify.Models;
using Taskitify.Services;

namespace Taskitify.ViewModels;

public sealed class PresentationToolbarViewModel
{
    private readonly SettingsService _settings;

    public PresentationToolbarViewModel(SettingsService settings)
    {
        _settings = settings;
        LoadTools();
        ResetToolOrderCommand = new RelayCommand(() =>
        {
            _settings.Update(s => s.PresentationToolbar.Order = PlayerToolIds.ForPresentation(null));
            LoadTools();
        });
    }

    public ObservableCollection<PlayerToolItem> PlayerTools { get; } = [];
    public ICommand ResetToolOrderCommand { get; }

    public void MoveTool(PlayerToolItem tool, int index)
    {
        var from = PlayerTools.IndexOf(tool);
        if (from < 0 || index < 0 || index >= PlayerTools.Count || index == from)
            return;
        PlayerTools.Move(from, index);
        _settings.Update(s => s.PresentationToolbar.Order = PlayerTools.Select(item => item.Id).ToList());
    }

    private void LoadTools()
    {
        PlayerTools.Clear();
        foreach (var id in PlayerToolIds.ForPresentation(_settings.Current.PresentationToolbar.Order))
            PlayerTools.Add(new PlayerToolItem(id, () => _settings.Current.PresentationToolbar.Shows(id),
                show => _settings.Update(s =>
                {
                    var hidden = s.PresentationToolbar.Hidden;
                    hidden.RemoveAll(item => item == id);
                    if (!show)
                        hidden.Add(id);
                }), settingsLabel: "PlayerSettings"));
    }
}
