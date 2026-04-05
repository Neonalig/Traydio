using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using ManagedBass;
using ManagedBass.Tags;
using Traydio.Models;
using Traydio.Services;

namespace Traydio.Plugin.ManagedBass;

public sealed class BassRadioPlayer : IRadioPlayer, IDisposable
{
    private static readonly HttpClient _http = new();

    private readonly Lock _gate = new();
    private readonly string? _nativeLibraryFolder;
    private readonly int? _preferredOutputDeviceIndex;

    private int _streamHandle;
    private bool _isInitialized;
    private bool _isMuted;
    private int _volume = 100;

    private string? _currentStationName;
    private string? _nowPlaying;
    private string? _lastError;

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

    public BassRadioPlayer(string? nativeLibraryFolder = null, int? preferredOutputDeviceIndex = null)
    {
        _nativeLibraryFolder = string.IsNullOrWhiteSpace(nativeLibraryFolder)
            ? null
            : nativeLibraryFolder.Trim();

        _preferredOutputDeviceIndex = preferredOutputDeviceIndex is >= 0
            ? preferredOutputDeviceIndex
            : null;

        InitializeBass();
        RaiseStateChanged();
    }

    public void Play(RadioStation station)
    {
        ArgumentNullException.ThrowIfNull(station);

        lock (_gate)
        {
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
                    _lastError = $"BASS failed to open stream: {Bass.LastError}";
                    RaiseStateChanged_NoLock();
                    return;
                }

                ApplyVolume_NoLock();

                if (!Bass.ChannelPlay(_streamHandle))
                {
                    _lastError = $"BASS failed to start playback: {Bass.LastError}";
                    StopAndFreeCurrentStream_NoLock();
                    RaiseStateChanged_NoLock();
                    return;
                }

                UpdateNowPlaying_NoLock();
                RaiseStateChanged_NoLock();
            }
            catch (Exception ex)
            {
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
            }
            else
            {
                Bass.ChannelPlay(_streamHandle);
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

        // Optional: load Opus plugin if shipped.
        TryLoadPlugin("bassopus");
        TryLoadPlugin("bassopus.dll");
        TryLoadPlugin("libbassopus.so");
        TryLoadPlugin("libbassopus.dylib");

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
        if (_streamHandle == 0)
        {
            _nowPlaying = null;
            return;
        }

        var tag = BassTags.Read(_streamHandle, "%IFV1(%ARTI - )%TITL");
        _nowPlaying = string.IsNullOrWhiteSpace(tag) ? null : tag;
    }

    private void StopAndFreeCurrentStream_NoLock()
    {
        if (_streamHandle == 0)
        {
            return;
        }

        Bass.ChannelStop(_streamHandle);
        Bass.StreamFree(_streamHandle);
        _streamHandle = 0;
    }

    private RadioPlayerState CreateState()
    {
        var playbackState = _streamHandle == 0
            ? PlaybackState.Stopped
            : Bass.ChannelIsActive(_streamHandle);

        return new RadioPlayerState
        {
            IsPlaying = playbackState == PlaybackState.Playing,
            IsPaused = playbackState == PlaybackState.Paused,
            IsLoading = false,
            IsMuted = _isMuted,
            Volume = _volume,
            Position = TimeSpan.Zero,
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
}


