using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SqlAnalyzer.App.Controls;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Views;

public sealed class ConnectionImportPreviewWindow : Window
{
    private readonly IReadOnlyList<ConnectionProfile> _existingProfiles;
    private readonly List<ConnectionImportPreviewItem> _items;
    private readonly TextBox _searchTextBox;
    private readonly ComboBox _policyComboBox;
    private readonly StackPanel _rowsHost;
    private readonly TextBlock _summaryTextBlock;
    private readonly Button _confirmButton;

    public ConnectionImportPreviewWindow(IReadOnlyList<ConnectionProfile> importedProfiles, IReadOnlyList<ConnectionProfile> existingProfiles, string sourceFileText)
    {
        Title = "导入连接";
        Width = 1080;
        Height = 690;
        MinWidth = 900;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#F8FAFC");

        _existingProfiles = existingProfiles;
        _items = importedProfiles
            .Select(profile => new ConnectionImportPreviewItem
            {
                Profile = profile,
                IsSelected = true,
                ResolvedName = profile.Name
            })
            .ToList();

        _searchTextBox = new TextBox
        {
            Watermark = "搜索连接、主机、数据库、分组、环境标签",
            MinHeight = 30
        };
        _searchTextBox.TextChanged += (_, _) => RebuildRows();

        _policyComboBox = new ComboBox
        {
            ItemsSource = new[] { "重名连接自动重命名", "覆盖本地已有连接", "跳过冲突连接" },
            SelectedIndex = 0,
            MinWidth = 190
        };
        _policyComboBox.SelectionChanged += (_, _) =>
        {
            ApplyConflictPolicy();
            RebuildRows();
        };

        _summaryTextBlock = new TextBlock
        {
            Foreground = Brush.Parse("#475569"),
            VerticalAlignment = VerticalAlignment.Center
        };

        _rowsHost = new StackPanel
        {
            Spacing = 0
        };

        _confirmButton = new Button
        {
            Content = BuildIconText("Import", "导入选中连接"),
            MinWidth = 120
        };
        _confirmButton.Click += (_, _) => Close(true);

        Content = BuildContent(sourceFileText);
        ApplyConflictPolicy();
        RebuildRows();
    }

    public ConnectionImportConflictPolicy ConflictPolicy => _policyComboBox.SelectedIndex switch
    {
        1 => ConnectionImportConflictPolicy.Replace,
        2 => ConnectionImportConflictPolicy.Skip,
        _ => ConnectionImportConflictPolicy.Rename
    };

    public IReadOnlyList<ConnectionImportPreviewItem> SelectedItems =>
        _items.Where(item => item.IsSelected && item.IsImportable && !string.Equals(item.ImportAction, "Skip", StringComparison.OrdinalIgnoreCase)).ToArray();

    private Control BuildContent(string sourceFileText)
    {
        Grid root = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            Margin = new Avalonia.Thickness(14)
        };

        Control titleRow = BuildTitleRow("Import", "预览并选择要导入的连接", $"文件：{sourceFileText}");
        ToolTip.SetTip(titleRow, sourceFileText);
        root.Children.Add(titleRow);

