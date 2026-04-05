using System;
using LibVLCSharp.Shared;
using Traydio.Models;

namespace Traydio.Services;

public sealed class LibVlcRadioPlayer : IRadioPlayer, IDisposable
{
    private static readonly string[] _libVlcArguments =
    [
        "--network-caching=1500",
        "--live-caching=1500",
        "--clock-jitter=0",
        "--clock-synchro=0",
        "--http-reconnect",
        "--no-video"
    ];

    private static readonly string[] _mediaOptions =
    [
        ":network-caching=1500",
        ":live-caching=1500",
        ":clock-jitter=0",
        ":clock-synchro=0",
        ":http-reconnect=true",
        ":no-video"
    ];

    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private Media? _currentMedia;

    private bool _isLoading;
    private bool _isPaused;
    private int _volume;
    private TimeSpan _position;
    private TimeSpan? _duration;
    private string? _currentStationName;
    private string? _nowPlaying;
    private string? _lastError;

    public event EventHandler<RadioPlayerState>? StateChanged;

    public LibVlcRadioPlayer(IStationRepository stationRepository)
    {
        Core.Initialize();
        _libVlc = new LibVLC(_libVlcArguments);
        _mediaPlayer = new MediaPlayer(_libVlc);

        HookMediaPlayerEvents();
        SetVolume(stationRepository.Volume);
        PublishState();
    }

    public bool IsPlaying => _mediaPlayer.IsPlaying;

    public bool IsMuted => _mediaPlayer.Mute;

    public RadioPlayerState State => new()
    {
        IsPlaying = IsPlaying,
        IsPaused = _isPaused,
        IsLoading = _isLoading,
        IsMuted = IsMuted,
        Volume = _volume,
        Position = _position,
        Duration = _duration,
        CurrentStationName = _currentStationName,
        NowPlaying = _nowPlaying,
        LastError = _lastError,
    };

    public void Play(RadioStation station)
    {
        try
        {
            _currentMedia?.Dispose();
            _currentMedia = new Media(_libVlc, new Uri(station.StreamUrl));
            ApplyMediaWorkarounds(_currentMedia);
            _currentStationName = station.Name;
            _nowPlaying = null;
            _lastError = null;
            _isLoading = true;
            _isPaused = false;
            _position = TimeSpan.Zero;
            _duration = null;
            PublishState();

            _mediaPlayer.Play(_currentMedia);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _isLoading = false;
            Console.Error.WriteLine($"[Traydio][LibVLC][Play] {ex}");
            Console.WriteLine($"[Traydio][LibVLC][Play] {ex.Message}");
            PublishState();
        }
    }

    public void Pause()
    {
        try
        {
            if (_mediaPlayer.CanPause)
            {
                _mediaPlayer.Pause();
            }
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Console.Error.WriteLine($"[Traydio][LibVLC][Pause] {ex}");
            Console.WriteLine($"[Traydio][LibVLC][Pause] {ex.Message}");
            PublishState();
        }
    }

