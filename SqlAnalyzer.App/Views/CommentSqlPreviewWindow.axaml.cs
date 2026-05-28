using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SqlAnalyzer.App.Views;

public partial class CommentSqlPreviewWindow : Window
{
    public CommentSqlPreviewWindow()
    {
        InitializeComponent();
    }
    public CommentSqlPreviewWindow(string title, string content, string copyButtonText = "复制内容", string closeButtonText = "关闭")
        : this()
    {
        Title = string.IsNullOrWhiteSpace(title) ? "内容预览" : title;
        SqlPreviewTextBox.Text = content ?? string.Empty;
        CopyButton.Content = string.IsNullOrWhiteSpace(copyButtonText) ? "复制内容" : copyButtonText;
        CloseButton.Content = string.IsNullOrWhiteSpace(closeButtonText) ? "关闭" : closeButtonText;
    }
    private async void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        TopLevel? topLevel = GetTopLevel(this);
        if (topLevel?.Clipboard != null)
        {
            await topLevel.Clipboard.SetTextAsync(SqlPreviewTextBox.Text ?? string.Empty);
        }
    }
    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
