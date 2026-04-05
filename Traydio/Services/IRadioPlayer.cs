using System;
using Traydio.Models;

namespace Traydio.Services;

public interface IRadioPlayer
{
    event EventHandler<RadioPlayerState>? StateChanged;

    bool IsPlaying { get; }

    bool IsMuted { get; }

    RadioPlayerState State { get; }

    void Play(RadioStation station);

    void Pause();

    void TogglePause();

    void Stop();

    void SetVolume(int volume);

    void ToggleMute();
}

