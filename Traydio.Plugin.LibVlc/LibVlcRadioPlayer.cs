using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LibVLCSharp.Shared;
using Traydio.Models;
using Traydio.Services;

namespace Traydio.Plugin.LibVlc;

/// <summary>
/// Internet radio player backed by LibVLCSharp.
/// </summary>
public sealed class LibVlcRadioPlayer : IRadioPlayer, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly Lock _sync = new();
    private Media? _currentMedia;

    private RadioStation? _currentStation;
    private string? _lastError;
    private string? _nowPlaying;
    private bool _isLoading;
    private bool _disposed;
    private int _requestedVolume = 60;
    private string? _requestedAudioOutputDeviceId;

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
            ? new LibVLC(enableDebugLogs: true, vlcOptions)
            : new LibVLC(
                enableDebugLogs: true,
                "--no-video",
                "--input-repeat=0",
                "--no-snapshot-preview",
                "--http-reconnect",
                "--network-caching=1500",
                "--live-caching=1500",
                "--file-caching=1000",
                "--no-audio-time-stretch"
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

            _mediaPlayer.Stop();

            var oldMedia = _currentMedia;
            _currentMedia = null;
            if (oldMedia is not null)
            {
                DetachMediaEvents(oldMedia);
                oldMedia.Dispose();
            }

            var media = new Media(_libVlc, new Uri(station.StreamUrl));
            media.AddOption(":http-reconnect=true");
            media.AddOption(":network-caching=1500");
            media.AddOption(":live-caching=1500");
            media.AddOption(":no-audio-time-stretch");
            AttachMediaEvents(media);
            _currentMedia = media;

            var started = _mediaPlayer.Play(media);

            if (!started)
            {
                _isLoading = false;
                _lastError = $"Failed to start playback for '{station.Name}'.";
                DetachMediaEvents(media);
                media.Dispose();
                _currentMedia = null;
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
            ReleaseCurrentMedia();
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
            _requestedVolume = clamped;
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

    /// <inheritdoc />
    public IReadOnlyList<RadioAudioOutputDevice> GetAudioOutputDevices()
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            var devices = _mediaPlayer.AudioOutputDeviceEnum;
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (devices is null)
            {
                return [];
            }

            return devices
                .Where(static device => !string.IsNullOrWhiteSpace(device.DeviceIdentifier))
                .Select(static device => new RadioAudioOutputDevice(
                    device.DeviceIdentifier,
                    string.IsNullOrWhiteSpace(device.Description)
                        ? device.DeviceIdentifier
                        : device.Description))
                .DistinctBy(static device => device.Id)
                .ToList();
        }
    }

    /// <inheritdoc />
    public void SetAudioOutputDevice(string? deviceId)
    {
        ThrowIfDisposed();

        var normalized = string.IsNullOrWhiteSpace(deviceId)
            ? null
            : deviceId.Trim();

        lock (_sync)
        {
            _requestedAudioOutputDeviceId = normalized;
            _mediaPlayer.SetOutputDevice(null!, normalized);
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
        _libVlc.Log -= OnLibVlcLog;
        ReleaseCurrentMedia();
        _mediaPlayer.Dispose();
        _libVlc.Dispose();
    }

    private void HookEvents()
    {
        _libVlc.Log += OnLibVlcLog;
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
            _mediaPlayer.Volume = _requestedVolume;
            _mediaPlayer.SetOutputDevice(null!, _requestedAudioOutputDeviceId);
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

        RaiseStateChanged();
    }

    private void OnLibVlcLog(object? sender, LogEventArgs e)
    {
        var formatted = string.IsNullOrWhiteSpace(e.FormattedLog)
            ? $"[{e.Level}] {e.Module}: {e.Message}"
            : e.FormattedLog.Trim();

        Console.Error.WriteLine($"[Traydio][LibVLC] {formatted}");

        if (e.Level is not (LogLevel.Warning or LogLevel.Error))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(e.Message))
        {
            return;
        }

        var message = e.Message.Trim();
        if (!LooksLikeAudioError(message, e.Module))
        {
            return;
        }

        lock (_sync)
        {
            _lastError = $"LibVLC audio output error: {message}";
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

    private void AttachMediaEvents(Media media)
    {
        media.MetaChanged += OnMediaMetaChanged;
        media.ParsedChanged += OnMediaParsedChanged;
    }

    private void DetachMediaEvents(Media media)
    {
        media.MetaChanged -= OnMediaMetaChanged;
        media.ParsedChanged -= OnMediaParsedChanged;
    }

    private void ReleaseCurrentMedia()
    {
        var media = _currentMedia;
        _currentMedia = null;
        if (media is null)
        {
            return;
        }

        DetachMediaEvents(media);
        media.Dispose();
    }

    private static bool LooksLikeAudioError(string message, string? module)
    {
        var haystack = (module + " " + message).ToLowerInvariant();
        return haystack.Contains("audio")
               || haystack.Contains("aout")
               || haystack.Contains("wasapi")
               || haystack.Contains("directsound")
               || haystack.Contains("mmdevice")
               || haystack.Contains("device");
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


