using System;
using System.Threading;
using LibVLCSharp.Shared;
using Traydio.Models;

namespace Traydio.Services;

/// <summary>
/// Internet radio player backed by LibVLCSharp.
/// </summary>
public sealed class LibVlcRadioPlayer : IRadioPlayer, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly Lock _sync = new();

    private RadioStation? _currentStation;
    private string? _lastError;
    private string? _nowPlaying;
    private bool _isLoading;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<RadioPlayerState>? StateChanged;

    /// <inheritdoc />
    public bool IsPlaying => _mediaPlayer.IsPlaying;

    /// <inheritdoc />
    public bool IsMuted => _mediaPlayer.Mute;

    /// <inheritdoc />
    public RadioPlayerState State => CreateState();

    /// <summary>
    /// Creates a new radio player instance.
    /// </summary>
    /// <param name="vlcOptions">Optional VLC startup options.</param>
    public LibVlcRadioPlayer(params string[] vlcOptions)
    {
        Core.Initialize();

        _libVlc = vlcOptions is { Length: > 0 }
            ? new LibVLC(vlcOptions)
            : new LibVLC(
                "--no-video",
                "--input-repeat=0",
                "--network-caching=1000",
                "--file-caching=1000",
                "--live-caching=1000",
                "--no-snapshot-preview"
            );

        _mediaPlayer = new MediaPlayer(_libVlc);

        HookEvents();
        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void Play(RadioStation station)
    {
        ArgumentNullException.ThrowIfNull(station);

        ThrowIfDisposed();

        lock (_sync)
        {
            _currentStation = station;
            _lastError = null;
            _nowPlaying = null;
            _isLoading = true;

            using var media = new Media(_libVlc, new Uri(station.StreamUrl));

            media.MetaChanged += OnMediaMetaChanged;
            media.ParsedChanged += OnMediaParsedChanged;

            var started = _mediaPlayer.Play(media);

            if (!started)
            {
                _isLoading = false;
                _lastError = $"Failed to start playback for '{station.Name}'.";
            }
        }

        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void Pause()
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            if (_mediaPlayer.CanPause)
            {
                _mediaPlayer.Pause();
            }
        }

        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void TogglePause()
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            if (_mediaPlayer.IsPlaying)
            {
                if (_mediaPlayer.CanPause)
                {
                    _mediaPlayer.Pause();
                }
                else
                {
                    _mediaPlayer.Stop();
                }
            }
            else if (_currentStation is not null)
            {
                Play(_currentStation);
                return;
            }
        }

        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void Stop()
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            _isLoading = false;
            _mediaPlayer.Stop();
        }

        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void SetVolume(int volume)
    {
        ThrowIfDisposed();

        var clamped = Math.Clamp(volume, 0, 100);

        lock (_sync)
        {
            _mediaPlayer.Volume = clamped;
        }

        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void ToggleMute()
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            _mediaPlayer.Mute = !_mediaPlayer.Mute;
        }

        RaiseStateChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        UnhookEvents();
        _mediaPlayer.Dispose();
        _libVlc.Dispose();
    }

    private void HookEvents()
    {
        _mediaPlayer.Opening += OnOpening;
        _mediaPlayer.Buffering += OnBuffering;
        _mediaPlayer.Playing += OnPlaying;
        _mediaPlayer.Paused += OnPaused;
        _mediaPlayer.Stopped += OnStopped;
        _mediaPlayer.EndReached += OnEndReached;
        _mediaPlayer.EncounteredError += OnEncounteredError;
        _mediaPlayer.VolumeChanged += OnVolumeChanged;
        _mediaPlayer.MediaChanged += OnMediaChanged;
        _mediaPlayer.TimeChanged += OnTimeChanged;
        _mediaPlayer.LengthChanged += OnLengthChanged;
        _mediaPlayer.Muted += OnMuted;
        _mediaPlayer.Unmuted += OnUnmuted;
    }

    private void UnhookEvents()
    {
        _mediaPlayer.Opening -= OnOpening;
        _mediaPlayer.Buffering -= OnBuffering;
        _mediaPlayer.Playing -= OnPlaying;
        _mediaPlayer.Paused -= OnPaused;
        _mediaPlayer.Stopped -= OnStopped;
        _mediaPlayer.EndReached -= OnEndReached;
        _mediaPlayer.EncounteredError -= OnEncounteredError;
        _mediaPlayer.VolumeChanged -= OnVolumeChanged;
        _mediaPlayer.MediaChanged -= OnMediaChanged;
        _mediaPlayer.TimeChanged -= OnTimeChanged;
        _mediaPlayer.LengthChanged -= OnLengthChanged;
        _mediaPlayer.Muted -= OnMuted;
        _mediaPlayer.Unmuted -= OnUnmuted;
    }

    private void OnOpening(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            _isLoading = true;
            _lastError = null;
        }

        RaiseStateChanged();
    }

    private void OnBuffering(object? sender, MediaPlayerBufferingEventArgs e)
    {
        lock (_sync)
        {
            _isLoading = e.Cache < 100f && !_mediaPlayer.IsPlaying;
        }

        RaiseStateChanged();
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            _isLoading = false;
            UpdateNowPlayingFromMedia();
        }

        RaiseStateChanged();
    }

    private void OnPaused(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            _isLoading = false;
        }

        RaiseStateChanged();
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            _isLoading = false;
        }

        RaiseStateChanged();
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            _isLoading = false;
        }

        RaiseStateChanged();
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            _isLoading = false;
            _lastError = _currentStation is null
                ? "Playback error."
                : $"Playback error while streaming '{_currentStation.Name}'.";
        }

        RaiseStateChanged();
    }

    private void OnVolumeChanged(object? sender, MediaPlayerVolumeChangedEventArgs e) => RaiseStateChanged();

    private void OnMediaChanged(object? sender, MediaPlayerMediaChangedEventArgs e)
    {
        lock (_sync)
        {
            _nowPlaying = null;
            _lastError = null;
        }

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (e.Media is not null)
        {
            e.Media.MetaChanged += OnMediaMetaChanged;
            e.Media.ParsedChanged += OnMediaParsedChanged;
        }

        RaiseStateChanged();
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e) => RaiseStateChanged();

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e) => RaiseStateChanged();

    private void OnMuted(object? sender, EventArgs e) => RaiseStateChanged();

    private void OnUnmuted(object? sender, EventArgs e) => RaiseStateChanged();

    private void OnMediaMetaChanged(object? sender, MediaMetaChangedEventArgs e)
    {
        lock (_sync)
        {
            UpdateNowPlayingFromMedia();
        }

        RaiseStateChanged();
    }

    private void OnMediaParsedChanged(object? sender, MediaParsedChangedEventArgs e)
    {
        lock (_sync)
        {
            UpdateNowPlayingFromMedia();
        }

        RaiseStateChanged();
    }

    private void UpdateNowPlayingFromMedia()
    {
        var media = _mediaPlayer.Media;
        if (media is null)
        {
            return;
        }

        var title = media.Meta(MetadataType.Title);
        var nowPlaying = media.Meta(MetadataType.NowPlaying);
        var artist = media.Meta(MetadataType.Artist);

        _nowPlaying =
            FirstNonBlank(nowPlaying, title) ??
            (string.IsNullOrWhiteSpace(artist) ? null : artist);
    }

    private RadioPlayerState CreateState()
    {
        var isPlaying = _mediaPlayer.IsPlaying;
        var state = _mediaPlayer.State;
        var volume = _mediaPlayer.Volume;
        var isMuted = _mediaPlayer.Mute;

        var time = _mediaPlayer.Time;
        var length = _mediaPlayer.Length;

        return new RadioPlayerState
        {
            IsPlaying = isPlaying,
            IsPaused = state == VLCState.Paused,
            IsLoading = _isLoading || state == VLCState.Opening || state == VLCState.Buffering,
            IsMuted = isMuted,
            Volume = Math.Clamp(volume, 0, 100),
            Position = time > 0 ? TimeSpan.FromMilliseconds(time) : TimeSpan.Zero,
            Duration = length > 0 ? TimeSpan.FromMilliseconds(length) : null,
            CurrentStationName = _currentStation?.Name,
            NowPlaying = _nowPlaying,
            LastError = _lastError
        };
    }

    private void RaiseStateChanged()
    {
        if (_disposed)
        {
            return;
        }

        StateChanged?.Invoke(this, CreateState());
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
