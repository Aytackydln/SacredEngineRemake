using Avalonia.Controls;
using Avalonia.Layout;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

internal sealed class GameDirectoryPromptWindow : Window
{
    private readonly TextBox _gameDirectoryTextBox;

    public GameDirectoryPromptWindow(string gameDirectory)
    {
        Title = "Game Directory";
        Width = 680;
        MinWidth = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _gameDirectoryTextBox = new TextBox
        {
            Text = gameDirectory,
            PlaceholderText = @"E:\SteamLibrary\steamapps\common\Sacred Gold"
        };

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 90,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        okButton.Click += (_, _) => Close(_gameDirectoryTextBox.Text);

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 90,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        cancelButton.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(18),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Enter the Sacred game directory before loading the item table."
                },
                _gameDirectoryTextBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        okButton,
                        cancelButton
                    }
                }
            }
        };

        Opened += (_, _) =>
        {
            _gameDirectoryTextBox.Focus();
            _gameDirectoryTextBox.SelectAll();
        };
    }
}
