using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Traydio.Services;

public static class AppErrorHandler
{
    private static bool _isInstalled;

    public static void InstallGlobalHandlers()
    {
        if (_isInstalled)
        {
            return;
        }

        _isInstalled = true;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Report(ex, "AppDomain unhandled exception", showDialog: true);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Report(args.Exception, "Unobserved task exception", showDialog: false);
            args.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            Report(args.Exception, "Dispatcher unhandled exception", showDialog: true);
            args.Handled = true;
        };
    }

    public static void Report(Exception ex, string context, bool showDialog)
    {
        var message = $"[Traydio][Error] {context}: {ex}";
        Console.Error.WriteLine(message);
        Trace.WriteLine(message);

        if (!showDialog)
        {
            return;
        }

        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                ShowDialogCore(context, ex);
                return;
            }

            Dispatcher.UIThread.Post(() => ShowDialogCore(context, ex), DispatcherPriority.Background);
        }
        catch (Exception dialogEx)
        {
            var dialogMessage = "[Traydio][Error] Failed to show error dialog: " + dialogEx;
            Console.Error.WriteLine(dialogMessage);
            Trace.WriteLine(dialogMessage);
        }
    }

    private static void ShowDialogCore(string context, Exception ex)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        var owner = desktop.MainWindow;

        var dialog = new Window
        {
            Title = "Traydio Error",
            Width = 680,
            Height = 320,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Grid
            {
                Margin = new Thickness(12),
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = context,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    },
                    new TextBox
                    {
                        Text = ex.ToString(),
                        IsReadOnly = true,
                        AcceptsReturn = true,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        [Grid.RowProperty] = 1,
                    },
                    new Button
                    {
                        Content = "Close",
                        MinWidth = 100,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        [Grid.RowProperty] = 2,
                    },
                },
            },
        };

        if (dialog.Content is Grid grid && grid.Children[2] is Button closeButton)
        {
            closeButton.Click += (_, _) => dialog.Close();
        }

        if (owner is not null)
        {
            dialog.ShowDialog(owner).ForgetWithErrorHandling("Show unhandled error dialog", showDialog: false);
            return;
        }

        dialog.Show();
    }
}

