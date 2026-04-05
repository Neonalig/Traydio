using Traydio.Models;

namespace Traydio.Services;

public interface IRadioPlayer
{
    bool IsPlaying { get; }

    void Play(RadioStation station);

    void Pause();

    void TogglePause();

    void Stop();

    void SetVolume(int volume);
}

