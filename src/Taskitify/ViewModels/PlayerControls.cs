using System.Windows.Input;
using Taskitify.Services;

namespace Taskitify.ViewModels;

/// <summary>
/// The volume button of a player: its glyph and tooltip, and the slider it opens. Spotify's volume
/// in the Windows mixer, read on demand (opening, hovering, the wheel), never polled.
/// </summary>
public sealed class VolumeControl : ObservableObject
{
    private readonly AppVolumeService _volume;
    private string _glyph = "";
    private string _tip = LocalizationService.Get("SpotifyVolume");
    private double _percent;
    private bool _isMuted;
    private bool _isAvailable = true;
    private bool _isOpen;
    private bool _applying;

    public VolumeControl(AppVolumeService volume)
    {
        _volume = volume;
        ToggleOpenCommand = new RelayCommand(() =>
        {
            Refresh();
            IsOpen = !IsOpen;
        });
        MuteCommand = new RelayCommand(() =>
        {
            _volume.ToggleMute();
            Refresh();
        });
    }

    /// <summary>The button: opens or closes the slider.</summary>
    public ICommand ToggleOpenCommand { get; }

    /// <summary>The speaker next to the slider.</summary>
    public ICommand MuteCommand { get; }

    public string Glyph { get => _glyph; private set => Set(ref _glyph, value); }

    public string Tip { get => _tip; private set => Set(ref _tip, value); }

    public bool IsMuted { get => _isMuted; private set => Set(ref _isMuted, value); }

    /// <summary>Spotify has an audio session (it played something since it started).</summary>
    public bool IsAvailable { get => _isAvailable; private set => Set(ref _isAvailable, value); }

    public bool IsOpen { get => _isOpen; set => Set(ref _isOpen, value); }

    /// <summary>0..100, bound to the slider: setting it changes Spotify's volume (and unmutes).</summary>
    public double Percent
    {
        get => _percent;
        set
        {
            value = Math.Clamp(Math.Round(value), 0, 100);
            if (!Set(ref _percent, value) || _applying)
                return;
            _volume.SetVolume((float)(value / 100));
            Show((float)(value / 100), muted: false);
        }
    }

    /// <summary>Mouse wheel: ±5 %.</summary>
    public void Nudge(int direction)
    {
        if (_volume.Get() is { } v)
        {
            _volume.SetVolume(v.Volume + (0.05f * direction));
            Refresh();
        }
    }

    public void Refresh()
    {
        if (_volume.Get() is not { } state)
        {
            IsAvailable = false;
            Glyph = "";
            Tip = LocalizationService.Get("VolumeUnavailable");
            return;
        }
        IsAvailable = true;
        _applying = true;
        Percent = state.Volume * 100;
        _applying = false;
        Show(state.Volume, state.Muted);
    }

    private void Show(float volume, bool muted)
    {
        IsMuted = muted || volume <= 0.001f;
        Glyph = IsMuted ? "" : volume < 0.34f ? "" : volume < 0.67f ? "" : "";
        Tip = muted ? LocalizationService.Get("SpotifyMuted") : LocalizationService.Format("VolumeDetail", Math.Round(volume * 100));
    }
}

/// <summary>
/// The heart of a player: liked or not, and the confirmation an unlike needs (a stray click must
/// not drop a song from Liked Songs). The state itself is shared, in <see cref="SpotifyLikeService"/>.
/// </summary>
public sealed class LikeControl : ObservableObject, IDisposable
{
    private readonly SpotifyLikeService _likes;
    private bool _isConfirming;

    public LikeControl(SpotifyLikeService likes)
    {
        _likes = likes;
        _likes.Changed += OnChanged;
        LocalizationService.Changed += OnChanged;
        ClickCommand = new RelayCommand(() =>
        {
            if (_likes.IsBusy)
                return;
            if (_likes.IsLiked == true)
                IsConfirming = true;
            else
                _ = _likes.ToggleAsync();
        });
        ConfirmCommand = new RelayCommand(() =>
        {
            IsConfirming = false;
            if (_likes.IsLiked == true)
                _ = _likes.ToggleAsync();
        });
        CancelCommand = new RelayCommand(() => IsConfirming = false);
    }

    public ICommand ClickCommand { get; }

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }

    public bool IsAvailable => _likes.IsAvailable;

    public bool IsLiked => _likes.IsLiked == true;

    public string Glyph => IsLiked ? "" : "";

    public string Tip => _likes.Failed ? LocalizationService.Get("LikeFailed")
        : _likes.IsBusy ? LocalizationService.Get("LikeSaving")
        : IsLiked ? LocalizationService.Get("Unlike")
        : LocalizationService.Get("Like");

    /// <summary>"Remove from Liked Songs?" is showing.</summary>
    public bool IsConfirming { get => _isConfirming; set => Set(ref _isConfirming, value); }

    /// <summary>The views refresh the state when the player is shown or the heart hovered.</summary>
    public void Refresh() => _likes.Refresh();

    private void OnChanged(object? sender, EventArgs e)
    {
        if (!_likes.IsAvailable || _likes.IsLiked != true)
            IsConfirming = false;
        OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        _likes.Changed -= OnChanged;
        LocalizationService.Changed -= OnChanged;
    }
}
