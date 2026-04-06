using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using ManagedBass;
using ManagedBass.Tags;
using Microsoft.Extensions.Logging;
using Traydio.Models;
using Traydio.Services;

namespace Traydio.Plugin.ManagedBass;

public sealed class BassRadioPlayer : IRadioPlayer, IDisposable
{
    private static readonly HttpClient _http = new();

    private readonly Lock _gate = new();
    private readonly string? _nativeLibraryFolder;
    private readonly int? _preferredOutputDeviceIndex;
    private readonly string? _bassOpusDllPath;
    private readonly string? _tagsDllPath;
    private readonly ILogger<BassRadioPlayer>? _logger;

    private int _streamHandle;
    private bool _isInitialized;
    private bool _isMetadataSupported = true;
    private bool _hasLoggedMetadataUnavailable;
    private IntPtr _tagsLibraryHandle;
    private Timer? _stateTickTimer;
    private TimeSpan _elapsedBeforeClock;
    private DateTime _clockAnchorUtc;
    private bool _isClockRunning;
    private bool _isMuted;
    private int _volume = 100;

    private string? _currentStationName;
    private string? _nowPlaying;
    private string? _lastError;
    private bool _isLoading;

    public event EventHandler<RadioPlayerState>? StateChanged;

    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _streamHandle != 0 && Bass.ChannelIsActive(_streamHandle) == PlaybackState.Playing;
            }
        }
    }

    public bool IsMuted
    {
        get
        {
            lock (_gate)
            {
                return _isMuted;
            }
        }
    }

    public RadioPlayerState State
    {
        get
        {
            lock (_gate)
            {
                return CreateState();
            }
        }
    }

    public BassRadioPlayer(
        string? nativeLibraryFolder = null,
        int? preferredOutputDeviceIndex = null,
        string? bassOpusDllPath = null,
        string? tagsDllPath = null,
        ILogger<BassRadioPlayer>? logger = null)
    {
        _nativeLibraryFolder = string.IsNullOrWhiteSpace(nativeLibraryFolder)
            ? null
            : nativeLibraryFolder.Trim();

        _preferredOutputDeviceIndex = preferredOutputDeviceIndex is >= 0
            ? preferredOutputDeviceIndex
            : null;

        _bassOpusDllPath = string.IsNullOrWhiteSpace(bassOpusDllPath)
            ? null
            : bassOpusDllPath.Trim();

        _tagsDllPath = string.IsNullOrWhiteSpace(tagsDllPath)
            ? null
            : tagsDllPath.Trim();

        _logger = logger;

        InitializeBass();
        RaiseStateChanged();
    }

    public void Play(RadioStation station)
    {
        ArgumentNullException.ThrowIfNull(station);

        lock (_gate)
        {
            _isLoading = true;
            _lastError = null;
            _nowPlaying = null;
            _currentStationName = station.Name;

            StopAndFreeCurrentStream_NoLock();

            try
            {
                var resolvedUrl = ResolvePlayableUrl(station.StreamUrl).GetAwaiter().GetResult();

                _streamHandle = Bass.CreateStream(
                    resolvedUrl,
                    0,
                    BassFlags.Default,
                    null,
                    IntPtr.Zero);

                if (_streamHandle == 0)
                {
                    _isLoading = false;
                    _lastError = $"BASS failed to open stream: {Bass.LastError}";
                    RaiseStateChanged_NoLock();
                    return;
                }

                ApplyVolume_NoLock();

                if (!Bass.ChannelPlay(_streamHandle))
                {
                    _isLoading = false;
                    _lastError = $"BASS failed to start playback: {Bass.LastError}";
                    StopAndFreeCurrentStream_NoLock();
                    RaiseStateChanged_NoLock();
                    return;
                }

                ResetPlaybackClock_NoLock();
                StartPlaybackClock_NoLock();
                StartStateTicker_NoLock();

                UpdateNowPlaying_NoLock();
                RaiseStateChanged_NoLock();
            }
            catch (Exception ex)
            {
                _isLoading = false;
                _lastError = ex.Message;
                StopAndFreeCurrentStream_NoLock();
                RaiseStateChanged_NoLock();
            }
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_streamHandle != 0)
            {
                Bass.ChannelPause(_streamHandle);
                PausePlaybackClock_NoLock();
            }

            RaiseStateChanged_NoLock();
        }
    }

    public void TogglePause()
    {
        lock (_gate)
        {
            if (_streamHandle == 0)
            {
                RaiseStateChanged_NoLock();
                return;
            }

            var active = Bass.ChannelIsActive(_streamHandle);

            if (active == PlaybackState.Playing)
            {
                Bass.ChannelPause(_streamHandle);
                PausePlaybackClock_NoLock();
            }
            else
            {
                if (Bass.ChannelPlay(_streamHandle))
                {
                    StartPlaybackClock_NoLock();
                }
            }

            RaiseStateChanged_NoLock();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopAndFreeCurrentStream_NoLock();
            RaiseStateChanged_NoLock();
        }
    }

    public void SetVolume(int volume)
    {
        lock (_gate)
        {
            _volume = Math.Clamp(volume, 0, 100);

            if (_volume > 0)
            {
                _isMuted = false;
            }

            ApplyVolume_NoLock();
            RaiseStateChanged_NoLock();
        }
    }

    public void ToggleMute()
    {
        lock (_gate)
        {
            _isMuted = !_isMuted;
            ApplyVolume_NoLock();
            RaiseStateChanged_NoLock();
        }
    }

    public IReadOnlyList<RadioAudioOutputDevice> GetAudioOutputDevices()
    {
        // ManagedBass device switching for active network streams is not currently wired in this engine.
        return [];
    }

    public void SetAudioOutputDevice(string? deviceId)
    {
        // No-op for now: this engine always uses the default output device.
    }

    public void Dispose()
    {
        lock (_gate)
        {
            StopAndFreeCurrentStream_NoLock();

            if (_isInitialized)
            {
                Bass.Free();
                _isInitialized = false;
            }

            if (_tagsLibraryHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(_tagsLibraryHandle);
                _tagsLibraryHandle = IntPtr.Zero;
            }

            _stateTickTimer?.Dispose();
            _stateTickTimer = null;
        }
    }

    private void InitializeBass()
    {
        if (_isInitialized)
        {
            return;
        }

        ConfigureNativeLibraryPath();

        var outputDeviceIndex = _preferredOutputDeviceIndex ?? -1;

        if (!Bass.Init(outputDeviceIndex))
        {
            throw new InvalidOperationException($"BASS init failed: {Bass.LastError}");
        }

        // Recommended starting point for internet radio.
        Bass.PlaybackBufferLength = 250;
        Bass.UpdatePeriod = 50;
        Bass.NetBufferLength = 5000;
        Bass.NetPreBuffer = 15;
        Bass.NetPlaylist = 1;

        if (!string.IsNullOrWhiteSpace(_bassOpusDllPath))
        {
            TryLoadPlugin(_bassOpusDllPath);
        }

        // Optional: load Opus plugin if shipped.
        TryLoadPlugin("bassopus");
        TryLoadPlugin("bassopus.dll");
        TryLoadPlugin("libbassopus.so");
        TryLoadPlugin("libbassopus.dylib");

        _isMetadataSupported = DetectTagsNativeLibraryAvailability();

        _isInitialized = true;
    }

    private void ConfigureNativeLibraryPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_nativeLibraryFolder) || !Directory.Exists(_nativeLibraryFolder))
        {
            return;
        }

        SetDllDirectory(_nativeLibraryFolder);
    }

    private static async System.Threading.Tasks.Task<string> ResolvePlayableUrl(string url)
    {
        if (!LooksLikePlaylist(url))
        {
            return url;
        }

        var text = await _http.GetStringAsync(url).ConfigureAwait(false);

        using var reader = new StringReader(text);

        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            var trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            return trimmed;
        }

        throw new InvalidOperationException("Playlist did not contain a playable stream URL.");
    }

    private static bool LooksLikePlaylist(string url)
    {
        return url.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) ||
               url.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
               url.EndsWith(".pls", StringComparison.OrdinalIgnoreCase) ||
               url.EndsWith(".asx", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyVolume_NoLock()
    {
        if (_streamHandle == 0)
        {
            return;
        }

        var linear = _isMuted ? 0f : _volume / 100f;
        Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Volume, linear);
    }

    private void UpdateNowPlaying_NoLock()
    {
        if (_streamHandle == 0 || !_isMetadataSupported)
        {
            _nowPlaying = null;
            return;
        }

        try
        {
            var tag = BassTags.Read(_streamHandle, "%IFV1(%ARTI - )%TITL");
            if (string.IsNullOrWhiteSpace(tag) || IsMalformedNowPlaying(tag))
            {
                _nowPlaying = null;
                return;
            }

            _nowPlaying = tag.Trim();
        }
        catch (Exception ex) when (IsTagsLoadFailure(ex))
        {
            _nowPlaying = null;
            _isMetadataSupported = false;
            LogMetadataUnavailable("[Traydio][ManagedBass] Metadata read disabled because tags.dll is unavailable. " + ex);
        }
        catch
        {
            _nowPlaying = null;
        }
    }

    private bool DetectTagsNativeLibraryAvailability()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_tagsDllPath)
                && File.Exists(_tagsDllPath)
                && NativeLibrary.TryLoad(_tagsDllPath, out _tagsLibraryHandle))
            {
                return true;
            }

            if (!NativeLibrary.TryLoad("tags", out var handle))
            {
                LogMetadataUnavailable("[Traydio][ManagedBass] tags.dll not found. Playback will continue without metadata.");
                return false;
            }

            NativeLibrary.Free(handle);
            return true;
        }
        catch (Exception ex)
        {
            LogMetadataUnavailable("[Traydio][ManagedBass] tags.dll probe failed. Playback will continue without metadata. " + ex.Message);
            return false;
        }
    }

    private void LogMetadataUnavailable(string message)
    {
        if (_hasLoggedMetadataUnavailable)
        {
            return;
        }

        _hasLoggedMetadataUnavailable = true;
        _logger?.LogWarning("{Message}", message);
    }

    private static bool IsTagsLoadFailure(Exception ex)
    {
        if (ex is DllNotFoundException)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(ex.Message)
            && ex.Message.Contains("tags", StringComparison.OrdinalIgnoreCase)
            && ex.Message.Contains("unable to load dll", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ex.InnerException is not null && IsTagsLoadFailure(ex.InnerException);
    }

    private void StopAndFreeCurrentStream_NoLock()
    {
        _isLoading = false;

        if (_streamHandle == 0)
        {
            return;
        }

        Bass.ChannelStop(_streamHandle);
        Bass.StreamFree(_streamHandle);
        _streamHandle = 0;
        ResetPlaybackClock_NoLock();
        StopStateTicker_NoLock();
    }

    private RadioPlayerState CreateState()
    {
        var playbackState = _streamHandle == 0
            ? PlaybackState.Stopped
            : Bass.ChannelIsActive(_streamHandle);

        if (_streamHandle == 0 ||
            playbackState == PlaybackState.Playing ||
            playbackState == PlaybackState.Paused ||
            !string.IsNullOrWhiteSpace(_lastError))
        {
            _isLoading = false;
        }
        else
        {
            _isLoading = true;
        }

        if (playbackState != PlaybackState.Playing)
        {
            PausePlaybackClock_NoLock();
        }
        else
        {
            StartPlaybackClock_NoLock();
        }

        var position = GetElapsedPlaybackPosition_NoLock(playbackState);

        return new RadioPlayerState
        {
            IsPlaying = playbackState == PlaybackState.Playing,
            IsPaused = playbackState == PlaybackState.Paused,
            IsLoading = _isLoading,
            IsMuted = _isMuted,
            Volume = _volume,
            Position = position,
            Duration = null,
            CurrentStationName = _currentStationName,
            NowPlaying = _nowPlaying,
            LastError = _lastError
        };
    }

    private void RaiseStateChanged_NoLock() => StateChanged?.Invoke(this, CreateState());

    private void RaiseStateChanged() => StateChanged?.Invoke(this, State);

    private static void TryLoadPlugin(string fileName)
    {
        try
        {
            Bass.PluginLoad(fileName);
        }
        catch
        {
            // Ignore missing optional plugins.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string lpPathName);

    private void ResetPlaybackClock_NoLock()
    {
        _elapsedBeforeClock = TimeSpan.Zero;
        _clockAnchorUtc = DateTime.UtcNow;
        _isClockRunning = false;
    }

    private void StartPlaybackClock_NoLock()
    {
        if (_isClockRunning)
        {
            return;
        }

        _clockAnchorUtc = DateTime.UtcNow;
        _isClockRunning = true;
    }

    private void PausePlaybackClock_NoLock()
    {
        if (!_isClockRunning)
        {
            return;
        }

        var delta = DateTime.UtcNow - _clockAnchorUtc;
        if (delta > TimeSpan.Zero)
        {
            _elapsedBeforeClock += delta;
        }

        _isClockRunning = false;
    }

    private TimeSpan GetElapsedPlaybackPosition_NoLock(PlaybackState playbackState)
    {
        if (_streamHandle == 0 || playbackState == PlaybackState.Stopped)
        {
            return TimeSpan.Zero;
        }

        if (!_isClockRunning || playbackState != PlaybackState.Playing)
        {
            return _elapsedBeforeClock;
        }

        var delta = DateTime.UtcNow - _clockAnchorUtc;
        return delta > TimeSpan.Zero
            ? _elapsedBeforeClock + delta
            : _elapsedBeforeClock;
    }

    private void StartStateTicker_NoLock()
    {
        if (_stateTickTimer is null)
        {
            _stateTickTimer = new Timer(OnStateTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            return;
        }

        _stateTickTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void StopStateTicker_NoLock()
    {
        _stateTickTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void OnStateTick(object? state)
    {
        lock (_gate)
        {
            if (_streamHandle == 0)
            {
                return;
            }

            var playbackState = Bass.ChannelIsActive(_streamHandle);
            if (playbackState == PlaybackState.Playing || playbackState == PlaybackState.Paused)
            {
                UpdateNowPlaying_NoLock();
            }

            RaiseStateChanged_NoLock();
        }
    }

    private static bool IsMalformedNowPlaying(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Contains("expected", StringComparison.OrdinalIgnoreCase)
               && trimmed.Contains('<')
               && trimmed.Contains('>');
    }
}


