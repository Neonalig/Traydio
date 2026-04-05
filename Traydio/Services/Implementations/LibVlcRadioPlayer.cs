using System;
using LibVLCSharp.Shared;
using Traydio.Models;

namespace Traydio.Services;

public sealed class LibVlcRadioPlayer : IRadioPlayer, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private Media? _currentMedia;

    public LibVlcRadioPlayer(IStationRepository stationRepository)
    {
        Core.Initialize();
        _libVlc = new LibVLC();
        _mediaPlayer = new MediaPlayer(_libVlc);
        SetVolume(stationRepository.Volume);
    }

    public bool IsPlaying => _mediaPlayer.IsPlaying;

    public bool IsMuted => _mediaPlayer.Mute;

    public void Play(RadioStation station)
    {
        _currentMedia?.Dispose();
        _currentMedia = new Media(_libVlc, new Uri(station.StreamUrl));
        _mediaPlayer.Play(_currentMedia);
    }

    public void Pause()
    {
        if (_mediaPlayer.CanPause)
        {
            _mediaPlayer.Pause();
        }
    }

    public void TogglePause()
    {
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
            return;
        }

        _mediaPlayer.Play();
    }

    public void Stop()
    {
        _mediaPlayer.Stop();
    }

    public void SetVolume(int volume)
    {
        _mediaPlayer.Volume = Math.Clamp(volume, 0, 100);
    }

    public void ToggleMute()
    {
        _mediaPlayer.Mute = !_mediaPlayer.Mute;
    }

    public void Dispose()
    {
        _currentMedia?.Dispose();
        _mediaPlayer.Dispose();
        _libVlc.Dispose();
    }
}


