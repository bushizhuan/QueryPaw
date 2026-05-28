using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Models;

public sealed class ModelDiagramWorkspaceState : ObservableObject
{
    private bool _onlyRelatedTables = true;
    private bool _onlyNeighborhood;
    private bool _showColumns;
    private string _searchKeyword = string.Empty;
    private ModelTableNode? _selectedTable;
    private ModelDiagramRelationState? _selectedRelation;
    private string _messageText = string.Empty;
    private int _neighborhoodDepth = 1;
    private double _zoom = 1d;
    private int _renderVersion;
    private readonly Dictionary<string, (double X, double Y)> _nodePositionOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _tableSearchIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _parentTableLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _childTableLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _referenceTextLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _referencedByTextLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<ModelRelationEdge>> _relationsByTableLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _searchDebounceTimer;
    private string _selectedTableDetailsKey = string.Empty;
    private string _selectedRelationDetailsKey = string.Empty;
    private string _pendingViewportCenterTableName = string.Empty;
    private string _neighborhoodCacheTableName = string.Empty;
    private int _neighborhoodCacheDepth;
    private HashSet<string> _neighborhoodCacheNames = new(StringComparer.OrdinalIgnoreCase);
    private const int LargeModeThreshold = 300;
    public ModelDiagramWorkspaceState()
    {
        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(260)
        };
        _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
    }

    public string ConnectionProfileId { get; private set; } = string.Empty;

    public string ConnectionName { get; private set; } = string.Empty;

    public string ProviderName { get; private set; } = string.Empty;

    public string SchemaName { get; private set; } = string.Empty;

    public DateTimeOffset LoadedAt { get; private set; }

    public ObservableCollection<ModelTableNode> Tables { get; } = [];

    public ObservableCollection<ModelRelationEdge> Relations { get; } = [];

    public ObservableCollection<ModelTableNode> FilteredTables { get; } = [];

    public ObservableCollection<ModelDiagramNodeState> VisibleNodes { get; } = [];

    public ObservableCollection<ModelDiagramRelationState> VisibleRelations { get; } = [];

    public ObservableCollection<ModelColumnNode> SelectedTableColumns { get; } = [];

    public ObservableCollection<string> SelectedTableReferenceTexts { get; } = [];

    public ObservableCollection<string> SelectedTableReferencedByTexts { get; } = [];

    public bool IsLoaded => LoadedAt != default;

    public bool OnlyRelatedTables
    {
        get => _onlyRelatedTables;
        set
        {
            if (SetProperty(ref _onlyRelatedTables, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool OnlyNeighborhood
    {
        get => _onlyNeighborhood;
        set
        {
            if (SetProperty(ref _onlyNeighborhood, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool ShowColumns
    {
        get => _showColumns;
        set
        {
            if (SetProperty(ref _showColumns, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetProperty(ref _searchKeyword, value ?? string.Empty))
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            }
        }
    }

    public ModelTableNode? SelectedTable
    {
        get => _selectedTable;
        set
        {
            if (SetProperty(ref _selectedTable, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedTableName => SelectedTable?.TableName ?? string.Empty;

    public string SelectedTableCommentText => SelectedTable?.CommentText ?? string.Empty;

    public string SelectedTablePrimaryKeysText =>
        SelectedTable == null
            ? string.Empty
            : (SelectedTable.PrimaryKeyColumns.Count == 0
                ? "无主键"
                : string.Join(", ", SelectedTable.PrimaryKeyColumns));

    public ModelDiagramRelationState? SelectedRelation
    {
        get => _selectedRelation;
        set
        {
            if (SetProperty(ref _selectedRelation, value))
            {
                UpdateRelationSelectionState();
                TouchRenderVersion();
            }
        }
    }

    public string SelectedRelationConstraintName => SelectedRelation?.ConstraintName ?? string.Empty;

    public string SelectedRelationSummaryText => SelectedRelation?.SummaryText ?? string.Empty;

    public string SelectedRelationParentColumnsText => SelectedRelation?.ParentColumnsText ?? string.Empty;

    public string SelectedRelationChildColumnsText => SelectedRelation?.ChildColumnsText ?? string.Empty;

    public bool IsLargeMode => Tables.Count > LargeModeThreshold;

    public string MessageText
    {
        get => _messageText;
        private set => SetProperty(ref _messageText, value);
    }

    public int NeighborhoodDepth
    {
        get => _neighborhoodDepth;
        private set => SetProperty(ref _neighborhoodDepth, Math.Max(1, value));
    }

    public double Zoom
    {
        get => _zoom;
        private set
        {
            double normalized = Math.Clamp(value, 0.6d, 2.0d);
            if (SetProperty(ref _zoom, normalized))
            {
                TouchRenderVersion();
                OnPropertyChanged(nameof(ZoomText));
                OnPropertyChanged(nameof(CanvasWidth));
                OnPropertyChanged(nameof(CanvasHeight));
            }
        }
    }

    public int RenderVersion
    {
        get => _renderVersion;
        private set => SetProperty(ref _renderVersion, value);
    }

    public string ZoomText => $"{Math.Round(Zoom * 100d):0}%";

    public double CanvasWidth { get; private set; } = 1200d;

    public double CanvasHeight { get; private set; } = 720d;
    public void Load(ModelDiagramWorkspace workspace, string focusTableName = "")
    {
        Tables.Clear();
        Relations.Clear();
        FilteredTables.Clear();
        VisibleNodes.Clear();
        VisibleRelations.Clear();
        SelectedTableColumns.Clear();
        SelectedTableReferenceTexts.Clear();
        SelectedTableReferencedByTexts.Clear();

        ConnectionProfileId = workspace.ConnectionProfileId;
        ConnectionName = workspace.ConnectionName;
        ProviderName = workspace.ProviderName;
        SchemaName = workspace.SchemaName;
        LoadedAt = workspace.LoadedAt;
        _nodePositionOverrides.Clear();
        _tableSearchIndex.Clear();
        _parentTableLookup.Clear();
        _childTableLookup.Clear();
        _referenceTextLookup.Clear();
        _referencedByTextLookup.Clear();
        _relationsByTableLookup.Clear();
        _selectedTableDetailsKey = string.Empty;
        _selectedRelationDetailsKey = string.Empty;
        _pendingViewportCenterTableName = string.Empty;
        _neighborhoodCacheTableName = string.Empty;
        _neighborhoodCacheDepth = 0;
        _neighborhoodCacheNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        NeighborhoodDepth = 1;
        Zoom = 1d;
        SelectedRelation = null;
        _searchDebounceTimer.Stop();

        foreach (ModelTableNode table in workspace.Tables.OrderBy(item => item.TableName, StringComparer.OrdinalIgnoreCase))
        {
            Tables.Add(table);
            _tableSearchIndex[table.TableName] = BuildTableSearchIndex(table);
        }

        foreach (ModelRelationEdge relation in workspace.Relations)
        {
            Relations.Add(relation);
        }

        BuildRelationLookups();

        if (!string.IsNullOrWhiteSpace(focusTableName))
        {
            SelectedTable = Tables.FirstOrDefault(item => string.Equals(item.TableName, focusTableName, StringComparison.OrdinalIgnoreCase));
            OnlyNeighborhood = SelectedTable != null;
            if (SelectedTable != null)
            {
                QueueViewportCenter(SelectedTable.TableName);
            }
        }
        else if (IsLargeMode)
        {
            SelectedTable = PickLargeModeSeedTable();
            OnlyNeighborhood = SelectedTable != null;
            if (SelectedTable != null)
            {
                QueueViewportCenter(SelectedTable.TableName);
            }
        }
        else if (SelectedTable != null)
        {
            SelectedTable = Tables.FirstOrDefault(item => string.Equals(item.TableName, SelectedTable.TableName, StringComparison.OrdinalIgnoreCase));
        }

        ApplyFilters();
        SetMessageText(IsLargeMode
            ? $"已加载 {SchemaName} 模式：{Tables.Count} 张表，{Relations.Count} 条关系。当前模式较大，已默认聚焦局部关系，建议优先使用搜索或“仅当前表上下游”。"
            : $"已加载 {SchemaName} 模式：{Tables.Count} 张表，{Relations.Count} 条关系。");
        OnPropertyChanged(nameof(IsLoaded));
    }
    public void FocusTable(string tableName)
    {
        ModelTableNode? target = Tables.FirstOrDefault(item => string.Equals(item.TableName, tableName, StringComparison.OrdinalIgnoreCase));
        if (target != null)
        {
            SelectedTable = target;
            QueueViewportCenter(target.TableName);
        }
    }
    public void ReloadLayout()
    {
        _nodePositionOverrides.Clear();
        ApplyFilters();
    }
    public void FocusRelation(string constraintName, string fromTable, string toTable)
    {
        ModelDiagramRelationState? relation = VisibleRelations.FirstOrDefault(item =>
            string.Equals(item.ConstraintName, constraintName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.FromTable, fromTable, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ToTable, toTable, StringComparison.OrdinalIgnoreCase));
        if (relation != null)
        {
            SelectedRelation = relation;
        }
    }
    public void ExpandNeighborhood()
    {
        NeighborhoodDepth++;
        OnlyNeighborhood = true;
        ApplyFilters();
    }
    public void MoveNode(string tableName, double deltaX, double deltaY)
    {
        ModelDiagramNodeState? node = VisibleNodes.FirstOrDefault(item => string.Equals(item.TableName, tableName, StringComparison.OrdinalIgnoreCase));
        if (node == null)
        {
            return;
        }

        node.X = Math.Max(20d, node.X + deltaX);
        node.Y = Math.Max(20d, node.Y + deltaY);
        _nodePositionOverrides[tableName] = (node.X, node.Y);
        ReplaceCollection(VisibleRelations, BuildRelationStates(VisibleNodes.ToDictionary(item => item.TableName, StringComparer.OrdinalIgnoreCase)));
        RecalculateCanvasBounds(VisibleNodes);
        TouchRenderVersion();
        OnPropertyChanged(nameof(VisibleNodes));
        OnPropertyChanged(nameof(VisibleRelations));
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
    }
    public void ResetNeighborhood()
    {
        NeighborhoodDepth = 1;
        OnlyNeighborhood = SelectedTable != null;
        ApplyFilters();
    }
    public void ZoomIn()
    {
        Zoom += 0.2d;
    }
    public void ZoomOut()
    {
        Zoom -= 0.2d;
    }
    public void ResetZoom()
    {
        Zoom = 1d;
    }
    public void SetMessageText(string message)
    {
        MessageText = message ?? string.Empty;
    }

    private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        ApplyFilters();
    }
    public ModelDiagramWorkspace ToWorkspace()
    {
        return new ModelDiagramWorkspace
        {
            ProviderName = ProviderName,
            ConnectionProfileId = ConnectionProfileId,
            ConnectionName = ConnectionName,
            SchemaName = SchemaName,
            LoadedAt = LoadedAt,
            Tables = Tables.ToArray(),
            Relations = Relations.ToArray()
        };
    }
    private void ApplyFilters()
    {
        IEnumerable<ModelTableNode> query = Tables;
        if (OnlyRelatedTables)
        {
            query = query.Where(item => item.ReferencesCount > 0 || item.ReferencedByCount > 0 || item.ForeignKeyCount > 0);
        }

        if (!string.IsNullOrWhiteSpace(SearchKeyword))
        {
            string keyword = SearchKeyword.Trim();
            query = query.Where(item =>
                _tableSearchIndex.TryGetValue(item.TableName, out string? searchText) &&
                searchText.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        List<ModelTableNode> filtered = query
            .OrderBy(item => item.TableName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        HashSet<string> searchMatchedNames = filtered
            .Select(item => item.TableName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        ReplaceCollection(FilteredTables, filtered);

        if (SelectedTable != null &&
            !Tables.Any(item => string.Equals(item.TableName, SelectedTable.TableName, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedTable = null;
            OnPropertyChanged(nameof(SelectedTable));
        }

        if (!string.IsNullOrWhiteSpace(SearchKeyword) &&
            filtered.Count > 0 &&
            (SelectedTable == null || !searchMatchedNames.Contains(SelectedTable.TableName)))
        {
            _selectedTable = filtered[0];
            QueueViewportCenter(_selectedTable.TableName);
            OnPropertyChanged(nameof(SelectedTable));
        }

        HashSet<string> visibleNames = filtered
            .Select(item => item.TableName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (OnlyNeighborhood && SelectedTable != null)
        {
            visibleNames = BuildNeighborhoodNames(SelectedTable.TableName, NeighborhoodDepth);
            visibleNames.IntersectWith(filtered.Select(item => item.TableName));
        }

        if (SelectedTable != null &&
            (string.IsNullOrWhiteSpace(SearchKeyword) || searchMatchedNames.Contains(SelectedTable.TableName)))
        {
            visibleNames.Add(SelectedTable.TableName);
        }

        List<ModelTableNode> visibleTables = filtered
            .Where(item => visibleNames.Contains(item.TableName))
            .OrderBy(item => item.TableName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Dictionary<string, ModelDiagramNodeState> nodeLookup = BuildNodeStates(visibleTables, searchMatchedNames);
        ReplaceCollection(VisibleNodes, nodeLookup.Values.OrderBy(item => item.Y).ThenBy(item => item.X).ToList());
        ReplaceCollection(VisibleRelations, BuildRelationStates(nodeLookup));
        if (SelectedRelation != null)
        {
            SelectedRelation = VisibleRelations.FirstOrDefault(item =>
                string.Equals(item.ConstraintName, SelectedRelation.ConstraintName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.FromTable, SelectedRelation.FromTable, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ToTable, SelectedRelation.ToTable, StringComparison.OrdinalIgnoreCase));
        }
        bool tableDetailsChanged = RefreshSelectedTableDetails();
        bool relationDetailsChanged = RefreshSelectedRelationDetails();
        OnPropertyChanged(nameof(FilteredTables));
        OnPropertyChanged(nameof(VisibleNodes));
        OnPropertyChanged(nameof(VisibleRelations));
        if (tableDetailsChanged)
        {
            OnPropertyChanged(nameof(SelectedTableName));
            OnPropertyChanged(nameof(SelectedTableCommentText));
            OnPropertyChanged(nameof(SelectedTablePrimaryKeysText));
            OnPropertyChanged(nameof(SelectedTableColumns));
            OnPropertyChanged(nameof(SelectedTableReferenceTexts));
            OnPropertyChanged(nameof(SelectedTableReferencedByTexts));
        }

        if (relationDetailsChanged)
        {
            OnPropertyChanged(nameof(SelectedRelation));
            OnPropertyChanged(nameof(SelectedRelationConstraintName));
            OnPropertyChanged(nameof(SelectedRelationSummaryText));
            OnPropertyChanged(nameof(SelectedRelationParentColumnsText));
            OnPropertyChanged(nameof(SelectedRelationChildColumnsText));
        }
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(NeighborhoodDepth));
        OnPropertyChanged(nameof(Zoom));
        OnPropertyChanged(nameof(ZoomText));
        OnPropertyChanged(nameof(IsLargeMode));
    }

    private void TouchRenderVersion()
    {
        RenderVersion++;
    }
    private Dictionary<string, ModelDiagramNodeState> BuildNodeStates(IReadOnlyList<ModelTableNode> visibleTables, IReadOnlySet<string> searchMatchedNames)
    {
        Dictionary<string, ModelDiagramNodeState> nodes = new(StringComparer.OrdinalIgnoreCase);
        const double compactWidth = 240d;
        const double compactHeight = 84d;
        const double expandedRowHeight = 20d;

        if (SelectedTable != null && visibleTables.Any(item => string.Equals(item.TableName, SelectedTable.TableName, StringComparison.OrdinalIgnoreCase)))
        {
            List<ModelTableNode> parents = GetParentTables(SelectedTable.TableName, visibleTables);
            List<ModelTableNode> children = GetChildTables(SelectedTable.TableName, visibleTables);
            HashSet<string> directNames = parents.Select(item => item.TableName)
                .Concat(children.Select(item => item.TableName))
                .Append(SelectedTable.TableName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<ModelTableNode> others = visibleTables.Where(item => !directNames.Contains(item.TableName)).ToList();

            AddColumn(nodes, parents, 40d, 40d, compactWidth, compactHeight, expandedRowHeight, visibleTables.Count, searchMatchedNames);
            AddSingle(nodes, SelectedTable, 360d, 140d, compactWidth, compactHeight, expandedRowHeight, visibleTables.Count, searchMatchedNames);
            AddColumn(nodes, children, 680d, 40d, compactWidth, compactHeight, expandedRowHeight, visibleTables.Count, searchMatchedNames);
            AddGrid(nodes, others, 40d, 360d, compactWidth, compactHeight, expandedRowHeight, visibleTables.Count, searchMatchedNames);
        }
        else if (!string.IsNullOrWhiteSpace(SearchKeyword) && visibleTables.Count > 0)
        {
            List<ModelTableNode> prioritized = visibleTables
                .OrderByDescending(item => item.ReferencesCount + item.ReferencedByCount + item.ForeignKeyCount)
                .ThenBy(item => item.TableName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            int focusCount = Math.Min(prioritized.Count, 12);
            List<ModelTableNode> focusTables = prioritized.Take(focusCount).ToList();
            List<ModelTableNode> remainingTables = prioritized.Skip(focusCount).ToList();
            int focusColumns = focusTables.Count <= 3 ? focusTables.Count : focusTables.Count <= 8 ? 3 : 4;
            double focusStartX = Math.Max(40d, (1200d - focusColumns * 280d) / 2d);
            AddGrid(nodes, focusTables, focusStartX, 40d, compactWidth, compactHeight, expandedRowHeight, visibleTables.Count, searchMatchedNames, focusColumns);
            if (remainingTables.Count > 0)
            {
                int focusRows = (int)Math.Ceiling(focusTables.Count / (double)focusColumns);
                double nextStartY = 40d + focusRows * 150d + 70d;
                AddGrid(nodes, remainingTables, 40d, nextStartY, compactWidth, compactHeight, expandedRowHeight, visibleTables.Count, searchMatchedNames);
            }
        }
        else
        {
            AddGrid(nodes, visibleTables, 40d, 40d, compactWidth, compactHeight, expandedRowHeight, visibleTables.Count, searchMatchedNames);
        }

        CanvasWidth = Math.Max(1200d, nodes.Values.Any() ? nodes.Values.Max(item => item.X + item.Width + 60d) : 1200d);
        CanvasHeight = Math.Max(720d, nodes.Values.Any() ? nodes.Values.Max(item => item.Y + item.Height + 60d) : 720d);
        return nodes;
    }
    private IReadOnlyList<ModelDiagramRelationState> BuildRelationStates(IReadOnlyDictionary<string, ModelDiagramNodeState> nodeLookup)
    {
        List<ModelDiagramRelationState> result = [];
        HashSet<string> seenRelationKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (string tableName in nodeLookup.Keys)
        {
            if (!_relationsByTableLookup.TryGetValue(tableName, out IReadOnlyList<ModelRelationEdge>? relationItems))
            {
                continue;
            }

            foreach (ModelRelationEdge relation in relationItems)
            {
                string relationKey = BuildRelationKey(relation.ConstraintName, relation.ParentTable, relation.ChildTable);
                if (!seenRelationKeys.Add(relationKey) ||
                    !nodeLookup.TryGetValue(relation.ParentTable, out ModelDiagramNodeState? parentNode) ||
                    !nodeLookup.TryGetValue(relation.ChildTable, out ModelDiagramNodeState? childNode))
                {
                    continue;
                }

                (double startX, double startY, double endX, double endY) = ResolveLineEndpoints(parentNode, childNode);
                bool highlighted = SelectedTable != null &&
                                   (string.Equals(SelectedTable.TableName, relation.ParentTable, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(SelectedTable.TableName, relation.ChildTable, StringComparison.OrdinalIgnoreCase));
                bool selected = SelectedRelation != null &&
                                string.Equals(SelectedRelation.ConstraintName, relation.ConstraintName, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(SelectedRelation.FromTable, relation.ParentTable, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(SelectedRelation.ToTable, relation.ChildTable, StringComparison.OrdinalIgnoreCase);
                result.Add(new ModelDiagramRelationState
                {
                    ConstraintName = relation.ConstraintName,
                    FromTable = relation.ParentTable,
                    ToTable = relation.ChildTable,
                    ParentColumnsText = string.Join(", ", relation.ParentColumns),
                    ChildColumnsText = string.Join(", ", relation.ChildColumns),
                    ParentColumns = relation.ParentColumns.ToArray(),
                    ChildColumns = relation.ChildColumns.ToArray(),
                    StartX = startX,
                    StartY = startY,
                    EndX = endX,
                    EndY = endY,
                    PathData = $"M {startX},{startY} L {endX},{endY}",
                    IsSelected = selected,
                    IsHighlighted = highlighted
                });
            }
        }

        return result;
    }

    private void UpdateRelationSelectionState()
    {
        foreach (ModelDiagramRelationState relation in VisibleRelations)
        {
            relation.IsSelected =
                SelectedRelation != null &&
                string.Equals(relation.ConstraintName, SelectedRelation.ConstraintName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relation.FromTable, SelectedRelation.FromTable, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relation.ToTable, SelectedRelation.ToTable, StringComparison.OrdinalIgnoreCase);
        }
    }

    private bool RefreshSelectedTableDetails()
    {
        string detailKey = SelectedTable?.TableName ?? string.Empty;
        if (string.Equals(_selectedTableDetailsKey, detailKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _selectedTableDetailsKey = detailKey;
        ReplaceCollection(SelectedTableColumns, SelectedTable?.Columns ?? []);
        ReplaceCollection(
            SelectedTableReferenceTexts,
            SelectedTable == null
                ? []
                : _referenceTextLookup.TryGetValue(SelectedTable.TableName, out IReadOnlyList<string>? references)
                    ? references
                    : []);
        ReplaceCollection(
            SelectedTableReferencedByTexts,
            SelectedTable == null
                ? []
                : _referencedByTextLookup.TryGetValue(SelectedTable.TableName, out IReadOnlyList<string>? referencedBy)
                    ? referencedBy
                    : []);
        return true;
    }

    private bool RefreshSelectedRelationDetails()
    {
        string detailKey = SelectedRelation == null
            ? string.Empty
            : BuildRelationKey(SelectedRelation.ConstraintName, SelectedRelation.FromTable, SelectedRelation.ToTable);
        if (string.Equals(_selectedRelationDetailsKey, detailKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _selectedRelationDetailsKey = detailKey;
        return true;
    }
    private HashSet<string> BuildNeighborhoodNames(string tableName, int depth)
    {
        if (string.Equals(_neighborhoodCacheTableName, tableName, StringComparison.OrdinalIgnoreCase) &&
            _neighborhoodCacheDepth == depth &&
            _neighborhoodCacheNames.Count > 0)
        {
            return new HashSet<string>(_neighborhoodCacheNames, StringComparer.OrdinalIgnoreCase);
        }

        HashSet<string> result = [tableName];
        HashSet<string> frontier = [tableName];

        for (int level = 0; level < depth; level++)
        {
            HashSet<string> next = [];
            foreach (string table in frontier)
            {
                if (_childTableLookup.TryGetValue(table, out IReadOnlyList<string>? childTables))
                {
                    next.UnionWith(childTables);
                }

                if (_parentTableLookup.TryGetValue(table, out IReadOnlyList<string>? parentTables))
                {
                    next.UnionWith(parentTables);
                }
            }

            next.ExceptWith(result);
            if (next.Count == 0)
            {
                break;
            }

            result.UnionWith(next);
            frontier = next;
        }

        _neighborhoodCacheTableName = tableName;
        _neighborhoodCacheDepth = depth;
        _neighborhoodCacheNames = new HashSet<string>(result, StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private List<ModelTableNode> GetParentTables(string tableName, IReadOnlyList<ModelTableNode> visibleTables)
    {
        HashSet<string> names = _parentTableLookup.TryGetValue(tableName, out IReadOnlyList<string>? parentTables)
            ? parentTables.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        return visibleTables.Where(item => names.Contains(item.TableName)).OrderBy(item => item.TableName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<ModelTableNode> GetChildTables(string tableName, IReadOnlyList<ModelTableNode> visibleTables)
    {
        HashSet<string> names = _childTableLookup.TryGetValue(tableName, out IReadOnlyList<string>? childTables)
            ? childTables.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        return visibleTables.Where(item => names.Contains(item.TableName)).OrderBy(item => item.TableName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void AddColumn(
        IDictionary<string, ModelDiagramNodeState> target,
        IReadOnlyList<ModelTableNode> tables,
        double x,
        double startY,
        double width,
        double compactHeight,
        double expandedRowHeight,
        int visibleTableCount,
        IReadOnlySet<string> searchMatchedNames)
    {
        for (int index = 0; index < tables.Count; index++)
        {
            AddSingle(target, tables[index], x, startY + index * 140d, width, compactHeight, expandedRowHeight, visibleTableCount, searchMatchedNames);
        }
    }

    private void AddGrid(
        IDictionary<string, ModelDiagramNodeState> target,
        IReadOnlyList<ModelTableNode> tables,
        double startX,
        double startY,
        double width,
        double compactHeight,
        double expandedRowHeight,
        int visibleTableCount,
        IReadOnlySet<string> searchMatchedNames,
        int columns = 4)
    {
        for (int index = 0; index < tables.Count; index++)
        {
            int row = index / columns;
            int column = index % columns;
            AddSingle(target, tables[index], startX + column * 280d, startY + row * 150d, width, compactHeight, expandedRowHeight, visibleTableCount, searchMatchedNames);
        }
    }

    private void AddSingle(
        IDictionary<string, ModelDiagramNodeState> target,
        ModelTableNode table,
        double x,
        double y,
        double width,
        double compactHeight,
        double expandedRowHeight,
        int visibleTableCount,
        IReadOnlySet<string> searchMatchedNames)
    {
        bool isSelected = SelectedTable != null &&
                          string.Equals(SelectedTable.TableName, table.TableName, StringComparison.OrdinalIgnoreCase);
        bool isHighlighted = isSelected || IsDirectlyRelatedToSelectedTable(table.TableName);
        IReadOnlyList<string> previewLines = BuildPreviewColumnLines(table, visibleTableCount, isSelected, isHighlighted);
        double height = compactHeight + previewLines.Count * expandedRowHeight;
        bool isSearchMatched = searchMatchedNames.Contains(table.TableName);

        bool hasPositionOverride = _nodePositionOverrides.TryGetValue(table.TableName, out (double X, double Y) position);
        target[table.TableName] = new ModelDiagramNodeState
        {
            TableName = table.TableName,
            DisplayName = string.IsNullOrWhiteSpace(table.DisplayName) ? table.TableName : table.DisplayName,
            CommentText = table.CommentText,
            PreviewColumnLines = previewLines,
            X = hasPositionOverride ? position.X : x,
            Y = hasPositionOverride ? position.Y : y,
            Width = width,
            Height = height,
            IsSelected = isSelected,
            IsHighlighted = isHighlighted,
            IsSearchMatched = isSearchMatched
        };
    }
    private ModelTableNode? PickLargeModeSeedTable()
    {
        return Tables
            .OrderByDescending(item => item.ReferencesCount + item.ReferencedByCount + item.ForeignKeyCount)
            .ThenByDescending(item => item.PrimaryKeyColumns.Count)
            .ThenBy(item => item.TableName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(item => item.ReferencesCount > 0 || item.ReferencedByCount > 0 || item.ForeignKeyCount > 0);
    }

    private bool IsDirectlyRelatedToSelectedTable(string tableName)
    {
        if (SelectedTable == null)
        {
            return false;
        }

        if (string.Equals(SelectedTable.TableName, tableName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (_childTableLookup.TryGetValue(SelectedTable.TableName, out IReadOnlyList<string>? childTables) &&
                childTables.Contains(tableName, StringComparer.OrdinalIgnoreCase)) ||
               (_parentTableLookup.TryGetValue(SelectedTable.TableName, out IReadOnlyList<string>? parentTables) &&
                parentTables.Contains(tableName, StringComparer.OrdinalIgnoreCase));
    }

    private IReadOnlyList<string> BuildPreviewColumnLines(
        ModelTableNode table,
        int visibleTableCount,
        bool isSelected,
        bool isHighlighted)
    {
        if (!ShowColumns || table.Columns.Count == 0)
        {
            return [];
        }

        // 大图模式下只展开当前表和直接关联表字段，避免一次渲染上千个 TextBlock。
        if (visibleTableCount > 40 && !isSelected && !isHighlighted)
        {
            return [];
        }

        int count = Math.Min(visibleTableCount > 80 ? 4 : 8, table.Columns.Count);
        List<string> lines = new(count + 1);
        for (int index = 0; index < count; index++)
        {
            ModelColumnNode column = table.Columns[index];
            string prefix = column.IsPrimaryKey ? "PK " : column.IsForeignKey ? "FK " : string.Empty;
            string comment = string.IsNullOrWhiteSpace(column.CommentText) ? string.Empty : $" - {column.CommentText}";
            lines.Add($"{prefix}{column.ColumnName}{comment}");
        }

        if (table.Columns.Count > count)
        {
            lines.Add($"... 还有 {table.Columns.Count - count} 个字段");
        }

        return lines;
    }

    private void RecalculateCanvasBounds(IEnumerable<ModelDiagramNodeState> nodes)
    {
        List<ModelDiagramNodeState> materializedNodes = nodes.ToList();
        CanvasWidth = Math.Max(1200d, materializedNodes.Any() ? materializedNodes.Max(item => item.X + item.Width + 60d) : 1200d);
        CanvasHeight = Math.Max(720d, materializedNodes.Any() ? materializedNodes.Max(item => item.Y + item.Height + 60d) : 720d);
    }

    private static (double startX, double startY, double endX, double endY) ResolveLineEndpoints(ModelDiagramNodeState from, ModelDiagramNodeState to)
    {
        if (from.X + from.Width <= to.X)
        {
            return (from.X + from.Width, from.Y + from.Height / 2d, to.X, to.Y + to.Height / 2d);
        }

        if (to.X + to.Width <= from.X)
        {
            return (from.X, from.Y + from.Height / 2d, to.X + to.Width, to.Y + to.Height / 2d);
        }

        if (from.Y <= to.Y)
        {
            return (from.X + from.Width / 2d, from.Y + from.Height, to.X + to.Width / 2d, to.Y);
        }

        return (from.X + from.Width / 2d, from.Y, to.X + to.Width / 2d, to.Y + to.Height);
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        target.Clear();
        foreach (T item in items)
        {
            target.Add(item);
        }
    }

    private void BuildRelationLookups()
    {
        Dictionary<string, HashSet<string>> parentSets = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> childSets = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> referenceTexts = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> referencedByTexts = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<ModelRelationEdge>> relationItemsByTable = new(StringComparer.OrdinalIgnoreCase);

        foreach (ModelRelationEdge relation in Relations)
        {
            AddLookupValue(parentSets, relation.ChildTable, relation.ParentTable);
            AddLookupValue(childSets, relation.ParentTable, relation.ChildTable);
            AddLookupValue(referenceTexts, relation.ChildTable, $"{relation.ParentTable} ({string.Join(", ", relation.ParentColumns)})");
            AddLookupValue(referencedByTexts, relation.ParentTable, $"{relation.ChildTable} ({string.Join(", ", relation.ChildColumns)})");
            AddRelationLookupValue(relationItemsByTable, relation.ParentTable, relation);
            AddRelationLookupValue(relationItemsByTable, relation.ChildTable, relation);
        }

        CopyLookup(_parentTableLookup, parentSets);
        CopyLookup(_childTableLookup, childSets);
        CopyLookup(_referenceTextLookup, referenceTexts);
        CopyLookup(_referencedByTextLookup, referencedByTexts);
        CopyRelationLookup(_relationsByTableLookup, relationItemsByTable);
    }

    private static void AddLookupValue(Dictionary<string, HashSet<string>> target, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!target.TryGetValue(key, out HashSet<string>? values))
        {
            values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            target[key] = values;
        }

        values.Add(value);
    }

    private static void CopyLookup(Dictionary<string, IReadOnlyList<string>> target, Dictionary<string, HashSet<string>> source)
    {
        foreach (KeyValuePair<string, HashSet<string>> pair in source)
        {
            target[pair.Key] = pair.Value.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    private static void AddRelationLookupValue(Dictionary<string, List<ModelRelationEdge>> target, string key, ModelRelationEdge relation)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!target.TryGetValue(key, out List<ModelRelationEdge>? values))
        {
            values = [];
            target[key] = values;
        }

        values.Add(relation);
    }

    private static void CopyRelationLookup(Dictionary<string, IReadOnlyList<ModelRelationEdge>> target, Dictionary<string, List<ModelRelationEdge>> source)
    {
        foreach (KeyValuePair<string, List<ModelRelationEdge>> pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static string BuildRelationKey(string constraintName, string parentTable, string childTable)
    {
        return string.Concat(constraintName, "\u001F", parentTable, "\u001F", childTable);
    }
    public string ConsumePendingViewportCenterTableName()
    {
        string tableName = _pendingViewportCenterTableName;
        _pendingViewportCenterTableName = string.Empty;
        return tableName;
    }

    private void QueueViewportCenter(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return;
        }

        _pendingViewportCenterTableName = tableName;
    }

    private static string BuildTableSearchIndex(ModelTableNode table)
    {
        IEnumerable<string> parts = new[] { table.TableName, table.DisplayName, table.CommentText }
            .Concat(table.Columns.SelectMany(column => new[] { column.ColumnName, column.DisplayName, column.CommentText, column.DataType }));
        return string.Join('\n', parts.Where(static item => !string.IsNullOrWhiteSpace(item)));
    }
}
