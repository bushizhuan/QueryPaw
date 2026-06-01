using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using SqlAnalyzer.App.Models;

namespace SqlAnalyzer.App.Services;

public sealed class ResultWorkspaceController
{
    private readonly Dictionary<int, Border> _headerBorders = [];
    private readonly HashSet<int> _pinnedHeaderColumns = [];
    private readonly Dictionary<ResultRowViewItem, Border> _rowBorders = [];
    private readonly Dictionary<ResultRowViewItem, Border> _actionBorders = [];
    private readonly Dictionary<(ResultRowViewItem Row, int ColumnIndex), Border> _cellBorders = [];
    private readonly HashSet<ResultRowViewItem> _selectedRows = [];
    private readonly HashSet<(ResultRowViewItem Row, int ColumnIndex)> _selectedCells = [];
    private ResultRowViewItem? _selectedRow;
    private ResultRowViewItem? _selectionAnchorRow;
    private ResultCellContext? _cellSelectionAnchor;
    private ResultCellContext? _selectedCell;
    private bool _isDraggingSelection;
    private bool _isDraggingCellSelection;
    private bool _isDraggingHeaderSelection;

    public ResultRowViewItem? SelectedRow => _selectedRow;

    public IReadOnlyList<ResultRowViewItem> SelectedRows => _selectedRows.ToArray();

    public bool HasSelection => _selectedRows.Count > 0 || _selectedRow != null;

    public bool HasCellSelection => _selectedCells.Count > 0 || _selectedCell != null;

    public bool IsDraggingCellSelection => _isDraggingCellSelection;

    public bool IsDraggingHeaderSelection => _isDraggingHeaderSelection;

    public ResultCellContext? SelectedCell => _selectedCell;

    public int SelectedCellSelectionCount => _selectedCells.Count;