        Grid policyRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            ColumnSpacing = 8,
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        };
        policyRow.Children.Add(new TextBlock
        {
            Text = "冲突处理：",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#334155")
        });
        Grid.SetColumn(_policyComboBox, 1);
        policyRow.Children.Add(_policyComboBox);
        Grid.SetRow(policyRow, 1);
        root.Children.Add(policyRow);

        Grid toolbar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        };
        toolbar.Children.Add(_searchTextBox);

        Button selectVisibleButton = new()
        {
            Content = "全选当前筛选",
            MinWidth = 110
        };
        selectVisibleButton.Click += (_, _) => SelectVisibleItems(true);
        Grid.SetColumn(selectVisibleButton, 1);
        toolbar.Children.Add(selectVisibleButton);

        Button invertVisibleButton = new()
        {
            Content = "反选当前筛选",
            MinWidth = 110
        };
        invertVisibleButton.Click += (_, _) => InvertVisibleItems();
        Grid.SetColumn(invertVisibleButton, 2);
        toolbar.Children.Add(invertVisibleButton);

        Button clearButton = new()
        {
            Content = "清空选择",
            MinWidth = 88
        };
        clearButton.Click += (_, _) => SelectVisibleItems(false, allItems: true);
        Grid.SetColumn(clearButton, 3);
        toolbar.Children.Add(clearButton);

        Grid.SetRow(toolbar, 2);
        root.Children.Add(toolbar);

        Border listBorder = new()
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#D7DCE5"),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(8),
            Child = BuildListContainer()
        };
        Grid.SetRow(listBorder, 3);
        root.Children.Add(listBorder);

        Grid footer = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
            Margin = new Avalonia.Thickness(0, 12, 0, 0)
        };
        footer.Children.Add(_summaryTextBlock);

        Button cancelButton = new()
        {
            Content = BuildIconText("Error", "取消"),
            MinWidth = 84
        };
        cancelButton.Click += (_, _) => Close(false);
        Grid.SetColumn(cancelButton, 1);
        footer.Children.Add(cancelButton);

        Grid.SetColumn(_confirmButton, 2);
        footer.Children.Add(_confirmButton);

        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        return root;
    }

    private static Control BuildTitleRow(string iconKind, string titleText, string descriptionText)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12,
            Margin = new Avalonia.Thickness(0, 0, 0, 12)
        };
        row.Children.Add(BuildIconTile(iconKind, 42, 24));

        StackPanel textPanel = new()
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center
        };
        textPanel.Children.Add(new TextBlock
        {
            Text = titleText,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#0F172A")
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = descriptionText,
            Foreground = Brush.Parse("#64748B"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(textPanel, 1);
        row.Children.Add(textPanel);
        return row;
    }

    private static Control BuildIconTile(string iconKind, double tileSize, double iconSize)
    {
        return new Border
        {
            Width = tileSize,
            Height = tileSize,
            CornerRadius = new Avalonia.CornerRadius(13),
            Background = Brush.Parse("#EAF5FF"),
            BorderBrush = Brush.Parse("#BBD8F2"),
            BorderThickness = new Avalonia.Thickness(1),
            Child = new BlueIcon
            {
                Kind = iconKind,
                Width = iconSize,
                Height = iconSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static Control BuildIconText(string iconKind, string text)
    {
        StackPanel panel = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(new BlueIcon
        {
            Kind = iconKind,
            Width = 14,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private Control BuildListContainer()
    {
        Grid listContainer = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*")
        };
        listContainer.Children.Add(BuildHeaderRow());

        ScrollViewer scrollViewer = new()
        {
            Content = _rowsHost,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Grid.SetRow(scrollViewer, 1);
        listContainer.Children.Add(scrollViewer);
        return listContainer;
    }

    private static Control BuildHeaderRow()
    {
        Grid header = new()
        {
            ColumnDefinitions = new ColumnDefinitions("52,1.35*,0.8*,1.15*,1.05*,1.6*"),
            Background = Brush.Parse("#EEF3F8")
        };
        AddHeaderCell(header, "选择", 0);
        AddHeaderCell(header, "连接名称", 1);
        AddHeaderCell(header, "数据库类型", 2);
        AddHeaderCell(header, "主机 / 服务", 3);
        AddHeaderCell(header, "数据库 / 模式", 4);
        AddHeaderCell(header, "导入状态", 5);
        return header;
    }

    private static void AddHeaderCell(Grid header, string text, int column)
    {
        TextBlock cell = new()
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#334155"),
            Margin = new Avalonia.Thickness(8, 7),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(cell, column);
        header.Children.Add(cell);
    }

    private void ApplyConflictPolicy()
    {
        foreach (ConnectionImportPreviewItem item in _items)
        {
            item.IsImportable = !string.IsNullOrWhiteSpace(item.Profile.Name) && !string.IsNullOrWhiteSpace(item.Profile.ProviderName);
            item.HasConflict = item.IsImportable && HasExistingConflict(item.Profile);
            item.ResolvedName = item.Profile.Name ?? string.Empty;

            if (!item.IsImportable)
            {
                item.ImportAction = "Skip";
                item.IsSelected = false;
                item.StatusText = "信息不完整，不能导入";
                item.WarningText = "连接名称或数据库类型为空。";
                continue;
            }

            if (!item.HasConflict)
            {
                item.ImportAction = "Add";
                item.StatusText = "可导入";
                item.WarningText = string.Empty;
                continue;
            }

            switch (ConflictPolicy)
            {
                case ConnectionImportConflictPolicy.Replace:
                    item.ImportAction = "Replace";
                    item.IsSelected = true;
                    item.StatusText = "将覆盖本地已有连接";
                    item.WarningText = "覆盖时会保留本地连接 ID，减少已打开工作区引用失效。";
                    break;
                case ConnectionImportConflictPolicy.Skip:
                    item.ImportAction = "Skip";
                    item.IsSelected = false;
                    item.StatusText = "冲突，已按策略跳过";
                    item.WarningText = "本地已存在同名、同类型、同主机的连接。";
                    break;
                default:
                    item.ImportAction = "Rename";
                    item.IsSelected = true;
                    item.ResolvedName = BuildUniqueName(item.Profile.Name ?? string.Empty, _existingProfiles.Select(profile => profile.Name));
                    item.StatusText = $"将重命名为 {item.ResolvedName}";
                    item.WarningText = "本地已存在同名、同类型、同主机的连接。";
                    break;
            }
        }
    }

    private void RebuildRows()
    {
        _rowsHost.Children.Clear();
        IReadOnlyList<ConnectionImportPreviewItem> visibleItems = GetVisibleItems();
        if (visibleItems.Count == 0)
        {
            _rowsHost.Children.Add(new TextBlock
            {
                Text = "没有匹配的连接。",
                Foreground = Brush.Parse("#64748B"),
                Margin = new Avalonia.Thickness(12)
            });
        }
        else
        {
            foreach (ConnectionImportPreviewItem item in visibleItems)
            {
                _rowsHost.Children.Add(BuildDataRow(item));
            }
        }

        UpdateSummary();
    }

    private Control BuildDataRow(ConnectionImportPreviewItem item)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("52,1.35*,0.8*,1.15*,1.05*,1.6*"),
            MinHeight = 42
        };

        bool canSelect = item.IsImportable && !string.Equals(item.ImportAction, "Skip", StringComparison.OrdinalIgnoreCase);
        CheckBox checkBox = new()
        {
            IsChecked = item.IsSelected,
            IsEnabled = canSelect,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        checkBox.IsCheckedChanged += (_, _) =>
        {
            item.IsSelected = checkBox.IsChecked == true;
            UpdateSummary();
        };
        row.Children.Add(checkBox);

        AddDataCell(row, item.Profile.Name, 1, true);
        AddDataCell(row, item.Profile.ProviderName, 2);
        AddDataCell(row, BuildEndpointText(item.Profile), 3);
        AddDataCell(row, BuildDatabaseText(item.Profile), 4);
        AddStatusCell(row, item, 5);

        Border border = new()
        {
            BorderBrush = Brush.Parse("#E5EAF1"),
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            Background = item.IsImportable ? Brushes.White : Brush.Parse("#FFF7ED"),
            Child = row
        };
        return border;
    }

    private static void AddDataCell(Grid row, string text, int column, bool strong = false)
    {
        TextBlock cell = new()
        {
            Text = string.IsNullOrWhiteSpace(text) ? "-" : text,
            FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal,
            Foreground = strong ? Brush.Parse("#0F172A") : Brush.Parse("#334155"),
            Margin = new Avalonia.Thickness(8, 6),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        ToolTip.SetTip(cell, cell.Text);
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
    }

    private static void AddStatusCell(Grid row, ConnectionImportPreviewItem item, int column)
    {
        TextBlock cell = new()
        {
            Text = item.StatusText,
            Foreground = item.IsImportable ? Brush.Parse(item.HasConflict ? "#B45309" : "#166534") : Brush.Parse("#C2410C"),
            Margin = new Avalonia.Thickness(8, 6),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        ToolTip.SetTip(cell, string.IsNullOrWhiteSpace(item.WarningText) ? item.StatusText : $"{item.StatusText}\n{item.WarningText}");
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
    }

    private IReadOnlyList<ConnectionImportPreviewItem> GetVisibleItems()
    {
        string keyword = (_searchTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return _items;
        }

        return _items.Where(item => Matches(item.Profile, keyword) || item.StatusText.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void SelectVisibleItems(bool selected, bool allItems = false)
    {
        IEnumerable<ConnectionImportPreviewItem> targetItems = allItems ? _items : GetVisibleItems();
        foreach (ConnectionImportPreviewItem item in targetItems)
        {
            if (item.IsImportable && !string.Equals(item.ImportAction, "Skip", StringComparison.OrdinalIgnoreCase))
            {
                item.IsSelected = selected;
            }
        }
        RebuildRows();
    }

    private void InvertVisibleItems()
    {
        foreach (ConnectionImportPreviewItem item in GetVisibleItems())
        {
            if (item.IsImportable && !string.Equals(item.ImportAction, "Skip", StringComparison.OrdinalIgnoreCase))
            {
                item.IsSelected = !item.IsSelected;
            }
        }
        RebuildRows();
    }

    private void UpdateSummary()
    {
        int selectedCount = SelectedItems.Count;
        int importableCount = _items.Count(item => item.IsImportable);
        int conflictCount = _items.Count(item => item.HasConflict);
        int invalidCount = _items.Count(item => !item.IsImportable);
        _summaryTextBlock.Text = $"已选择 {selectedCount} / 可导入 {importableCount} 个连接，冲突 {conflictCount} 个，无效 {invalidCount} 个";
        _confirmButton.IsEnabled = selectedCount > 0;
    }

    private bool HasExistingConflict(ConnectionProfile profile)
    {
        return _existingProfiles.Any(existing => IsSameConnectionIdentity(existing, profile));
    }

    private static bool IsSameConnectionIdentity(ConnectionProfile left, ConnectionProfile right)
    {
        return string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.ProviderName, right.ProviderName, StringComparison.OrdinalIgnoreCase)
               && string.Equals(BuildEndpointText(left), BuildEndpointText(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildUniqueName(string baseName, IEnumerable<string> existingNames)
    {
        HashSet<string> names = existingNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string normalizedBase = string.IsNullOrWhiteSpace(baseName) ? "Imported Connection" : baseName.Trim();
        string candidate = normalizedBase;
        int index = 1;
        while (names.Contains(candidate))
        {
            candidate = $"{normalizedBase}_导入{index++}";
        }
        return candidate;
    }

    private static bool Matches(ConnectionProfile profile, string keyword)
    {
        string haystack = string.Join('\n', new[]
        {
            profile.Name,
            profile.ProviderName,
            profile.Server,
            profile.OracleHost,
            profile.OracleTnsName,
            profile.Database,
            profile.Schema,
            profile.GroupName,
            profile.EnvironmentTag,
            profile.UserName
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return haystack.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildEndpointText(ConnectionProfile profile)
    {
        if (string.Equals(profile.ProviderName, "Oracle", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(profile.OracleConnectionMode, "Tns", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(profile.OracleTnsName) ? profile.Server : profile.OracleTnsName;
            }
            string host = string.IsNullOrWhiteSpace(profile.OracleHost) ? profile.Server : profile.OracleHost;
            return string.IsNullOrWhiteSpace(host) ? string.Empty : $"{host}:{profile.OraclePort}";
        }

        return profile.Port > 0 ? $"{profile.Server}:{profile.Port}" : profile.Server;
    }

    private static string BuildDatabaseText(ConnectionProfile profile)
    {
        string database = string.IsNullOrWhiteSpace(profile.Database) ? profile.OracleServiceName : profile.Database;
        return string.IsNullOrWhiteSpace(profile.Schema) ? database : $"{database} / {profile.Schema}";
    }
}
