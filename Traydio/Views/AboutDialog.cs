using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Traydio.Services;

namespace Traydio.Views;

public static class AboutDialog
{
    public static async Task ShowDialog(Window? parentWindow, string productName, string copyrightText, Bitmap iconBitmap)
    {
        var dialog = new Window
        {
            Title = "About " + productName,
            Width = 460,
            Height = 240,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Grid
            {
                Margin = new Thickness(12),
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
                ColumnSpacing = 10,
                RowSpacing = 8,
                Children =
                {
                    new Image
                    {
                        Source = iconBitmap,
                        Width = 48,
                        Height = 48,
                        [Grid.RowProperty] = 0,
                        [Grid.ColumnProperty] = 0,
                        [Grid.RowSpanProperty] = 3,
                    },
                    new TextBlock
                    {
                        Text = productName,
                        FontSize = 18,
                        FontWeight = FontWeight.SemiBold,
                        [Grid.RowProperty] = 0,
                        [Grid.ColumnProperty] = 1,
                    },
                    new TextBlock
                    {
                        Text = "Internet Radio Control Center",
                        [Grid.RowProperty] = 1,
                        [Grid.ColumnProperty] = 1,
                    },
                    new TextBlock
                    {
                        Text = copyrightText,
                        TextWrapping = TextWrapping.Wrap,
                        [Grid.RowProperty] = 2,
                        [Grid.ColumnProperty] = 1,
                    },
                    new Button
                    {
                        Content = "OK",
                        MinWidth = 100,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        [Grid.RowProperty] = 3,
                        [Grid.ColumnProperty] = 1,
                    },
                },
            },
        };

        WindowThemeHelper.ApplyClassicWindowTheme(dialog);

        if (dialog.Content is Grid grid && grid.Children[^1] is Button okButton)
        {
            okButton.Click += (_, _) => dialog.Close();
        }

        if (parentWindow is not null)
        {
            await dialog.ShowDialog(parentWindow);
            return;
        }

        dialog.Show();
    }
}

