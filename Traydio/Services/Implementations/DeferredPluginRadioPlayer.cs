using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Traydio.Common;
using Traydio.Models;

namespace Traydio.Services.Implementations;

/// <summary>
/// Defers concrete engine creation until playback is requested.
/// This lets the app start even when no playback plugin is currently active.
/// </summary>
public sealed class DeferredPluginRadioPlayer(
    IServiceProvider serviceProvider,
    IPluginManager pluginManager,
    IStationRepository stationRepository) : IRadioPlayer, IDisposable
{
    private readonly Lock _gate = new();

    private IRadioPlayer? _inner;
    private string? _lastError;
    private bool _isLoading;
    private int _playRequestVersion;

    private int _requestedVolume = Math.Clamp(stationRepository.Volume, 0, 100);
    private string? _requestedAudioOutputDeviceId = string.IsNullOrWhiteSpace(stationRepository.AudioOutputDeviceId)
        ? null
        : stationRepository.AudioOutputDeviceId.Trim();

    public event EventHandler<RadioPlayerState>? StateChanged;

    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _inner?.IsPlaying ?? false;
            }
        }
    }

    public bool IsMuted
    {
        get
        {
            lock (_gate)
            {
                return _inner?.IsMuted ?? false;
            }
        }
    }

    public RadioPlayerState State
    {
        get
        {
            lock (_gate)
            {
                return _inner?.State ?? CreateFallbackState();
            }
        }
    }

    public void Play(RadioStation station)
    {
        ArgumentNullException.ThrowIfNull(station);

        int requestVersion;
        lock (_gate)
        {
            _isLoading = true;
            _lastError = null;
            requestVersion = ++_playRequestVersion;
        }

        RaiseStateChanged();

        _ = Task.Run(() => StartPlaybackWorker(station, requestVersion));
    }

    public void Pause()
    {
        lock (_gate)
        {
            _inner?.Pause();
        }

        RaiseStateChanged();
    }

    public void TogglePause()
    {
        lock (_gate)
        {
            _inner?.TogglePause();
        }

        RaiseStateChanged();
    }

    public void Stop()
    {
        lock (_gate)
        {
            _inner?.Stop();
        }

        RaiseStateChanged();
    }

    public void SetVolume(int volume)
    {
        lock (_gate)
        {
            _requestedVolume = Math.Clamp(volume, 0, 100);
            _inner?.SetVolume(_requestedVolume);
        }

        RaiseStateChanged();
    }

    public void ToggleMute()
    {
        lock (_gate)
        {
            _inner?.ToggleMute();
        }

        RaiseStateChanged();
    }

    public IReadOnlyList<RadioAudioOutputDevice> GetAudioOutputDevices()
    {
        lock (_gate)
        {
            return _inner?.GetAudioOutputDevices() ?? [];
        }
    }

    public void SetAudioOutputDevice(string? deviceId)
    {
        var normalized = string.IsNullOrWhiteSpace(deviceId)
            ? null
            : deviceId.Trim();

        lock (_gate)
        {
            _requestedAudioOutputDeviceId = normalized;
            _inner?.SetAudioOutputDevice(normalized);
        }

        RaiseStateChanged();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_inner is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _inner = null;
        }
    }

    private void EnsureInnerPlayer_NoLock()
    {
        if (_inner is not null)
        {
            return;
        }

        var engines = pluginManager.GetPlugins()
            .SelectMany(static plugin => plugin.Capabilities.OfType<IRadioPlayerEngineCapability>())
            .GroupBy(static capability => capability.EngineId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

        if (engines.Length == 0)
        {
            _lastError = "No radio player engine plugin is currently active. Enable one in Plugin Manager.";
            return;
        }

        var requestedEngineId = stationRepository.RadioPlayerEngineId;
        var selectedEngine = !string.IsNullOrWhiteSpace(requestedEngineId)
            ? engines.FirstOrDefault(engine => string.Equals(engine.EngineId, requestedEngineId, StringComparison.OrdinalIgnoreCase))
            : null;

        selectedEngine ??= engines.First();

        var player = selectedEngine.CreatePlayer(serviceProvider);
        player.StateChanged += OnInnerStateChanged;
        player.SetVolume(_requestedVolume);

        if (!string.IsNullOrWhiteSpace(_requestedAudioOutputDeviceId))
        {
            player.SetAudioOutputDevice(_requestedAudioOutputDeviceId);
        }

        _inner = player;
        _lastError = null;

        if (!string.Equals(stationRepository.RadioPlayerEngineId, selectedEngine.EngineId, StringComparison.OrdinalIgnoreCase))
        {
            stationRepository.RadioPlayerEngineId = selectedEngine.EngineId;
        }
    }

    private void OnInnerStateChanged(object? sender, RadioPlayerState state)
    {
        StateChanged?.Invoke(this, MergeLastError(state));
    }

    private RadioPlayerState MergeLastError(RadioPlayerState state)
    {
        if (string.IsNullOrWhiteSpace(_lastError) && !_isLoading)
        {
            return state;
        }

        return new RadioPlayerState
        {
            IsPlaying = state.IsPlaying,
            IsPaused = state.IsPaused,
            IsLoading = _isLoading || state.IsLoading,
            IsMuted = state.IsMuted,
            Volume = state.Volume,
            Position = state.Position,
            Duration = state.Duration,
            CurrentStationName = state.CurrentStationName,
            NowPlaying = state.NowPlaying,
            LastError = _lastError,
        };
    }

    private RadioPlayerState CreateFallbackState()
    {
        return new RadioPlayerState
        {
            IsPlaying = false,
            IsPaused = false,
            IsLoading = _isLoading,
            IsMuted = false,
            Volume = _requestedVolume,
            Position = TimeSpan.Zero,
            Duration = null,
            CurrentStationName = null,
            NowPlaying = null,
            LastError = _lastError,
        };
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, State);
    }

    private void StartPlaybackWorker(RadioStation station, int requestVersion)
    {
        try
        {
            lock (_gate)
            {
                if (requestVersion != _playRequestVersion)
                {
                    return;
                }

                EnsureInnerPlayer_NoLock();
                _inner?.Play(station);
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (requestVersion == _playRequestVersion)
                {
                    _lastError = ex.Message;
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (requestVersion == _playRequestVersion)
                {
                    _isLoading = false;
                }
            }

            RaiseStateChanged();
        }
    }
}

