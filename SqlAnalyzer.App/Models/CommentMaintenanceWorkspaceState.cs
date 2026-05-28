using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Models;
public sealed class CommentMaintenanceWorkspaceState : ObservableObject
{
    private bool _onlyEmpty;
    private bool _onlyChanged;
    private string _tableKeyword = string.Empty;
    private string _columnKeyword = string.Empty;
    private string _selectedTableFilter = string.Empty;
    private CommentMaintenanceTableItem? _selectedTableItem;
    private string _lastOperationSummary = string.Empty;
    private bool _lastOperationHasIssues;
    private bool _isBusy;

    public string ConnectionProfileId { get; private set; } = string.Empty;

    public string ConnectionName { get; private set; } = string.Empty;

    public string ProviderName { get; private set; } = string.Empty;

    public string SchemaName { get; private set; } = string.Empty;

    public DateTimeOffset LoadedAt { get; private set; }

    public ObservableCollection<CommentMaintenanceTableItem> Tables { get; } = new ResettableObservableCollection<CommentMaintenanceTableItem>();

    public ObservableCollection<CommentMaintenanceColumnItem> Columns { get; } = new ResettableObservableCollection<CommentMaintenanceColumnItem>();

    public ObservableCollection<CommentMaintenanceTableItem> FilteredTables { get; } = new ResettableObservableCollection<CommentMaintenanceTableItem>();

    public ObservableCollection<CommentMaintenanceColumnItem> FilteredColumns { get; } = new ResettableObservableCollection<CommentMaintenanceColumnItem>();

    public ObservableCollection<string> TableFilterOptions { get; } = new ResettableObservableCollection<string>();

    public bool IsLoaded => LoadedAt != default;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string LastOperationSummary => _lastOperationSummary;

    public bool HasOperationSummary => !string.IsNullOrWhiteSpace(_lastOperationSummary);

    public bool LastOperationHasIssues => _lastOperationHasIssues;

