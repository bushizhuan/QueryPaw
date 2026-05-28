using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SqlAnalyzer.App.Controls;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Views;

public sealed class ConnectionExportSelectionWindow : Window
{
    private readonly List<ConnectionExportSelectionItem> _items;
    private readonly TextBox _searchTextBox;
    private readonly StackPanel _rowsHost;
    private readonly TextBlock _summaryTextBlock;
    private readonly Button _confirmButton;

    public ConnectionExportSelectionWindow(IReadOnlyList<ConnectionProfile> profiles, string preferredProfileId)
    {
        Title = "导出连接";
        Width = 980;
        Height = 650;
        MinWidth = 820;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#F8FAFC");

        bool hasPreferred = profiles.Any(item => string.Equals(item.Id, preferredProfileId, StringComparison.OrdinalIgnoreCase));
        _items = profiles
            .OrderBy(item => item.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new ConnectionExportSelectionItem
            {
                Profile = item,
                IsSelected = hasPreferred
                    ? string.Equals(item.Id, preferredProfileId, StringComparison.OrdinalIgnoreCase)
                    : profiles.Count == 1
            })
            .ToList();

        _searchTextBox = new TextBox
        {
            Watermark = "搜索连接、主机、数据库、分组、环境标签",
            MinHeight = 30
        };
        _searchTextBox.TextChanged += (_, _) => RebuildRows();

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
            Content = BuildIconText("Export", "下一步：选择保存位置"),
            MinWidth = 150
        };
        _confirmButton.Click += (_, _) => Close(true);

        Content = BuildContent();
        RebuildRows();
    }

    public IReadOnlyList<ConnectionProfile> SelectedProfiles =>
        _items.Where(item => item.IsSelected).Select(item => item.Profile).ToArray();

    private Control BuildContent()
    {
        Grid root = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Avalonia.Thickness(14)
        };

        root.Children.Add(BuildTitleRow(
            "Export",
            "选择要导出的数据库连接",
            "可一次导出一个或多个连接，导出的 .wlb 文件不包含连接密码。"));

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

        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);

        Border listBorder = new()
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#D7DCE5"),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(8),
            Child = BuildListContainer()
        };
        Grid.SetRow(listBorder, 2);
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

        Grid.SetRow(footer, 3);
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
            TextWrapping = TextWrapping.Wrap
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
            ColumnDefinitions = new ColumnDefinitions("52,1.5*,0.9*,1.3*,1.1*,1.1*"),
            Background = Brush.Parse("#EEF3F8")
        };
        AddHeaderCell(header, "选择", 0);
        AddHeaderCell(header, "连接名称", 1);
        AddHeaderCell(header, "数据库类型", 2);
        AddHeaderCell(header, "主机 / 服务", 3);
        AddHeaderCell(header, "数据库 / 模式", 4);
        AddHeaderCell(header, "分组 / 环境", 5);
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

    private void RebuildRows()
    {
        _rowsHost.Children.Clear();
        IReadOnlyList<ConnectionExportSelectionItem> visibleItems = GetVisibleItems();
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
            foreach (ConnectionExportSelectionItem item in visibleItems)
            {
                _rowsHost.Children.Add(BuildDataRow(item));
            }
        }

        UpdateSummary();
    }

    private Control BuildDataRow(ConnectionExportSelectionItem item)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("52,1.5*,0.9*,1.3*,1.1*,1.1*"),
            MinHeight = 42
        };

        CheckBox checkBox = new()
        {
            IsChecked = item.IsSelected,
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
        AddDataCell(row, BuildGroupText(item.Profile), 5);

        Border border = new()
        {
            BorderBrush = Brush.Parse("#E5EAF1"),
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
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

    private IReadOnlyList<ConnectionExportSelectionItem> GetVisibleItems()
    {
        string keyword = (_searchTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return _items;
        }

        return _items.Where(item => Matches(item.Profile, keyword)).ToArray();
    }

    private void SelectVisibleItems(bool selected, bool allItems = false)
    {
        IEnumerable<ConnectionExportSelectionItem> targetItems = allItems ? _items : GetVisibleItems();
        foreach (ConnectionExportSelectionItem item in targetItems)
        {
            item.IsSelected = selected;
        }
        RebuildRows();
    }

    private void InvertVisibleItems()
    {
        foreach (ConnectionExportSelectionItem item in GetVisibleItems())
        {
            item.IsSelected = !item.IsSelected;
        }
        RebuildRows();
    }

    private void UpdateSummary()
    {
        int selectedCount = _items.Count(item => item.IsSelected);
        _summaryTextBlock.Text = $"已选择 {selectedCount} / 共 {_items.Count} 个连接";
        _confirmButton.IsEnabled = selectedCount > 0;
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

    private static string BuildGroupText(ConnectionProfile profile)
    {
        string group = string.IsNullOrWhiteSpace(profile.GroupName) ? "默认分组" : profile.GroupName;
        string environment = string.IsNullOrWhiteSpace(profile.EnvironmentTag) ? "DEV" : profile.EnvironmentTag;
        return $"{group} / {environment}";
    }

    private sealed class ConnectionExportSelectionItem
    {
        public ConnectionProfile Profile { get; init; } = new();
        public bool IsSelected { get; set; }
    }
}
