using Avalonia.Controls;
using Avalonia.Interactivity;
using SqlAnalyzer.App.Models;

namespace SqlAnalyzer.App.Views;

public partial class ObjectDetailsWindow : Window
{
    public ObjectDetailsWindow()
    {
        InitializeComponent();
    }
    public ObjectDetailsWindow(ObjectDetailsModel model, UiTextSet uiText)
        : this()
    {
        string title = string.IsNullOrWhiteSpace(model.WindowTitle) ? uiText.ObjectDetails : model.WindowTitle;
        Title = title;
        TitleTextBlock.Text = title;
        SubtitleTextBlock.Text = string.IsNullOrWhiteSpace(model.DisplayText)
            ? uiText.ObjectDetailsSubtitle
            : model.DisplayText;

        OverviewTabItem.Header = uiText.ObjectDetailsOverviewTab;
        DdlTabItem.Header = uiText.ObjectDetailsDdlTab;
        PreviewSqlTabItem.Header = uiText.ObjectDetailsPreviewSqlTab;

        DisplayNameLabelTextBlock.Text = uiText.ObjectDetailsDisplayNameLabel;
        RawNameLabelTextBlock.Text = uiText.ObjectDetailsRawNameLabel;
        SchemaLabelTextBlock.Text = uiText.ObjectDetailsSchemaLabel;
        TypeLabelTextBlock.Text = uiText.ObjectDetailsTypeLabel;
        ProviderLabelTextBlock.Text = uiText.ObjectDetailsProviderLabel;
        DisplayTextLabelTextBlock.Text = uiText.ObjectDetailsDisplayTextLabel;
        DescriptionLabelTextBlock.Text = uiText.ObjectDetailsDescriptionLabel;
        CloseButton.Content = uiText.Close;

        DisplayNameTextBlock.Text = string.IsNullOrWhiteSpace(model.DisplayName) ? "-" : model.DisplayName;
        RawNameTextBlock.Text = string.IsNullOrWhiteSpace(model.RawName) ? "-" : model.RawName;
        SchemaTextBlock.Text = string.IsNullOrWhiteSpace(model.SchemaName) ? "-" : model.SchemaName;
        TypeTextBlock.Text = string.IsNullOrWhiteSpace(model.ObjectType) ? "-" : model.ObjectType;
        ProviderTextBlock.Text = string.IsNullOrWhiteSpace(model.ProviderName) ? "-" : model.ProviderName;
        DisplayTextTextBlock.Text = string.IsNullOrWhiteSpace(model.DisplayText) ? "-" : model.DisplayText;
        DescriptionTextBlock.Text = string.IsNullOrWhiteSpace(model.Description) ? "-" : model.Description;

        DdlTextBox.Text = model.HasDdl ? model.DdlText : uiText.ObjectDetailsDdlUnavailable;
        DdlHintTextBlock.Text = uiText.ObjectDetailsDdlHint;

        PreviewSqlTextBox.Text = model.HasPreviewSql ? model.PreviewSql : uiText.ObjectDetailsPreviewUnavailable;
        PreviewHintTextBlock.Text = uiText.ObjectDetailsPreviewHint;
    }
    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