    public bool IsDraggingSelection => _isDraggingSelection;
    public bool IsRowSelected(ResultRowViewItem row) => _selectedRows.Contains(row) || ReferenceEquals(_selectedRow, row);
    public Border BuildFixedActionHeader(double width, bool includeSubtitlePlaceholder)
    {
        return new Border
        {
            Width = width,
            Background = Brush.Parse("#F8FAFC"),
            BorderBrush = Brush.Parse("#D1D9E6"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = new StackPanel
            {
                Spacing = 2,
                Margin = new Thickness(6, 6),
                Children =
                {
                    new TextBlock
                    {
                        Text = " ",
                        FontWeight = FontWeight.SemiBold,
                        Opacity = 0
                    },
                    new TextBlock
                    {
                        Text = " ",
                        FontSize = 10,
                        Opacity = 0,
                        IsVisible = includeSubtitlePlaceholder
                    }
                }
            }
        };
    }

    public Border BuildScrollableHeaderRow(
        ResultSetViewItem resultSet,
        IReadOnlyList<double> columnWidths,
        Func<ResultColumnViewItem, string> headerTitleFormatter,
        Func<ResultColumnViewItem, string> headerSubtitleFormatter,
        Func<ResultColumnViewItem, string> headerTooltipFormatter,
        EventHandler<PointerPressedEventArgs> headerPressedHandler,
        Func<int, ContextMenu> headerContextMenuFactory)
    {
        IReadOnlyList<int> orderedColumns = GetOrderedDataColumnIndexes(resultSet);
        Grid headerGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", columnWidths.Select(width => $"{width}")))
        };

        for (int visualIndex = 0; visualIndex < orderedColumns.Count; visualIndex++)
        {
            int columnIndex = orderedColumns[visualIndex];
            ResultColumnViewItem column = resultSet.Columns[columnIndex];
            string subtitle = headerSubtitleFormatter(column);
            Border headerCell = new()
            {
                Tag = columnIndex,
                Background = resultSet.PinnedColumnIndex == columnIndex ? Brush.Parse("#E6F0FF") : Brush.Parse("#F3F6FB"),
                BorderBrush = Brush.Parse("#D1D9E6"),
                BorderThickness = new Thickness(0, 0, visualIndex == orderedColumns.Count - 1 ? 0 : 1, 1),
                Padding = new Thickness(6, 6),
                Child = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = headerTitleFormatter(column),
                            FontWeight = FontWeight.SemiBold,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            MaxLines = 1
                        },
                        new TextBlock
                        {
                            Text = subtitle,
                            FontSize = 10,
                            Foreground = Brush.Parse("#64748B"),
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            MaxLines = 1,
                            IsVisible = !string.IsNullOrWhiteSpace(subtitle)
                        }
                    }
                }
            };
            headerCell.AddHandler(InputElement.PointerPressedEvent, headerPressedHandler, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            headerCell.ContextMenu = headerContextMenuFactory(columnIndex);
            _headerBorders[columnIndex] = headerCell;
            if (resultSet.PinnedColumnIndex == columnIndex)
            {
                _pinnedHeaderColumns.Add(columnIndex);
            }

            ToolTip.SetTip(headerCell, headerTooltipFormatter(column));
            Grid.SetColumn(headerCell, visualIndex);
            headerGrid.Children.Add(headerCell);
        }

        return new Border
        {
            Background = Brush.Parse("#F8FAFC"),
            Child = headerGrid
        };
    }
    public Border BuildFixedActionRow(
        ResultRowViewItem row,
        double width,
        Func<ResultRowViewItem, ContextMenu> actionContextMenuFactory)
    {
        Border actionBorder = new()
        {
            Width = width,
            Background = Brush.Parse("#F8FAFC"),
            BorderBrush = Brush.Parse("#E5EAF1"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Height = 31,
            Child = new TextBlock
            {
                Text = _selectedRows.Contains(row) ? ">" : string.Empty,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush.Parse("#2563EB"),
                FontWeight = FontWeight.SemiBold
            },
            Tag = row,
            ContextMenu = actionContextMenuFactory(row)
        };
        RegisterActionBorder(row, actionBorder);
        return actionBorder;
    }

    public Border BuildScrollableDataRow(
        ResultRowViewItem row,
        ResultSetViewItem resultSet,
        IReadOnlyList<double> columnWidths,
        Func<ResultCellContext, ContextMenu> cellContextMenuFactory,
        EventHandler<TextChangedEventArgs>? editableCellTextChangedHandler)
    {
        IReadOnlyList<int> orderedColumns = GetOrderedDataColumnIndexes(resultSet);
        Grid rowGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", columnWidths.Select(width => $"{width}")))
        };

        for (int visualIndex = 0; visualIndex < orderedColumns.Count; visualIndex++)
        {
            int columnIndex = orderedColumns[visualIndex];
            string cellText = columnIndex < row.Values.Count ? row.Values[columnIndex] : string.Empty;
            ResultCellContext context = new()
            {
                Row = row,
                ColumnIndex = columnIndex
            };

            ResultColumnViewItem column = resultSet.Columns[columnIndex];
            Border cellBorder = new()
            {
                Tag = context,
                Focusable = false,
                BorderBrush = Brush.Parse("#E5EAF1"),
                BorderThickness = new Thickness(0, 0, visualIndex == orderedColumns.Count - 1 ? 0 : 1, 0),
                Height = 30,
                Background = resultSet.IsEditMode && column.IsEditable ? Brush.Parse("#FFFBEB") : Brushes.Transparent,
                Child = BuildDisplayCellContent(cellText),
                ContextMenu = cellContextMenuFactory(context)
            };
            ToolTip.SetTip(cellBorder, cellText);
            RegisterCellBorder(context, cellBorder);
            Grid.SetColumn(cellBorder, visualIndex);
            rowGrid.Children.Add(cellBorder);
        }

        return new Border
        {
            Tag = row,
            Background = Brush.Parse("#FFFFFF"),
            BorderBrush = Brush.Parse("#E5EAF1"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = rowGrid
        };
    }

    public bool TryGetCellBorder(ResultCellContext context, out Border? border)
    {
        return _cellBorders.TryGetValue((context.Row, context.ColumnIndex), out border);
    }

    public Control BuildDisplayCellContent(string cellText)
    {
        TextBlock cellTextBlock = new()
        {
            Text = cellText,
            Margin = new Thickness(6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };

        if (string.Equals(cellText, "(null)", StringComparison.OrdinalIgnoreCase))
        {
            cellTextBlock.Foreground = Brush.Parse("#94A3B8");
            cellTextBlock.FontStyle = FontStyle.Italic;
        }

        return cellTextBlock;
    }

    public TextBox BuildEditableCellEditor(
        ResultCellContext context,
        string cellText,
        EventHandler<TextChangedEventArgs> editableCellTextChangedHandler)
    {
        TextBox editor = new()
        {
            Text = string.Equals(cellText, "(null)", StringComparison.OrdinalIgnoreCase) ? string.Empty : cellText,
            Tag = context,
            Margin = new Thickness(4, 1),
            Padding = new Thickness(2, 0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        editor.TextChanged += editableCellTextChangedHandler;
        return editor;
    }
    public ContextMenu BuildHeaderContextMenu(
        int columnIndex,
        EventHandler<RoutedEventArgs> copyHeaderHandler,
        EventHandler<RoutedEventArgs> pinHeaderHandler)
    {
        MenuItem copyMenuItem = new()
        {
            Header = "复制列名",
            Tag = columnIndex
        };
        copyMenuItem.Click += copyHeaderHandler;

        MenuItem pinMenuItem = new()
        {
            Header = "固定当前列",
            Tag = columnIndex
        };
        pinMenuItem.Click += pinHeaderHandler;

        return new ContextMenu
        {
            ItemsSource = new object[] { copyMenuItem, pinMenuItem }
        };
    }
    public ContextMenu BuildActionContextMenu(
        ResultRowViewItem row,
        EventHandler<RoutedEventArgs> copyRowsHandler,
        EventHandler<RoutedEventArgs> copyInsertHandler)
    {
        MenuItem copyRowsMenuItem = new()
        {
            Header = "复制选中数据",
            Tag = row
        };
        copyRowsMenuItem.Click += copyRowsHandler;

        MenuItem copyInsertMenuItem = new()
        {
            Header = "复制为 INSERT 语句",
            Tag = row
        };
        copyInsertMenuItem.Click += copyInsertHandler;

        return new ContextMenu
        {
            ItemsSource = new object[] { copyRowsMenuItem, copyInsertMenuItem }
        };
    }
    public ContextMenu BuildActionContextMenu(
        ResultSetViewItem resultSet,
        ResultRowViewItem row,
        EventHandler<RoutedEventArgs> copyRowsHandler,
        EventHandler<RoutedEventArgs> copyRowsWithHeaderHandler,
        EventHandler<RoutedEventArgs> copyInsertHandler,
        EventHandler<RoutedEventArgs> exportCsvHandler,
        EventHandler<RoutedEventArgs> exportJsonHandler,
        EventHandler<RoutedEventArgs> deleteRowsHandler)
    {
        MenuItem copyRowsMenuItem = new()
        {
            Header = "复制选中数据",
            Tag = row
        };
        copyRowsMenuItem.Click += copyRowsHandler;

        MenuItem copyRowsWithHeaderMenuItem = new()
        {
            Header = "复制选中数据（含列名）",
            Tag = row
        };
        copyRowsWithHeaderMenuItem.Click += copyRowsWithHeaderHandler;

        MenuItem copyInsertMenuItem = new()
        {
            Header = "复制为 INSERT 语句",
            Tag = row
        };
        copyInsertMenuItem.Click += copyInsertHandler;

        MenuItem exportCsvMenuItem = new()
        {
            Header = "导出当前结果为 CSV",
            Tag = row
        };
        exportCsvMenuItem.Click += exportCsvHandler;

        MenuItem exportJsonMenuItem = new()
        {
            Header = "导出当前结果为 JSON",
            Tag = row
        };
        exportJsonMenuItem.Click += exportJsonHandler;

        MenuItem deleteRowsMenuItem = new()
        {
            Header = "删除",
            Tag = row,
            IsEnabled = resultSet.CanDeleteRows
        };
        deleteRowsMenuItem.Click += deleteRowsHandler;

        return new ContextMenu
        {
            ItemsSource = new object[]
            {
                copyRowsMenuItem,
                copyRowsWithHeaderMenuItem,
                copyInsertMenuItem,
                new Separator(),
                exportCsvMenuItem,
                exportJsonMenuItem,
                new Separator(),
                deleteRowsMenuItem
            }
        };
    }
    public ContextMenu BuildCellContextMenu(ResultCellContext context, EventHandler<RoutedEventArgs> copyCellHandler)
    {
        MenuItem copyCellMenuItem = new()
        {
            Header = "复制单元格",
            Tag = context
        };
        copyCellMenuItem.Click += copyCellHandler;

        return new ContextMenu
        {
            ItemsSource = new object[] { copyCellMenuItem }
        };
    }
    public void Reset()
    {
        _headerBorders.Clear();
        _pinnedHeaderColumns.Clear();
        _rowBorders.Clear();
        _actionBorders.Clear();
        _cellBorders.Clear();
        _selectedRows.Clear();
        _selectedCells.Clear();
        _selectedRow = null;
        _selectionAnchorRow = null;
        _cellSelectionAnchor = null;
        _selectedCell = null;
        _isDraggingSelection = false;
        _isDraggingCellSelection = false;
        _isDraggingHeaderSelection = false;
    }
    public bool TryGetHeaderColumnAtPoint(Visual relativeTo, Point point, out int columnIndex, bool ignoreVerticalBounds = false)
    {
        double bestDistance = double.MaxValue;
        int bestColumnIndex = -1;
        foreach ((int key, Border border) in _headerBorders)
        {
            if (!TryBuildHitRect(border, relativeTo, out Rect rect))
            {
                continue;
            }

            bool containsPoint = ignoreVerticalBounds
                ? point.X >= rect.Left && point.X <= rect.Right
                : rect.Contains(point);
            if (!containsPoint)
            {
                continue;
            }

            double distance = GetDistanceToRectCenter(rect, point);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestColumnIndex = key;
            }
        }

        columnIndex = bestColumnIndex;
        return bestColumnIndex >= 0;
    }
    public void RegisterRowBorder(ResultRowViewItem row, Border border)
    {
        _rowBorders[row] = border;
    }
    public void RegisterActionBorder(ResultRowViewItem row, Border border)
    {
        _actionBorders[row] = border;
    }
    public void RegisterCellBorder(ResultCellContext context, Border border)
    {
        _cellBorders[(context.Row, context.ColumnIndex)] = border;
    }
    public bool TryGetActionRowAtPoint(Visual relativeTo, Point point, out ResultRowViewItem? row)
    {
        double bestDistance = double.MaxValue;
        ResultRowViewItem? bestRow = null;
        foreach ((ResultRowViewItem key, Border border) in _actionBorders)
        {
            if (!TryBuildHitRect(border, relativeTo, out Rect rect) || !rect.Contains(point))
            {
                continue;
            }

            double distance = GetDistanceToRectCenter(rect, point);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestRow = key;
            }
        }

        row = bestRow;
        return bestRow != null;
    }
    public bool TryGetCellAtPoint(Visual relativeTo, Point point, out ResultCellContext? context)
    {
        double bestDistance = double.MaxValue;
        ResultCellContext? bestContext = null;
        foreach (((ResultRowViewItem row, int columnIndex) key, Border border) in _cellBorders)
        {
            if (!TryBuildHitRect(border, relativeTo, out Rect rect) || !rect.Contains(point))
            {
                continue;
            }

            double distance = GetDistanceToRectCenter(rect, point);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestContext = new ResultCellContext
                {
                    Row = key.Item1,
                    ColumnIndex = key.Item2
                };
            }
        }

        context = bestContext;
        return bestContext != null;
    }

    private static bool TryBuildHitRect(Visual visual, Visual relativeTo, out Rect rect)
    {
        Point? origin = visual.TranslatePoint(new Point(0, 0), relativeTo);
        if (origin == null)
        {
            rect = default;
            return false;
        }

        rect = new Rect(origin.Value, visual.Bounds.Size);
        return true;
    }

    private static double GetDistanceToRectCenter(Rect rect, Point point)
    {
        double dx = (rect.X + (rect.Width / 2.0)) - point.X;
        double dy = (rect.Y + (rect.Height / 2.0)) - point.Y;
        return (dx * dx) + (dy * dy);
    }
    public double[] BuildColumnWidths(
        ResultSetViewItem resultSet,
        IReadOnlyList<ResultRowViewItem> visibleRows,
        Func<ResultColumnViewItem, string> headerFormatter)
    {
        IReadOnlyList<int> orderedColumns = GetOrderedColumnIndexes(resultSet);
        double[] widths = new double[orderedColumns.Count];
        int sampleSize = Math.Min(visibleRows.Count, 40);

        for (int visualIndex = 0; visualIndex < orderedColumns.Count; visualIndex++)
        {
            int columnIndex = orderedColumns[visualIndex];
            int maxLength = columnIndex < 0 ? 0 : GetHeaderMeasurementLength(resultSet.Columns[columnIndex], headerFormatter);
            for (int rowIndex = 0; rowIndex < sampleSize; rowIndex++)
            {
                if (columnIndex >= 0 && columnIndex < visibleRows[rowIndex].Values.Count)
                {
                    maxLength = Math.Max(maxLength, visibleRows[rowIndex].Values[columnIndex]?.Length ?? 0);
                }
            }

            widths[visualIndex] = columnIndex < 0
                ? 14
                : Math.Clamp((maxLength * 7) + 16, 76, 210);
        }

        return widths;
    }
    private static int GetHeaderMeasurementLength(ResultColumnViewItem column, Func<ResultColumnViewItem, string> headerFormatter)
    {
        string rawName = string.IsNullOrWhiteSpace(column.RawName) ? column.HeaderText : column.RawName;
        int rawLength = Math.Min((rawName ?? string.Empty).Trim().Length, 18);
        int headerLength = Math.Min((headerFormatter(column) ?? string.Empty).Trim().Length, 18);
        return Math.Max(rawLength, headerLength);
    }
    public IReadOnlyList<int> GetOrderedColumnIndexes(ResultSetViewItem resultSet)
    {
        List<int> ordered = [-1];
        if (resultSet.PinnedColumnIndex.HasValue &&
            resultSet.PinnedColumnIndex.Value >= 0 &&
            resultSet.PinnedColumnIndex.Value < resultSet.Columns.Count)
        {
            ordered.Add(resultSet.PinnedColumnIndex.Value);
        }

        for (int i = 0; i < resultSet.Columns.Count; i++)
        {
            if (resultSet.PinnedColumnIndex == i)
            {
                continue;
            }

            ordered.Add(i);
        }

        return ordered;
    }
    public IReadOnlyList<int> GetOrderedDataColumnIndexes(ResultSetViewItem resultSet)
    {
        return GetOrderedColumnIndexes(resultSet)
            .Where(columnIndex => columnIndex >= 0)
            .ToArray();
    }

    // 行拖选和单元格/列拖选是互斥的，进入行选择时先清掉当前单元格选区。
    public void BeginSelection(ResultRowViewItem row)
    {
        _isDraggingSelection = true;
        _isDraggingHeaderSelection = false;
        ClearCellSelection();
        SelectSingleRow(row);
    }
    public void UpdateDragSelection(ResultSetViewItem resultSet, ResultRowViewItem row, bool isLeftButtonPressed)
    {
        if (!_isDraggingSelection || !isLeftButtonPressed)
        {
            return;
        }

        SelectRowRange(resultSet, row);
    }
    public void EndSelection()
    {
        _isDraggingSelection = false;
        _isDraggingCellSelection = false;
        _isDraggingHeaderSelection = false;
    }

    // 单元格拖选以起始单元格为锚点，后续拖动统一扩展成矩形区域。
    public void BeginCellSelection(ResultSetViewItem resultSet, ResultCellContext context)
    {
        _isDraggingCellSelection = true;
        _isDraggingHeaderSelection = false;
        _cellSelectionAnchor = context;
        _selectedCell = context;
        _selectedRows.Clear();
        _selectedRow = context.Row;
        _selectionAnchorRow = null;
        UpdateCellSelection(resultSet, context, isLeftButtonPressed: true);
    }
    public void UpdateCellSelection(ResultSetViewItem resultSet, ResultCellContext context, bool isLeftButtonPressed)
    {
        if (!_isDraggingCellSelection || !isLeftButtonPressed)
        {
            return;
        }

        _selectedCell = context;
        UpdateCellSelectionRange(resultSet, _cellSelectionAnchor ?? context, context);
    }

    // 表头拖选本质上复用单元格矩形选区，只是固定为“首行到末行”的整列范围。
    public void BeginHeaderSelection(ResultSetViewItem resultSet, int columnIndex)
    {
        _isDraggingSelection = false;
        _isDraggingCellSelection = false;
        _isDraggingHeaderSelection = true;
        SelectColumnRange(resultSet, columnIndex, columnIndex);
    }
    public void UpdateHeaderSelection(ResultSetViewItem resultSet, int columnIndex, bool isLeftButtonPressed)
    {
        if (!_isDraggingHeaderSelection || !isLeftButtonPressed)
        {
            return;
        }

        int anchorColumnIndex = _cellSelectionAnchor?.ColumnIndex ?? columnIndex;
        SelectColumnRange(resultSet, anchorColumnIndex, columnIndex);
    }
    public void SelectColumn(ResultSetViewItem resultSet, int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= resultSet.Columns.Count)
        {
            return;
        }

        _isDraggingSelection = false;
        _isDraggingCellSelection = false;
        _isDraggingHeaderSelection = false;
        SelectColumnRange(resultSet, columnIndex, columnIndex);
    }
    public void EnsureRowIncludedInSelection(ResultRowViewItem row)
    {
        ClearCellSelection();
        if (!_selectedRows.Contains(row))
        {
            SelectSingleRow(row);
        }
    }
    public void PrepareRowContextSelection(ResultRowViewItem row)
    {
        ClearCellSelection();
        if (_selectedRows.Count == 0 && _selectedRow == null)
        {
            SelectSingleRow(row);
            return;
        }

        if (!_selectedRows.Contains(row) && !ReferenceEquals(_selectedRow, row))
        {
            SelectSingleRow(row);
            return;
        }

        RefreshSelectionStyles();
    }
    public void SelectSingleRow(ResultRowViewItem row)
    {
        _selectedRow = row;
        _selectionAnchorRow = row;
        _selectedRows.Clear();
        _selectedRows.Add(row);
        RefreshSelectionStyles();
    }
    public void SelectAllRows(ResultSetViewItem resultSet)
    {
        IReadOnlyList<ResultRowViewItem> visibleRows = resultSet.GetViewRows();
        _isDraggingSelection = false;
        _isDraggingCellSelection = false;
        _isDraggingHeaderSelection = false;
        ClearCellSelection();
        _selectedRows.Clear();

        if (visibleRows.Count == 0)
        {
            _selectedRow = null;
            _selectionAnchorRow = null;
            RefreshSelectionStyles();
            return;
        }

        _selectionAnchorRow = visibleRows[0];
        _selectedRow = visibleRows[^1];
        foreach (ResultRowViewItem row in visibleRows)
        {
            _selectedRows.Add(row);
        }

        RefreshSelectionStyles();
    }
    public void SelectRowRange(ResultSetViewItem resultSet, ResultRowViewItem row)
    {
        _selectedRow = row;
        _selectionAnchorRow ??= row;
        IReadOnlyList<ResultRowViewItem> visibleRows = resultSet.GetViewRows();
        int anchorIndex = IndexOfRow(visibleRows, _selectionAnchorRow);
        int currentIndex = IndexOfRow(visibleRows, row);
        if (anchorIndex < 0 || currentIndex < 0)
        {
            SelectSingleRow(row);
            return;
        }

        int start = Math.Min(anchorIndex, currentIndex);
        int end = Math.Max(anchorIndex, currentIndex);
        _selectedRows.Clear();
        for (int index = start; index <= end; index++)
        {
            _selectedRows.Add(visibleRows[index]);
        }

        RefreshSelectionStyles();
    }
    public IReadOnlyList<ResultRowViewItem> GetEffectiveSelection(ResultRowViewItem? fallbackRow = null)
    {
        if (_selectedRows.Count > 0)
        {
            return _selectedRows.ToArray();
        }

        if (fallbackRow != null)
        {
            return [fallbackRow];
        }

        if (_selectedRow != null)
        {
            return [_selectedRow];
        }

        return Array.Empty<ResultRowViewItem>();
    }
    public string BuildSelectionClipboardText(
        ResultSetViewItem resultSet,
        bool includeHeader,
        ResultRowViewItem? fallbackRow = null)
    {
        IReadOnlyList<ResultRowViewItem> rows = GetOrderedEffectiveSelection(resultSet, fallbackRow);
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        string body = string.Join(Environment.NewLine, rows.Select(item => item.ToClipboardText(blankNulls: true)));
        return includeHeader
            ? resultSet.HeaderClipboardText + Environment.NewLine + body
            : body;
    }
    public string BuildSelectedCellClipboardText(ResultSetViewItem resultSet)
    {
        if (_selectedCells.Count == 0)
        {
            return string.Empty;
        }

        IReadOnlyList<ResultRowViewItem> visibleRows = resultSet.GetViewRows();
        Dictionary<ResultRowViewItem, int> rowOrder = visibleRows
            .Select((row, index) => new { row, index })
            .ToDictionary(item => item.row, item => item.index);

        List<(ResultRowViewItem Row, int ColumnIndex)> ordered = _selectedCells
            .Where(item => rowOrder.ContainsKey(item.Row))
            .OrderBy(item => rowOrder[item.Row])
            .ThenBy(item => item.ColumnIndex)
            .ToList();

        if (ordered.Count == 0)
        {
            return string.Empty;
        }

        int minRow = ordered.Min(item => rowOrder[item.Row]);
        int maxRow = ordered.Max(item => rowOrder[item.Row]);
        int minColumn = ordered.Min(item => item.ColumnIndex);
        int maxColumn = ordered.Max(item => item.ColumnIndex);

        List<string> lines = [];
        for (int rowIndex = minRow; rowIndex <= maxRow; rowIndex++)
        {
            ResultRowViewItem row = visibleRows[rowIndex];
            List<string> values = [];
            for (int columnIndex = minColumn; columnIndex <= maxColumn; columnIndex++)
            {
                if (_selectedCells.Contains((row, columnIndex)))
                {
                    string value = columnIndex < row.Values.Count ? row.Values[columnIndex] : string.Empty;
                    values.Add(string.Equals(value, "(null)", StringComparison.OrdinalIgnoreCase) ? string.Empty : value);
                }
                else
                {
                    values.Add(string.Empty);
                }
            }

            lines.Add(string.Join("\t", values));
        }

        return string.Join(Environment.NewLine, lines);
    }
    public string BuildCellClipboardText(ResultCellContext context)
    {
        if (context.ColumnIndex < 0 || context.ColumnIndex >= context.Row.Values.Count)
        {
            return string.Empty;
        }

        string value = context.Row.Values[context.ColumnIndex];
        return string.Equals(value, "(null)", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : value;
    }
    public bool TryBuildInsertScript(
        ResultSetViewItem resultSet,
        out string script,
        out string errorMessage,
        ResultRowViewItem? fallbackRow = null)
    {
        IReadOnlyList<ResultRowViewItem> rows = GetOrderedEffectiveSelection(resultSet, fallbackRow);
        return resultSet.TryBuildInsertScript(rows, out script, out errorMessage);
    }
    public string BuildFullResultClipboardText(
        ResultSetViewItem resultSet,
        bool includeHeader,
        Func<ResultColumnViewItem, string>? headerFormatter = null)
    {
        return resultSet.ToClipboardText(includeHeader, headerFormatter);
    }
    public string BuildCsv(ResultSetViewItem resultSet, Func<ResultColumnViewItem, string>? headerFormatter = null)
    {
        return resultSet.ToCsv(headerFormatter);
    }
    public string BuildJson(ResultSetViewItem resultSet, Func<ResultColumnViewItem, string>? headerFormatter = null)
    {
        return resultSet.ToJson(headerFormatter);
    }

    // 统一的矩形选区更新逻辑，单元格拖选和多列表头拖选都会走这条链。
    private void UpdateCellSelectionRange(ResultSetViewItem resultSet, ResultCellContext anchor, ResultCellContext current)
    {
        IReadOnlyList<ResultRowViewItem> visibleRows = resultSet.GetViewRows();
        int anchorRowIndex = IndexOfRow(visibleRows, anchor.Row);
        int currentRowIndex = IndexOfRow(visibleRows, current.Row);
        if (anchorRowIndex < 0 || currentRowIndex < 0)
        {
            return;
        }

        int startRow = Math.Min(anchorRowIndex, currentRowIndex);
        int endRow = Math.Max(anchorRowIndex, currentRowIndex);
        int startColumn = Math.Min(anchor.ColumnIndex, current.ColumnIndex);
        int endColumn = Math.Max(anchor.ColumnIndex, current.ColumnIndex);

        _selectedCells.Clear();
        for (int rowIndex = startRow; rowIndex <= endRow; rowIndex++)
        {
            ResultRowViewItem row = visibleRows[rowIndex];
            for (int columnIndex = startColumn; columnIndex <= endColumn; columnIndex++)
            {
                _selectedCells.Add((row, columnIndex));
            }
        }

        RefreshSelectionStyles();
    }
    private IReadOnlyList<ResultRowViewItem> GetOrderedEffectiveSelection(ResultSetViewItem resultSet, ResultRowViewItem? fallbackRow = null)
    {
        IReadOnlyList<ResultRowViewItem> selectedRows = GetEffectiveSelection(fallbackRow);
        if (selectedRows.Count <= 1)
        {
            return selectedRows;
        }

        HashSet<ResultRowViewItem> selectedSet = selectedRows.ToHashSet();
        ResultRowViewItem[] orderedRows = resultSet.GetViewRows()
            .Where(selectedSet.Contains)
            .ToArray();

        return orderedRows.Length > 0 ? orderedRows : selectedRows;
    }

    private void ClearCellSelection()
    {
        _selectedCells.Clear();
        _selectedCell = null;
        _cellSelectionAnchor = null;
        _isDraggingCellSelection = false;
        _isDraggingHeaderSelection = false;
    }

    // 所有可视选中效果都在这里集中刷新，避免不同交互入口各自改样式导致闪烁或状态打架。
    private void RefreshSelectionStyles()
    {
        HashSet<int> selectedHeaderColumns = _selectedCells
            .Select(item => item.ColumnIndex)
            .ToHashSet();

        foreach ((int columnIndex, Border border) in _headerBorders)
        {
            bool isSelected = selectedHeaderColumns.Contains(columnIndex);
            bool isPinned = _pinnedHeaderColumns.Contains(columnIndex);
            border.Background = isSelected
                ? Brush.Parse("#DCEBFF")
                : isPinned
                    ? Brush.Parse("#E6F0FF")
                    : Brush.Parse("#F3F6FB");
        }

        foreach ((ResultRowViewItem item, Border border) in _rowBorders)
        {
            bool isSelected = _selectedRows.Contains(item);
            border.Background = isSelected
                ? Brush.Parse("#EAF3FF")
                : Brush.Parse("#FFFFFF");
        }

        foreach ((ResultRowViewItem item, Border border) in _actionBorders)
        {
            bool isSelected = _selectedRows.Contains(item);
            border.Background = isSelected
                ? Brush.Parse("#EAF3FF")
                : Brush.Parse("#F8FAFC");

            if (border.Child is TextBlock marker)
            {
                marker.Text = isSelected ? ">" : string.Empty;
            }
        }

        foreach (((ResultRowViewItem Row, int ColumnIndex) key, Border border) in _cellBorders)
        {
            bool isSelected = _selectedCells.Contains(key);
            border.Background = isSelected
                ? Brush.Parse("#DCEBFF")
                : Brushes.Transparent;
        }
    }
    private void SelectColumnRange(ResultSetViewItem resultSet, int anchorColumnIndex, int currentColumnIndex)
    {
        if (anchorColumnIndex < 0 || anchorColumnIndex >= resultSet.Columns.Count ||
            currentColumnIndex < 0 || currentColumnIndex >= resultSet.Columns.Count)
        {
            return;
        }

        _selectedRows.Clear();
        _selectedRow = null;
        _selectionAnchorRow = null;
        _selectedCells.Clear();

        IReadOnlyList<ResultRowViewItem> visibleRows = resultSet.GetViewRows();
        if (visibleRows.Count == 0)
        {
            _cellSelectionAnchor = null;
            _selectedCell = null;
            RefreshSelectionStyles();
            return;
        }

        ResultCellContext anchor = new()
        {
            Row = visibleRows[0],
            ColumnIndex = anchorColumnIndex
        };
        ResultCellContext current = new()
        {
            Row = visibleRows[^1],
            ColumnIndex = currentColumnIndex
        };

        _cellSelectionAnchor = anchor;
        _selectedCell = current;
        UpdateCellSelectionRange(resultSet, anchor, current);
    }
    private static int IndexOfRow(IReadOnlyList<ResultRowViewItem> rows, ResultRowViewItem target)
    {
        for (int index = 0; index < rows.Count; index++)
        {
            if (ReferenceEquals(rows[index], target))
            {
                return index;
            }
        }

        return -1;
    }
}

