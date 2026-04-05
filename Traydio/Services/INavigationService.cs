using System;

namespace Traydio.Services;

public interface INavigationService
{
    event EventHandler? Changed;

    AppPage CurrentPage { get; }

    object? CurrentPageViewModel { get; }

    void Navigate(AppPage page);
}