    public void TogglePause()
    {
        try
        {
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
                return;
            }

            _mediaPlayer.Play();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Console.Error.WriteLine($"[Traydio][LibVLC][TogglePause] {ex}");
            Console.WriteLine($"[Traydio][LibVLC][TogglePause] {ex.Message}");
            PublishState();
        }
    }

    public void Stop()
    {
        try
        {
            _mediaPlayer.Stop();
            _isLoading = false;
            _isPaused = false;
            _position = TimeSpan.Zero;
            _duration = null;
            _nowPlaying = null;
            PublishState();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Console.Error.WriteLine($"[Traydio][LibVLC][Stop] {ex}");
            Console.WriteLine($"[Traydio][LibVLC][Stop] {ex.Message}");
            PublishState();
        }
    }

    public void SetVolume(int volume)
    {
        try
        {
            _volume = Math.Clamp(volume, 0, 100);
            _mediaPlayer.Volume = _volume;
            PublishState();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Console.Error.WriteLine($"[Traydio][LibVLC][SetVolume] {ex}");
            Console.WriteLine($"[Traydio][LibVLC][SetVolume] {ex.Message}");
            PublishState();
        }
    }

    public void ToggleMute()
    {
        try
        {
            _mediaPlayer.Mute = !_mediaPlayer.Mute;
            PublishState();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Console.Error.WriteLine($"[Traydio][LibVLC][ToggleMute] {ex}");
            Console.WriteLine($"[Traydio][LibVLC][ToggleMute] {ex.Message}");
            PublishState();
        }
    }

    public void Dispose()
    {
        _currentMedia?.Dispose();
        _mediaPlayer.Dispose();
        _libVlc.Dispose();
    }

    private void HookMediaPlayerEvents()
    {
        _mediaPlayer.Opening += (_, _) =>
        {
            _isLoading = true;
            _isPaused = false;
            _lastError = null;
            PublishState();
        };

        _mediaPlayer.Buffering += (_, e) =>
        {
            _isLoading = e.Cache < 100 && !_mediaPlayer.IsPlaying;
            PublishState();
        };

        _mediaPlayer.Playing += (_, _) =>
        {
            _isLoading = false;
            _isPaused = false;
            _lastError = null;
            UpdateNowPlayingFromMedia();
            PublishState();
        };

        _mediaPlayer.Paused += (_, _) =>
        {
            _isLoading = false;
            _isPaused = true;
            PublishState();
        };

        _mediaPlayer.Stopped += (_, _) =>
        {
            _isLoading = false;
            _isPaused = false;
            _position = TimeSpan.Zero;
            _duration = null;
            PublishState();
        };

        _mediaPlayer.EndReached += (_, _) =>
        {
            _isLoading = false;
            _isPaused = false;
            _position = TimeSpan.Zero;
            PublishState();
        };

        _mediaPlayer.TimeChanged += (_, e) =>
        {
            _position = TimeSpan.FromMilliseconds(e.Time);
            UpdateNowPlayingFromMedia();
            PublishState();
        };

        _mediaPlayer.LengthChanged += (_, e) =>
        {
            _duration = e.Length > 0 ? TimeSpan.FromMilliseconds(e.Length) : null;
            PublishState();
        };

        _mediaPlayer.VolumeChanged += (_, e) =>
        {
            var reported = e.Volume;
            // Some backends report 0..1 while others report 0..100.
            var normalized = reported <= 1.0f
                ? (int)Math.Round(reported * 100.0f)
                : (int)Math.Round(reported);
            _volume = Math.Clamp(normalized, 0, 100);
            PublishState();
        };

        _mediaPlayer.EncounteredError += (_, _) =>
        {
            _isLoading = false;
            _lastError = "Playback error from media backend.";
            Console.Error.WriteLine("[Traydio][LibVLC] EncounteredError event.");
            Console.WriteLine("[Traydio][LibVLC] EncounteredError event.");
            PublishState();
        };
    }

    private void UpdateNowPlayingFromMedia()
    {
        try
        {
            if (_currentMedia is null)
            {
                return;
            }

            var nowPlaying = _currentMedia.Meta(MetadataType.NowPlaying);
            if (!string.IsNullOrWhiteSpace(nowPlaying))
            {
                _nowPlaying = nowPlaying;
                return;
            }

            var title = _currentMedia.Meta(MetadataType.Title);
            if (!string.IsNullOrWhiteSpace(title))
            {
                _nowPlaying = title;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Traydio][LibVLC][Metadata] {ex}");
            Console.WriteLine($"[Traydio][LibVLC][Metadata] {ex.Message}");
        }
    }

    private void PublishState()
    {
        StateChanged?.Invoke(this, State);
    }

    private static void ApplyMediaWorkarounds(Media media)
    {
        foreach (var option in _mediaOptions)
        {
            media.AddOption(option);
        }
    }
}
