using System;

namespace Traydio.Services;

public sealed class RadioPlayerState
{
    public bool IsPlaying { get; init; }

    public bool IsPaused { get; init; }

    public bool IsLoading { get; init; }

    public bool IsMuted { get; init; }

    public int Volume { get; init; }

    public TimeSpan Position { get; init; }

    public TimeSpan? Duration { get; init; }

    public string? CurrentStationName { get; init; }

    public string? NowPlaying { get; init; }

    public string? LastError { get; init; }
}

