using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Views;

public partial class TableDesignerWindow : Window
{
    private TableColumnDefinition? _selectedColumn;
    private bool _isUpdatingUi;
    public TableDesignerWindow()
    {
        WorkingCopy = new TableDesignModel();
        Columns = [];
        Indexes = [];
        UniqueKeys = [];
        ForeignKeys = [];
        Checks = [];
        Triggers = [];
        Options = [];

        InitializeComponent();
        DataContext = this;
        Opened += TableDesignerWindow_Opened;
        HookCollections();
        ApplyDataTypeOptionsToAllColumns(false);
        BindCollections();
        SyncWindowState();
    }
    public TableDesignerWindow(TableDesignModel design)
        : this()
    {
        ArgumentNullException.ThrowIfNull(design);

        WorkingCopy = Clone(design);
        ReplaceItems(Columns, WorkingCopy.Columns);
        ReplaceItems(Indexes, WorkingCopy.Indexes);
        ReplaceItems(UniqueKeys, WorkingCopy.UniqueKeys);
        ReplaceItems(ForeignKeys, WorkingCopy.ForeignKeys);
        ReplaceItems(Checks, WorkingCopy.Checks);
        ReplaceItems(Triggers, WorkingCopy.Triggers);
        ReplaceItems(Options, WorkingCopy.Options);
        ApplyDataTypeOptionsToAllColumns(true);

        _selectedColumn = Columns.FirstOrDefault();
        RefreshColumnMarkers();
        BindCollections();
        if (ColumnsGrid != null)
        {
            ColumnsGrid.SelectedItem = _selectedColumn;
        }

        SyncWindowState();
    }

    public TableDesignModel WorkingCopy { get; private set; }

    public ObservableCollection<TableColumnDefinition> Columns { get; }

    public ObservableCollection<TableIndexDefinition> Indexes { get; }

    public ObservableCollection<TableIndexDefinition> UniqueKeys { get; }

    public ObservableCollection<TableForeignKeyDefinition> ForeignKeys { get; }

    public ObservableCollection<TableCheckDefinition> Checks { get; }

    public ObservableCollection<TableTriggerDefinition> Triggers { get; }

    public ObservableCollection<TableOptionDefinition> Options { get; }

    public TableDesignModel ResultDesign => BuildCurrentDesign();
    private void BindCollections()
    {
        if (ColumnsGrid == null)
        {
            return;
        }

        ColumnsGrid.ItemsSource = Columns;
        IndexesGrid.ItemsSource = Indexes;
        UniqueKeysGrid.ItemsSource = UniqueKeys;
        ForeignKeysGrid.ItemsSource = ForeignKeys;
        ChecksGrid.ItemsSource = Checks;
        TriggersGrid.ItemsSource = Triggers;
        OptionsGrid.ItemsSource = Options;
    }
    private void TableDesignerWindow_Opened(object? sender, EventArgs e)
    {
        RefreshAllGrids();
        SyncWindowState();
    }
    private void SyncWindowState()
    {
        if (TitleTextBlock == null)
        {
            return;
        }

        _isUpdatingUi = true;
        try
        {
            string qualifiedTableName = BuildQualifiedTableName();
            Title = $"{qualifiedTableName} - 表设计";
            TitleTextBlock.Text = $"{qualifiedTableName} - 表设计";
            DescriptionTextBlock.Text = BuildDescriptionText();
            QualifiedTableNameTextBlock.Text = qualifiedTableName;

            bool supportsDirectSave = WorkingCopy.SupportsDirectSave;
            SaveModeTextBlock.Text = supportsDirectSave ? "可直接保存" : "预览模式";
            SaveModeTextBlock.Foreground = supportsDirectSave
                ? Brush.Parse("#166534")
                : Brush.Parse("#92400E");
            SaveModeBadge.Background = Brush.Parse(supportsDirectSave ? "#DCFCE7" : "#FEF3C7");
            SaveButton.IsEnabled = supportsDirectSave;
            FooterSaveButton.IsEnabled = supportsDirectSave;

            CommentTextBox.Text = WorkingCopy.TableComment ?? string.Empty;
            SqlPreviewTextBox.Text = TableDesignSqlPreviewBuilder.Build(BuildCurrentDesign());
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }
    private string BuildDescriptionText()
    {
        string summary =
            $"字段 {Columns.Count} 个，索引 {Indexes.Count} 个，唯一键 {UniqueKeys.Count} 个，外键 {ForeignKeys.Count} 个，检查 {Checks.Count} 个，触发器 {Triggers.Count} 个。";

        if (Columns.Count == 0)
        {
            return $"{summary} 当前未加载到字段元数据，请检查数据库权限、对象类型或当前提供程序支持范围。";
        }

        return WorkingCopy.SupportsDirectSave
            ? $"{summary} 当前数据库支持直接保存结构变更。"
            : $"{summary} 当前数据库以预览模式打开，可查看结构和 SQL 预览，但不会直接保存到数据库。";
    }
    private string BuildQualifiedTableName()
    {
        string schemaName = string.IsNullOrWhiteSpace(WorkingCopy.SchemaName) ? "default" : WorkingCopy.SchemaName;
        string tableName = string.IsNullOrWhiteSpace(WorkingCopy.TableName) ? "unnamed_table" : WorkingCopy.TableName;
        return $"{schemaName}.{tableName}";
    }
    private void AddColumnButton_Click(object? sender, RoutedEventArgs e)
    {
        TableColumnDefinition column = new()
        {
            Name = BuildNextColumnName(),
            IsNullable = true
        };
        ApplyDataTypeOptions(column, false);
        Columns.Add(column);
        _selectedColumn = column;
        if (ColumnsGrid != null)
        {
            ColumnsGrid.SelectedItem = column;
        }

        RefreshColumnMarkers();
        RefreshPreview();
    }
    private void RemoveColumnButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedColumn == null)
        {
            return;
        }

