using Avalonia.Controls.ApplicationLifetimes;

namespace Traydio.Services;

public interface ITrayController
{
    void Initialize(IClassicDesktopStyleApplicationLifetime lifetime);
}