    public bool OnlyEmpty
    {
        get => _onlyEmpty;
        set
        {
            if (SetProperty(ref _onlyEmpty, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool OnlyChanged
    {
        get => _onlyChanged;
        set
        {
            if (SetProperty(ref _onlyChanged, value))
            {
                ApplyFilters();
            }
        }
    }

    public string TableKeyword
    {
        get => _tableKeyword;
        set
        {
            if (SetProperty(ref _tableKeyword, value))
            {
                ApplyFilters();
            }
        }
    }

    public string ColumnKeyword
    {
        get => _columnKeyword;
        set
        {
            if (SetProperty(ref _columnKeyword, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedTableFilter
    {
        get => _selectedTableFilter;
        set
        {
            if (SetProperty(ref _selectedTableFilter, value))
            {
                SyncSelectedTableItem();
                ApplyFilters();
            }
        }
    }

    public CommentMaintenanceTableItem? SelectedTableItem
    {
        get => _selectedTableItem;
        set
        {
            if (SetProperty(ref _selectedTableItem, value))
            {
                string nextFilter = value?.ObjectName ?? string.Empty;
                if (!string.Equals(_selectedTableFilter, nextFilter, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedTableFilter = nextFilter;
                    OnPropertyChanged(nameof(SelectedTableFilter));
                    ApplyFilters();
                }
            }
        }
    }

    public int TableCount => Tables.Count;

    public int ColumnCount => Columns.Count;

    public int ChangedCount => Tables.Count(item => item.IsDirty) + Columns.Count(item => item.IsDirty);

    public bool HasChanges => ChangedCount > 0;
    public void Load(CommentMaintenanceWorkspace workspace)
    {
        UnsubscribeAll();

        ConnectionProfileId = workspace.ConnectionProfileId;
        ConnectionName = workspace.ConnectionName;
        ProviderName = workspace.ProviderName;
        SchemaName = workspace.SchemaName;
        LoadedAt = workspace.LoadedAt;

        CommentMaintenanceTableItem[] tableItems = workspace.Tables
            .Select(table =>
            {
                CommentMaintenanceTableItem item = new(table);
                item.PropertyChanged += WorkspaceItem_PropertyChanged;
                return item;
            })
            .ToArray();
        CommentMaintenanceColumnItem[] columnItems = workspace.Columns
            .Select(column =>
            {
                CommentMaintenanceColumnItem item = new(column);
                item.PropertyChanged += WorkspaceItem_PropertyChanged;
                return item;
            })
            .ToArray();

        ReplaceCollection(Tables, tableItems);
        ReplaceCollection(Columns, columnItems);
        ReplaceCollection(FilteredTables, Array.Empty<CommentMaintenanceTableItem>());
        ReplaceCollection(FilteredColumns, Array.Empty<CommentMaintenanceColumnItem>());

        List<string> tableFilterOptions = [string.Empty];
        tableFilterOptions.AddRange(tableItems
            .Select(item => item.ObjectName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
        ReplaceCollection(TableFilterOptions, tableFilterOptions);

        if (!tableFilterOptions.Any(item => string.Equals(item, SelectedTableFilter, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedTableFilter = tableItems
                .OrderBy(item => item.ObjectName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()?.ObjectName ?? string.Empty;
            OnPropertyChanged(nameof(SelectedTableFilter));
        }

        SyncSelectedTableItem();
        ApplyFilters();
        OnPropertyChanged(nameof(IsLoaded));
    }
    public CommentMaintenanceWorkspace ToWorkspace()
    {
        return new CommentMaintenanceWorkspace
        {
            ConnectionProfileId = ConnectionProfileId,
            ConnectionName = ConnectionName,
            ProviderName = ProviderName,
            SchemaName = SchemaName,
            LoadedAt = LoadedAt,
            Tables = Tables.Select(item => item.ToEntry()).ToArray(),
            Columns = Columns.Select(item => item.ToEntry()).ToArray()
        };
    }
    public void ClearFilters()
    {
        _onlyEmpty = false;
        _onlyChanged = false;
        _tableKeyword = string.Empty;
        _columnKeyword = string.Empty;
        _selectedTableFilter = string.Empty;
        _selectedTableItem = null;

        OnPropertyChanged(nameof(OnlyEmpty));
        OnPropertyChanged(nameof(OnlyChanged));
        OnPropertyChanged(nameof(TableKeyword));
        OnPropertyChanged(nameof(ColumnKeyword));
        OnPropertyChanged(nameof(SelectedTableFilter));
        OnPropertyChanged(nameof(SelectedTableItem));
        ApplyFilters();
    }
    public void SetLoadSummary()
    {
        SetOperationSummary($"已加载 {SchemaName} 模式：{Tables.Count} 个对象，{Columns.Count} 个字段。", hasIssues: false);
    }
    public void SetImportSummary(CommentImportResult result)
    {
        string summary = $"CSV 导入完成：新增 {result.ImportedCount} 项，更新 {result.UpdatedCount} 项，跳过 {result.SkippedCount} 项";
        if (result.Errors.Count > 0)
        {
            string preview = string.Join("；", result.Errors.Take(3).Select(item => $"第{item.RowNumber}行 {item.Message}"));
            summary = $"{summary}，失败 {result.Errors.Count} 行。{preview}";
            if (result.Errors.Count > 3)
            {
                summary += " 等。";
            }
        }
        else
        {
            summary += "。";
        }

        SetOperationSummary(summary, result.Errors.Count > 0);
    }
    public void SetExportSummary(string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        SetOperationSummary($"已导出 {Tables.Count} 个对象和 {Columns.Count} 个字段到 {fileName}。", hasIssues: false);
    }
    public void SetPreviewSummary(int sqlCount)
    {
        SetOperationSummary(
            sqlCount <= 0 ? "当前没有需要预览的注释更新 SQL。" : $"已生成 {sqlCount} 条注释更新 SQL 预览。",
            hasIssues: false);
    }
    public void SetApplySummary(int sqlCount)
    {
        SetOperationSummary(
            sqlCount <= 0 ? "没有检测到需要执行的注释更新。" : $"已执行 {sqlCount} 条注释更新 SQL，并刷新对象树、补全与结果列注释。",
            hasIssues: false);
    }
    public void ClearOperationSummary()
    {
        SetOperationSummary(string.Empty, hasIssues: false);
    }
    public void ApplyFilters()
    {
        List<CommentMaintenanceTableItem> tableItems = Tables
            .Where(MatchesTableFilter)
            .OrderBy(item => item.ObjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ReplaceCollection(FilteredTables, tableItems);

        List<CommentMaintenanceColumnItem> columnItems = Columns
            .Where(MatchesColumnFilter)
            .OrderBy(item => item.TableName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ColumnName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ReplaceCollection(FilteredColumns, columnItems);

        OnPropertyChanged(nameof(TableCount));
        OnPropertyChanged(nameof(ColumnCount));
        OnPropertyChanged(nameof(ChangedCount));
        OnPropertyChanged(nameof(HasChanges));
    }
    private void UnsubscribeAll()
    {
        foreach (CommentMaintenanceTableItem table in Tables)
        {
            table.PropertyChanged -= WorkspaceItem_PropertyChanged;
        }

        foreach (CommentMaintenanceColumnItem column in Columns)
        {
            column.PropertyChanged -= WorkspaceItem_PropertyChanged;
        }
    }
    private void WorkspaceItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommentMaintenanceTableItem.EditedComment) ||
            e.PropertyName == nameof(CommentMaintenanceColumnItem.EditedComment))
        {
            OnPropertyChanged(nameof(ChangedCount));
            OnPropertyChanged(nameof(HasChanges));

            bool shouldRefreshFilteredView = OnlyChanged ||
                                             (sender is CommentMaintenanceTableItem && !string.IsNullOrWhiteSpace(TableKeyword)) ||
                                             (sender is CommentMaintenanceColumnItem && !string.IsNullOrWhiteSpace(ColumnKeyword));
            if (shouldRefreshFilteredView)
            {
                ApplyFilters();
            }
        }
    }
    private bool MatchesTableFilter(CommentMaintenanceTableItem item)
    {
        if (OnlyEmpty && !string.IsNullOrWhiteSpace(item.CurrentComment))
        {
            return false;
        }

        if (OnlyChanged && !item.IsDirty)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(TableKeyword))
        {
            string keyword = TableKeyword.Trim();
            if (!item.ObjectName.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                !item.CurrentComment.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                !item.EditedComment.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
    private bool MatchesColumnFilter(CommentMaintenanceColumnItem item)
    {
        if (OnlyEmpty && !string.IsNullOrWhiteSpace(item.CurrentComment))
        {
            return false;
        }

        if (OnlyChanged && !item.IsDirty)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SelectedTableFilter))
        {
            if (string.IsNullOrWhiteSpace(ColumnKeyword) && !OnlyEmpty && !OnlyChanged)
            {
                return false;
            }
        }
        else if (!string.Equals(item.TableName, SelectedTableFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ColumnKeyword))
        {
            string keyword = ColumnKeyword.Trim();
            if (!item.TableName.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                !item.ColumnName.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                !item.CurrentComment.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                !item.EditedComment.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
    private static void ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        if (target is ResettableObservableCollection<T> resettable)
        {
            resettable.ReplaceAll(items);
            return;
        }

        target.Clear();
        foreach (T item in items)
        {
            target.Add(item);
        }
    }
    private void SetOperationSummary(string summary, bool hasIssues)
    {
        if (!string.Equals(_lastOperationSummary, summary, StringComparison.Ordinal))
        {
            _lastOperationSummary = summary;
            OnPropertyChanged(nameof(LastOperationSummary));
            OnPropertyChanged(nameof(HasOperationSummary));
        }

        if (_lastOperationHasIssues != hasIssues)
        {
            _lastOperationHasIssues = hasIssues;
            OnPropertyChanged(nameof(LastOperationHasIssues));
        }
    }
    private void SyncSelectedTableItem()
    {
        CommentMaintenanceTableItem? next = string.IsNullOrWhiteSpace(_selectedTableFilter)
            ? null
            : Tables.FirstOrDefault(item => string.Equals(item.ObjectName, _selectedTableFilter, StringComparison.OrdinalIgnoreCase));

        if (!ReferenceEquals(_selectedTableItem, next))
        {
            _selectedTableItem = next;
            OnPropertyChanged(nameof(SelectedTableItem));
        }
    }
}
internal sealed class ResettableObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (T item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
public sealed class CommentMaintenanceTableItem : ObservableObject
{
    private string _editedComment = string.Empty;

    public CommentMaintenanceTableItem(CommentMaintenanceTableEntry entry)
    {
        SchemaName = entry.SchemaName;
        ObjectName = entry.ObjectName;
        ObjectType = entry.ObjectType;
        CurrentComment = entry.CurrentComment ?? string.Empty;
        _editedComment = entry.EditedComment ?? entry.CurrentComment ?? string.Empty;
    }

    public string SchemaName { get; }

    public string ObjectName { get; }

    public string ObjectType { get; }

    public string CurrentComment { get; }

    public string EditedComment
    {
        get => _editedComment;
        set
        {
            if (SetProperty(ref _editedComment, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsDirty));
            }
        }
    }

    public CommentChangeStatus Status => ResolveStatus(CurrentComment, EditedComment);

    public string StatusText => Status switch
    {
        CommentChangeStatus.Added => "新增",
        CommentChangeStatus.Updated => "修改",
        CommentChangeStatus.Cleared => "清空",
        CommentChangeStatus.ImportFailed => "失败",
        _ => "未变更"
    };

    public bool IsDirty => Status != CommentChangeStatus.Unchanged;
    public CommentMaintenanceTableEntry ToEntry()
    {
        return new CommentMaintenanceTableEntry
        {
            SchemaName = SchemaName,
            ObjectName = ObjectName,
            ObjectType = ObjectType,
            CurrentComment = CurrentComment,
            EditedComment = EditedComment
        };
    }
    private static CommentChangeStatus ResolveStatus(string currentComment, string editedComment)
    {
        string current = currentComment?.Trim() ?? string.Empty;
        string edited = editedComment?.Trim() ?? string.Empty;
        if (string.Equals(current, edited, StringComparison.Ordinal))
        {
            return CommentChangeStatus.Unchanged;
        }

        if (string.IsNullOrWhiteSpace(current) && !string.IsNullOrWhiteSpace(edited))
        {
            return CommentChangeStatus.Added;
        }

        if (!string.IsNullOrWhiteSpace(current) && string.IsNullOrWhiteSpace(edited))
        {
            return CommentChangeStatus.Cleared;
        }

        return CommentChangeStatus.Updated;
    }
}
public sealed class CommentMaintenanceColumnItem : ObservableObject
{
    private string _editedComment = string.Empty;

    public CommentMaintenanceColumnItem(CommentMaintenanceColumnEntry entry)
    {
        SchemaName = entry.SchemaName;
        TableName = entry.TableName;
        ObjectType = entry.ObjectType;
        ColumnName = entry.ColumnName;
        DataType = entry.DataType;
        FullTypeDefinition = entry.FullTypeDefinition;
        IsNullable = entry.IsNullable;
        DefaultValue = entry.DefaultValue;
        IsIdentity = entry.IsIdentity;
        IsComputed = entry.IsComputed;
        ExtraDefinition = entry.ExtraDefinition;
        CurrentComment = entry.CurrentComment ?? string.Empty;
        _editedComment = entry.EditedComment ?? entry.CurrentComment ?? string.Empty;
    }

    public string SchemaName { get; }

    public string TableName { get; }

    public string ObjectType { get; }

    public string ColumnName { get; }

    public string DataType { get; }

    public string FullTypeDefinition { get; }

    public bool IsNullable { get; }

    public string DefaultValue { get; }

    public bool IsIdentity { get; }

    public bool IsComputed { get; }

    public string ExtraDefinition { get; }

    public string CurrentComment { get; }

    public string EditedComment
    {
        get => _editedComment;
        set
        {
            if (SetProperty(ref _editedComment, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsDirty));
            }
        }
    }

    public CommentChangeStatus Status => ResolveStatus(CurrentComment, EditedComment);

    public string StatusText => Status switch
    {
        CommentChangeStatus.Added => "新增",
        CommentChangeStatus.Updated => "修改",
        CommentChangeStatus.Cleared => "清空",
        CommentChangeStatus.ImportFailed => "失败",
        _ => "未变更"
    };

    public bool IsDirty => Status != CommentChangeStatus.Unchanged;
    public CommentMaintenanceColumnEntry ToEntry()
    {
        return new CommentMaintenanceColumnEntry
        {
            SchemaName = SchemaName,
            TableName = TableName,
            ObjectType = ObjectType,
            ColumnName = ColumnName,
            DataType = DataType,
            FullTypeDefinition = FullTypeDefinition,
            IsNullable = IsNullable,
            DefaultValue = DefaultValue,
            IsIdentity = IsIdentity,
            IsComputed = IsComputed,
            ExtraDefinition = ExtraDefinition,
            CurrentComment = CurrentComment,
            EditedComment = EditedComment
        };
    }
    private static CommentChangeStatus ResolveStatus(string currentComment, string editedComment)
    {
        string current = currentComment?.Trim() ?? string.Empty;
        string edited = editedComment?.Trim() ?? string.Empty;
        if (string.Equals(current, edited, StringComparison.Ordinal))
        {
            return CommentChangeStatus.Unchanged;
        }

        if (string.IsNullOrWhiteSpace(current) && !string.IsNullOrWhiteSpace(edited))
        {
            return CommentChangeStatus.Added;
        }

        if (!string.IsNullOrWhiteSpace(current) && string.IsNullOrWhiteSpace(edited))
        {
            return CommentChangeStatus.Cleared;
        }

        return CommentChangeStatus.Updated;
    }
}