        Columns.Remove(_selectedColumn);
        _selectedColumn = Columns.LastOrDefault();
        if (ColumnsGrid != null)
        {
            ColumnsGrid.SelectedItem = _selectedColumn;
        }

        RefreshColumnMarkers();
        RefreshPreview();
    }
    private void TogglePrimaryKeyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedColumn == null)
        {
            return;
        }

        _selectedColumn.IsPrimaryKey = !_selectedColumn.IsPrimaryKey;
        if (_selectedColumn.IsPrimaryKey)
        {
            _selectedColumn.IsNullable = false;
        }

        RefreshColumnMarkers();
        RefreshPreview();
    }
    private void ColumnsGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedColumn = ColumnsGrid?.SelectedItem as TableColumnDefinition;
    }
    private void ColumnsGrid_CellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        RefreshColumnMarkers();
        RefreshPreview();
    }
    private void ColumnTypeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: TableColumnDefinition column, SelectedItem: TableColumnTypeOption option })
        {
            return;
        }

        column.SelectedDataTypeOption = option;
        ApplyDataTypeSelection(column, option, preserveExistingValues: false);
        RefreshPreview();
    }
    private void DesignerTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        RefreshPreview();
    }
    private void CommentTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        WorkingCopy.TableComment = CommentTextBox?.Text ?? string.Empty;
        RefreshPreview();
    }
    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!WorkingCopy.SupportsDirectSave)
        {
            return;
        }

        Close(ResultDesign);
    }
    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
    private void RefreshPreview()
    {
        if (_isUpdatingUi || SqlPreviewTextBox == null)
        {
            return;
        }

        RefreshColumnMarkers();
        SqlPreviewTextBox.Text = TableDesignSqlPreviewBuilder.Build(BuildCurrentDesign());
        DescriptionTextBlock.Text = BuildDescriptionText();
    }
    private void RefreshColumnMarkers()
    {
        string[] uniqueColumns = UniqueKeys
            .Where(item => !string.IsNullOrWhiteSpace(item?.Columns))
            .SelectMany(item => item.Columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (TableColumnDefinition column in Columns)
        {
            if (column.IsPrimaryKey)
            {
                column.KeyMarker = "PK";
            }
            else if (uniqueColumns.Contains(column.Name, StringComparer.OrdinalIgnoreCase))
            {
                column.KeyMarker = "UK";
            }
            else
            {
                column.KeyMarker = string.Empty;
            }
        }
    }
    private void HookCollections()
    {
        Columns.CollectionChanged += CollectionChangedRefreshPreview;
        Indexes.CollectionChanged += CollectionChangedRefreshPreview;
        UniqueKeys.CollectionChanged += CollectionChangedRefreshPreview;
        ForeignKeys.CollectionChanged += CollectionChangedRefreshPreview;
        Checks.CollectionChanged += CollectionChangedRefreshPreview;
        Triggers.CollectionChanged += CollectionChangedRefreshPreview;
        Options.CollectionChanged += CollectionChangedRefreshPreview;
    }
    private void CollectionChangedRefreshPreview(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshPreview();
    }
    private TableDesignModel BuildCurrentDesign()
    {
        return new TableDesignModel
        {
            ProviderName = WorkingCopy.ProviderName ?? string.Empty,
            SchemaName = WorkingCopy.SchemaName ?? string.Empty,
            TableName = WorkingCopy.TableName ?? string.Empty,
            TableComment = WorkingCopy.TableComment ?? string.Empty,
            SupportsDirectSave = WorkingCopy.SupportsDirectSave,
            CapabilityLevel = WorkingCopy.CapabilityLevel ?? "PreviewOnly",
            Columns = Columns.Select(CloneColumn).ToList(),
            Indexes = Indexes.Select(CloneIndex).ToList(),
            UniqueKeys = UniqueKeys.Select(CloneIndex).ToList(),
            ForeignKeys = ForeignKeys.Select(CloneForeignKey).ToList(),
            Checks = Checks.Select(CloneCheck).ToList(),
            Triggers = Triggers.Select(CloneTrigger).ToList(),
            Options = Options.Select(CloneOption).ToList()
        };
    }
    private void RebindColumnsGrid()
    {
        if (ColumnsGrid == null)
        {
            return;
        }

        ColumnsGrid.ItemsSource = null;
        ColumnsGrid.ItemsSource = Columns;
        ColumnsGrid.SelectedItem = _selectedColumn;
    }
    private void RefreshAllGrids()
    {
        if (ColumnsGrid == null)
        {
            return;
        }

        ColumnsGrid.ItemsSource = null;
        ColumnsGrid.ItemsSource = Columns;
        ColumnsGrid.SelectedItem = _selectedColumn;

        RebindGrid(IndexesGrid, Indexes);
        RebindGrid(UniqueKeysGrid, UniqueKeys);
        RebindGrid(ForeignKeysGrid, ForeignKeys);
        RebindGrid(ChecksGrid, Checks);
        RebindGrid(TriggersGrid, Triggers);
        RebindGrid(OptionsGrid, Options);
    }
    private static void RebindGrid<T>(DataGrid? grid, ObservableCollection<T> items)
    {
        if (grid == null)
        {
            return;
        }

        grid.ItemsSource = null;
        grid.ItemsSource = items;
    }
    private string BuildNextColumnName()
    {
        int counter = Columns.Count + 1;
        string name;
        do
        {
            name = $"NEW_COLUMN_{counter}";
            counter++;
        }
        while (Columns.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)));

        return name;
    }
    private void ApplyDataTypeOptionsToAllColumns(bool preserveExistingValues)
    {
        foreach (TableColumnDefinition column in Columns)
        {
            ApplyDataTypeOptions(column, preserveExistingValues);
        }
    }
    private void ApplyDataTypeOptions(TableColumnDefinition column, bool preserveExistingValues)
    {
        column.AvailableDataTypeOptions = TableColumnTypeCatalog.GetOptionsWithCurrent(WorkingCopy.ProviderName, column.DataType);

        TableColumnTypeOption? selectedOption = TableColumnTypeCatalog.FindOption(WorkingCopy.ProviderName, column.DataType);
        if (selectedOption == null)
        {
            if (string.IsNullOrWhiteSpace(column.DataType))
            {
                selectedOption = TableColumnTypeCatalog.GetDefault(WorkingCopy.ProviderName);
                column.DataType = selectedOption.Name;
            }
            else
            {
                return;
            }
        }

        ApplyDataTypeSelection(column, selectedOption, preserveExistingValues);
    }
    private static void ApplyDataTypeSelection(TableColumnDefinition column, TableColumnTypeOption option, bool preserveExistingValues)
    {
        column.DataType = option.Name;

        if (option.SupportsLength)
        {
            if (!preserveExistingValues || !column.Length.HasValue || column.Length.Value <= 0)
            {
                column.Length = option.DefaultLength;
            }

            column.Precision = null;
            column.Scale = null;
            return;
        }

        column.Length = null;

        if (option.SupportsPrecision)
        {
            if (!preserveExistingValues || !column.Precision.HasValue || column.Precision.Value <= 0)
            {
                column.Precision = option.DefaultPrecision;
            }
        }
        else
        {
            column.Precision = null;
        }

        if (option.SupportsScale)
        {
            if (!preserveExistingValues || !column.Scale.HasValue || column.Scale.Value < 0)
            {
                column.Scale = option.DefaultScale;
            }
        }
        else
        {
            column.Scale = null;
        }
    }
    private static TableDesignModel Clone(TableDesignModel design)
    {
        return new TableDesignModel
        {
            ProviderName = design.ProviderName ?? string.Empty,
            SchemaName = design.SchemaName ?? string.Empty,
            TableName = design.TableName ?? string.Empty,
            TableComment = design.TableComment ?? string.Empty,
            SupportsDirectSave = design.SupportsDirectSave,
            CapabilityLevel = design.CapabilityLevel ?? "PreviewOnly",
            Columns = (design.Columns ?? []).Select(CloneColumn).ToList(),
            Indexes = (design.Indexes ?? []).Select(CloneIndex).ToList(),
            UniqueKeys = (design.UniqueKeys ?? []).Select(CloneIndex).ToList(),
            ForeignKeys = (design.ForeignKeys ?? []).Select(CloneForeignKey).ToList(),
            Checks = (design.Checks ?? []).Select(CloneCheck).ToList(),
            Triggers = (design.Triggers ?? []).Select(CloneTrigger).ToList(),
            Options = (design.Options ?? []).Select(CloneOption).ToList()
        };
    }
    private static TableColumnDefinition CloneColumn(TableColumnDefinition? column)
    {
        return new TableColumnDefinition
        {
            Name = column?.Name ?? string.Empty,
            DataType = column?.DataType ?? string.Empty,
            Length = column?.Length,
            Precision = column?.Precision,
            Scale = column?.Scale,
            IsPrimaryKey = column?.IsPrimaryKey ?? false,
            IsNullable = column?.IsNullable ?? true,
            Comment = column?.Comment ?? string.Empty,
            KeyMarker = column?.KeyMarker ?? string.Empty
        };
    }
    private static TableIndexDefinition CloneIndex(TableIndexDefinition? index)
    {
        return new TableIndexDefinition
        {
            Name = index?.Name ?? string.Empty,
            Columns = index?.Columns ?? string.Empty,
            IsUnique = index?.IsUnique ?? false,
            IndexType = index?.IndexType ?? string.Empty
        };
    }
    private static TableForeignKeyDefinition CloneForeignKey(TableForeignKeyDefinition? foreignKey)
    {
        return new TableForeignKeyDefinition
        {
            Name = foreignKey?.Name ?? string.Empty,
            Columns = foreignKey?.Columns ?? string.Empty,
            ReferenceTable = foreignKey?.ReferenceTable ?? string.Empty,
            ReferenceColumns = foreignKey?.ReferenceColumns ?? string.Empty,
            DeleteRule = foreignKey?.DeleteRule ?? string.Empty
        };
    }
    private static TableCheckDefinition CloneCheck(TableCheckDefinition? check)
    {
        return new TableCheckDefinition
        {
            Name = check?.Name ?? string.Empty,
            Expression = check?.Expression ?? string.Empty,
            IsEnabled = check?.IsEnabled ?? true
        };
    }
    private static TableTriggerDefinition CloneTrigger(TableTriggerDefinition? trigger)
    {
        return new TableTriggerDefinition
        {
            Name = trigger?.Name ?? string.Empty,
            TriggerType = trigger?.TriggerType ?? string.Empty,
            EventName = trigger?.EventName ?? string.Empty,
            Status = trigger?.Status ?? string.Empty,
            BodyPreview = trigger?.BodyPreview ?? string.Empty
        };
    }
    private static TableOptionDefinition CloneOption(TableOptionDefinition? option)
    {
        return new TableOptionDefinition
        {
            Name = option?.Name ?? string.Empty,
            Value = option?.Value ?? string.Empty
        };
    }
    private static void ReplaceItems<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T>? source)
    {
        target.Clear();
        if (source == null)
        {
            return;
        }

        foreach (T item in source)
        {
            target.Add(item);
        }
    }
}
