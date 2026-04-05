using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Traydio.Services;

namespace Traydio.Views;

public static class MessageBox
{
    public static async Task ShowDialog(Window? owner, string title, string message)
    {
        var dialog = CreateDialog(title, message, MessageBoxButtons.Ok, out var resultTask);

        if (owner is null)
        {
            dialog.Show();
            return;
        }

        await dialog.ShowDialog(owner);
        await resultTask;
    }

    public static async Task<MessageBoxResult> ShowDialog(Window? owner, string title, string message, MessageBoxButtons buttons)
    {
        var dialog = CreateDialog(title, message, buttons, out var resultTask);

        if (owner is null)
        {
            dialog.Show();
            return MessageBoxResult.None;
        }

        await dialog.ShowDialog(owner);
        return await resultTask;
    }

    private static Window CreateDialog(string title, string message, MessageBoxButtons buttons, out Task<MessageBoxResult> resultTask)
    {
        var tcs = new TaskCompletionSource<MessageBoxResult>();
        resultTask = tcs.Task;

        var dialog = new Window
        {
            Title = title,
            Width = 560,
            Height = 230,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        WindowThemeHelper.ApplyClassicWindowTheme(dialog);

        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            [Grid.RowProperty] = 1,
        };

        void AddButton(string text, MessageBoxResult result)
        {
            var button = new Button { Content = text, MinWidth = 90 };
            button.Click += (_, _) =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.SetResult(result);
                }

                dialog.Close();
            };
            buttonsPanel.Children.Add(button);
        }

        switch (buttons)
        {
            case MessageBoxButtons.Ok:
                AddButton("OK", MessageBoxResult.Ok);
                break;
            case MessageBoxButtons.YesNo:
                AddButton("Yes", MessageBoxResult.Yes);
                AddButton("No", MessageBoxResult.No);
                break;
            case MessageBoxButtons.YesNoCancel:
                AddButton("Yes", MessageBoxResult.Yes);
                AddButton("No", MessageBoxResult.No);
                AddButton("Cancel", MessageBoxResult.Cancel);
                break;
        }

        dialog.Closed += (_, _) =>
        {
            if (!tcs.Task.IsCompleted)
            {
                tcs.SetResult(MessageBoxResult.None);
            }
        };

        dialog.Content = new Grid
        {
            Margin = new Thickness(12),
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                buttonsPanel,
            },
        };

        return dialog;
    }
}

public enum MessageBoxButtons
{
    Ok,
    YesNo,
    YesNoCancel,
}

public enum MessageBoxResult
{
    None,
    Ok,
    Yes,
    No,
    Cancel,
}

