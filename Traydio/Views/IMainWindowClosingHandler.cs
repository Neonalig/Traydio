using System.Threading.Tasks;

namespace Traydio.Views;

/// <summary>
/// Handles main-window close requests for page-level confirmation workflows.
/// </summary>
public interface IMainWindowClosingHandler
{
    /// <summary>
    /// Returns whether the main window should continue closing.
    /// </summary>
    Task<bool> CanCloseMainWindowAsync();
}

