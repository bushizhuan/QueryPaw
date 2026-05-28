using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SqlAnalyzer.App.Localization;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.App.Services;
using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;

namespace SqlAnalyzer.App.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
	private readonly record struct DatabaseContextState(
		ConnectionProfile? Profile,
		string SchemaName,
		bool IsConnectionConnected,
		bool HasSchema,
		bool IsSchemaOpened,
		string Source);

	private static readonly string ExecutionPipelineLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlAnalyzer.Next", "execution-pipeline.log");

	private static readonly UiTextSet DefaultUiText = UiTextResourceStore.GetDefault();

	private static readonly bool DiagnosticLoggingEnabled = string.Equals(Environment.GetEnvironmentVariable("SQLANALYZER_DIAGNOSTIC_LOGS"), "1", StringComparison.OrdinalIgnoreCase);

	private static readonly TimeSpan ExplorerOperationTimeout = TimeSpan.FromSeconds(12L);

	private static readonly TimeSpan RelationLocatorWarmupTimeout = TimeSpan.FromSeconds(45L);

	private const string DefaultSchemaOption = "(Default)";
	public const int DefaultResultPreviewRowLimit = 500;
	public const int MaxResultPreviewRowLimit = 10000;

	private readonly IDatabaseProviderCatalog _providerCatalog;

	private readonly IDatabaseExplorerService _databaseExplorerService;

	private readonly ICommentMaintenanceService _commentMaintenanceService;

	private readonly IModelDiagramService _modelDiagramService;

	private readonly IObjectEditorService _objectEditorService;

	private readonly IConnectionProfileStore _connectionProfileStore;

	private readonly IEditorSessionStore _editorSessionStore;

	private readonly ILocalizationResolver _localizationResolver;

	private readonly ISqlExecutionService _sqlExecutionService;

	private readonly ISqlFormatterService _sqlFormatterService;

	private readonly CompletionSnapshotCoordinator _completionSnapshots;
	private readonly DocumentWorkspaceStateStore _workspaceStates;

	private readonly Dictionary<string, int> _recentCompletionUsage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> _connectedConnectionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	private readonly SchemaWorkspaceStateStore _schemaStates = new();

	private int _completionRequestVersion;
	private string _focusedExplorerNodeKey = string.Empty;
	private string _focusedExplorerConnectionProfileId = string.Empty;
	private string _focusedExplorerSchemaName = string.Empty;
	private string _focusedExplorerObjectName = string.Empty;
	private string _applicationTitle = DefaultUiText.ApplicationTitle;
	private string _connectionStatus = string.Empty;
	private string _executionStatus = DefaultUiText.ExecutionReady;
	private EditorDocument? _selectedDocument;
	private ObjectNode? _selectedObjectNode;
	private ConnectionProfile? _selectedConnectionProfile;
	private ConnectionProfile? _activeConnectionProfile;
	private ResultSetViewItem? _selectedResultSet;
	private string _resultPreview = DefaultUiText.ResultPlaceholder;
	private string _logPreview = DefaultUiText.WorkspaceBootstrapped;
	private string _searchText = string.Empty;
	private string _replaceText = string.Empty;
	private bool _isReplaceVisible;
	private bool _isSearchVisible;
	private bool _isCompletionOpen;
	private CompletionItem? _selectedCompletionItem;
	private UiTextSet _uiText = DefaultUiText;
	private string _currentLanguageCode = UiTextResourceStore.DefaultLanguageCode;
	private bool _isConnectionDialogOpen;
	private bool _isWordWrapEnabled = true;
	private int _lastResultRowCount;
	private string _lastExecutionTimeText = "--";
	private string _connectionSearchText = string.Empty;
	private string _selectedEnvironmentFilter = string.Empty;
	private string _selectedGroupFilter = string.Empty;
	private string _selectedCapabilityFilter = string.Empty;
	private bool _favoritesOnly;
	private string _selectedResultHeaderMode = "Dual";
	private ConnectionEditorModeKind _connectionEditorMode = ConnectionEditorModeKind.View;
	private ConnectionProfile? _connectionEditorDraft;
	private bool _isPrioritizingSelectedDocumentSchemas;

	public ObservableCollection<ObjectNode> ExplorerNodes { get; }

	public ObservableCollection<EditorDocument> Documents { get; }

	public ObservableCollection<string> Logs { get; }

	public ObservableCollection<ConnectionProfile> ConnectionProfiles { get; }

	public ObservableCollection<RecentFileEntry> RecentFiles { get; } = new ObservableCollection<RecentFileEntry>();

	public ObservableCollection<QueryHistoryEntry> QueryHistoryEntries { get; } = new ObservableCollection<QueryHistoryEntry>();

	public ObservableCollection<ResultSetViewItem> ResultSets { get; }

	public ObservableCollection<CompletionItem> CompletionItems { get; }

	public IReadOnlyList<LanguageOption> LanguageOptions => UiTextResourceStore.GetLanguageOptions();

	public IReadOnlyList<string> EnvironmentFilters => new[] { UiText.AllEnvironments, "DEV", "TEST", "UAT", "PROD" };

	public IReadOnlyList<string> EnvironmentTagOptions => new[] { "DEV", "TEST", "UAT", "PROD" };

	public IReadOnlyList<string> CapabilityFilters => new[] { UiText.AllCapabilities, UiText.Verified, UiText.Experimental, UiText.Planned };

	public IReadOnlyList<string> GroupFilters
	{
		get
		{
			List<string> groups = new List<string>();
			groups.Add(UiText.AllGroups);
			groups.AddRange((from item in ConnectionProfiles
				select item.GroupName into item
				where !string.IsNullOrWhiteSpace(item)
				select item).Distinct<string>(StringComparer.OrdinalIgnoreCase).OrderBy<string, string>((string item) => item, StringComparer.OrdinalIgnoreCase));
			return groups.ToArray();
		}
	}

	public IReadOnlyList<ConnectionProfile> FilteredConnectionProfiles => (from item in ConnectionProfiles.Where(MatchesConnectionFilter)
		orderby item.IsFavorite descending, item.LastUsedAt ?? DateTimeOffset.MinValue descending
		select item).ThenBy<ConnectionProfile, string>((ConnectionProfile item) => item.GroupName, StringComparer.OrdinalIgnoreCase).ThenBy<ConnectionProfile, string>((ConnectionProfile item) => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();

	public IReadOnlyList<ConnectionProfile> RecentConnectionProfiles => (from item in ConnectionProfiles
		where item.LastUsedAt.HasValue
		orderby item.LastUsedAt descending
		select item).Take(5).ToArray();

	public bool HasRecentConnections => RecentConnectionProfiles.Count > 0;

	public ConnectionEditorModeKind ConnectionEditorMode => _connectionEditorMode;

	public ConnectionProfile? ConnectionEditorDraft => _connectionEditorDraft;

	public bool ConnectionEditorHasProfile => _connectionEditorDraft != null;

	public bool ConnectionEditorHasNoProfile => _connectionEditorDraft == null;

	public bool ConnectionEditorCanEditFields => _connectionEditorMode != ConnectionEditorModeKind.View && _connectionEditorDraft != null;

	public bool ConnectionEditorIsReadOnly => !ConnectionEditorCanEditFields;

	public bool ConnectionEditorCanChangeSelection => !ConnectionEditorCanEditFields;

	public bool ConnectionEditorCanStartNew => !ConnectionEditorCanEditFields;

	public bool ConnectionEditorShowViewActions => _connectionEditorMode == ConnectionEditorModeKind.View;

	public bool ConnectionEditorShowEditActions => _connectionEditorMode != ConnectionEditorModeKind.View;

	public bool ConnectionEditorCanEditSelected => ConnectionEditorShowViewActions && SelectedConnectionProfile != null;

	public bool ConnectionEditorCanDuplicateSelected => ConnectionEditorShowViewActions && SelectedConnectionProfile != null;

	public bool ConnectionEditorCanDeleteSelected => ConnectionEditorShowViewActions && SelectedConnectionProfile != null;

	public bool ConnectionEditorCanUseSelected => ConnectionEditorShowViewActions && SelectedConnectionProfile != null;

	public bool ConnectionEditorCanTest => _connectionEditorDraft != null && !string.IsNullOrWhiteSpace(_connectionEditorDraft.ProviderName);

	public bool ConnectionEditorCanSave => ConnectionEditorCanEditFields && _connectionEditorDraft != null && !string.IsNullOrWhiteSpace(_connectionEditorDraft.ProviderName);

	public string ConnectionEditorModeText => _connectionEditorMode switch
	{
		ConnectionEditorModeKind.Create => UiText.ConnectionEditorModeCreate,
		ConnectionEditorModeKind.Edit => UiText.ConnectionEditorModeEdit,
		_ => UiText.ConnectionEditorModeView
	};

	public string ConnectionEditorTitleText
	{
		get
		{
			if (_connectionEditorDraft == null)
			{
				return UiText.NoConnectionSelected;
			}

			return string.IsNullOrWhiteSpace(_connectionEditorDraft.Name)
				? UiText.ConnectionName
				: _connectionEditorDraft.Name;
		}
	}

	public string ConnectionEditorHintText => _connectionEditorMode switch
	{
		ConnectionEditorModeKind.Create => UiText.ConnectionEditorCreateHint,
		ConnectionEditorModeKind.Edit => UiText.ConnectionEditorEditHint,
		_ => UiText.ConnectionEditorViewHint
	};

	public string ConnectionEditorEmptyText => UiText.ConnectionEditorEmpty;

	public string ConnectionEditorBasicSectionText => UiText.ConnectionEditorBasicSection;

	public string ConnectionEditorProviderSectionText => UiText.ConnectionEditorProviderSection;

	public string ConnectionEditorEndpointSectionText => UiText.ConnectionEditorEndpointSection;

	public string ConnectionEditorAuthSectionText => UiText.ConnectionEditorAuthSection;

	public string ConnectionEditorAdvancedSectionText => UiText.ConnectionEditorAdvancedSection;

	public string ConnectionEditorNotesSectionText => UiText.ConnectionEditorNotesSection;

	public ConnectionProfile? FocusedExplorerConnectionProfile =>
		ConnectionProfiles.FirstOrDefault(item => string.Equals(item.Id, _focusedExplorerConnectionProfileId, StringComparison.OrdinalIgnoreCase));

	public string FocusedExplorerSchemaName => _focusedExplorerSchemaName;

	public bool FocusedExplorerHasConnection => FocusedExplorerConnectionProfile != null;

	public bool FocusedExplorerIsConnectionLinked => IsConnectionProfileConnected(FocusedExplorerConnectionProfile);

	public bool FocusedExplorerIsSchemaOpened => IsSchemaOpen(FocusedExplorerConnectionProfile, _focusedExplorerSchemaName);

	public bool CanOpenFocusedCommentMaintenance => CanOpenSchemaWorkbench(ResolveWorkbenchContext());

	public bool CanOpenFocusedModelDiagram => CanOpenSchemaWorkbench(ResolveWorkbenchContext());

	public string FocusedExplorerContextText
	{
		get
		{
			ConnectionProfile? profile = FocusedExplorerConnectionProfile;
			if (profile == null)
			{
				return UiText.FocusedExplorerNoSelection;
			}

			string connectionPart = string.Format(CultureInfo.CurrentCulture, UiText.FocusedExplorerConnectionFormat, profile.Name);
			if (string.IsNullOrWhiteSpace(_focusedExplorerSchemaName))
			{
				return connectionPart;
			}

			string schemaState = FocusedExplorerIsSchemaOpened ? UiText.SchemaOpened : UiText.SchemaClosed;
			return string.Format(CultureInfo.CurrentCulture, UiText.FocusedExplorerSchemaFormat, profile.Name, _focusedExplorerSchemaName, schemaState);
		}
	}

	public IReadOnlyList<DatabaseProviderDefinition> Providers => _providerCatalog.GetAll();

	public IReadOnlyList<string> ResultHeaderModes => new[] { "Dual", "Display", "Raw" };

	public IReadOnlyList<string> OracleConnectionModes => new[] { "HostService", "Tns" };

	public IReadOnlyList<string> AuthenticationModeOptions => ConnectionProfileUtilities.GetAuthenticationModeOptions(_connectionEditorDraft?.ProviderName);

	public bool IsOracleProfileSelected => string.Equals(_connectionEditorDraft?.ProviderName, "Oracle", StringComparison.OrdinalIgnoreCase);

	public bool IsMongoProfileSelected => string.Equals(_connectionEditorDraft?.ProviderName, "MongoDb", StringComparison.OrdinalIgnoreCase);

	public bool IsServerDatabaseProfileSelected => !IsOracleProfileSelected && !IsMongoProfileSelected;

	public bool IsGenericConnectionFormVisible => IsServerDatabaseProfileSelected;

	public bool IsGenericPortVisible => IsServerDatabaseProfileSelected;

	public bool IsOracleHostMode => IsOracleProfileSelected && string.Equals(_connectionEditorDraft?.OracleConnectionMode ?? "HostService", "HostService", StringComparison.OrdinalIgnoreCase);

	public bool IsOracleTnsMode => IsOracleProfileSelected && string.Equals(_connectionEditorDraft?.OracleConnectionMode ?? "HostService", "Tns", StringComparison.OrdinalIgnoreCase);

	public string SelectedProviderDisplayName => Providers.FirstOrDefault((DatabaseProviderDefinition item) => string.Equals(item.Name, _connectionEditorDraft?.ProviderName, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? _connectionEditorDraft?.ProviderName ?? string.Empty;

	public string ServerFieldLabel => IsMongoProfileSelected ? UiText.Host : UiText.Server;

	public string DatabaseFieldLabel => IsMongoProfileSelected ? UiText.MongoDatabaseAuthSource : UiText.Database;

	public bool HasResults => ResultSets.Count > 0;

	public bool SelectedDocumentHasResults => GetSelectedDocumentState().ResultSets.Count > 0;

	public bool SelectedDocumentIsExecuting => GetSelectedDocumentState().IsExecuting;

	public bool SelectedDocumentIsRenderingResults => GetSelectedDocumentState().IsRenderingResults;

	public bool SelectedDocumentCanStartExecution => SelectedDocumentIsQuery && !GetSelectedDocumentState().IsExecuting;

	public bool SelectedDocumentIsBusy => SelectedDocumentIsObjectEditor
		? (GetSelectedObjectEditorState()?.IsBusy ?? false)
		: SelectedDocumentIsCommentMaintenance
			? (GetSelectedCommentWorkspaceState()?.IsBusy ?? false)
			: GetSelectedDocumentState().IsExecuting;

	public bool SelectedDocumentIsQuery => string.Equals(SelectedDocument?.DocumentKind, "Query", StringComparison.OrdinalIgnoreCase);

	public bool SelectedDocumentIsCommentMaintenance => string.Equals(SelectedDocument?.DocumentKind, "CommentMaintenance", StringComparison.OrdinalIgnoreCase);

	public bool SelectedDocumentIsModelDiagram => string.Equals(SelectedDocument?.DocumentKind, "ModelDiagram", StringComparison.OrdinalIgnoreCase);

	public bool SelectedDocumentIsObjectEditor => string.Equals(SelectedDocument?.DocumentKind, "ObjectEditor", StringComparison.OrdinalIgnoreCase);

	public bool SelectedDocumentIsQueryHistory => string.Equals(SelectedDocument?.DocumentKind, "QueryHistory", StringComparison.OrdinalIgnoreCase);

	public bool SelectedDocumentUsesTextEditor => SelectedDocumentIsQuery || SelectedDocumentIsObjectEditor;

	public bool SelectedDocumentShouldShowResultWorkspace =>
		SelectedDocumentIsObjectEditor ||
		(SelectedDocumentIsQuery && GetSelectedDocumentState().IsResultWorkspaceOpen);

	public bool SelectedDocumentCanToggleResultWorkspace => false;

	public string SelectedDocumentResultWorkspaceToggleText => UiText.Results;

	public CommentMaintenanceWorkspaceState? SelectedCommentWorkspace => GetSelectedCommentWorkspaceState();

	public ObservableCollection<CommentMaintenanceTableItem> SelectedCommentTables => GetSelectedCommentWorkspaceState()?.FilteredTables ?? [];

	public ObservableCollection<CommentMaintenanceColumnItem> SelectedCommentColumns => GetSelectedCommentWorkspaceState()?.FilteredColumns ?? [];

	public string SelectedCommentTablesCountText =>
		string.Format(CultureInfo.CurrentCulture, UiText.ItemCountFormat, SelectedCommentTables.Count);

	public string SelectedCommentColumnsCountText =>
		string.Format(CultureInfo.CurrentCulture, UiText.ItemCountFormat, SelectedCommentColumns.Count);

	public CommentMaintenanceTableItem? SelectedCommentWorkspaceSelectedTable
	{
		get
		{
			return GetSelectedCommentWorkspaceState()?.SelectedTableItem;
		}
		set
		{
			if (GetSelectedCommentWorkspaceState() is { } state && !ReferenceEquals(state.SelectedTableItem, value))
			{
				state.SelectedTableItem = value;
				NotifySelectedCommentWorkspaceChanged();
			}
		}
	}

	public ObservableCollection<string> SelectedCommentTableFilterOptions => GetSelectedCommentWorkspaceState()?.TableFilterOptions ?? [];

	public bool SelectedCommentWorkspaceIsLoaded => GetSelectedCommentWorkspaceState()?.IsLoaded ?? false;

	public bool SelectedCommentWorkspaceIsBusy => GetSelectedCommentWorkspaceState()?.IsBusy ?? false;

	public bool SelectedCommentWorkspaceHasChanges => GetSelectedCommentWorkspaceState()?.HasChanges ?? false;

	public int SelectedCommentWorkspaceChangedCount => GetSelectedCommentWorkspaceState()?.ChangedCount ?? 0;

	public string SelectedCommentWorkspaceChangedCountText =>
		string.Format(CultureInfo.CurrentCulture, UiText.ChangedCountFormat, SelectedCommentWorkspaceChangedCount);

	public string SelectedCommentWorkspaceConnectionName => GetSelectedCommentWorkspaceState()?.ConnectionName ?? string.Empty;

	public string SelectedCommentWorkspaceSchemaName => GetSelectedCommentWorkspaceState()?.SchemaName ?? string.Empty;

	public string SelectedCommentWorkspaceLoadedAtText => GetSelectedCommentWorkspaceState()?.LoadedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "--";

	public bool SelectedCommentWorkspaceHasSummary => GetSelectedCommentWorkspaceState()?.HasOperationSummary ?? false;

	public string SelectedCommentWorkspaceSummaryText => GetSelectedCommentWorkspaceState()?.LastOperationSummary ?? string.Empty;

	public string SelectedCommentWorkspaceSummaryForeground => (GetSelectedCommentWorkspaceState()?.LastOperationHasIssues ?? false) ? "#B45309" : "#2563EB";

	public ModelDiagramWorkspaceState? SelectedModelDiagram => GetSelectedModelDiagramState();

	public bool SelectedModelDiagramIsLoaded => GetSelectedModelDiagramState()?.IsLoaded ?? false;

	public string SelectedModelDiagramConnectionName => GetSelectedModelDiagramState()?.ConnectionName ?? string.Empty;

	public string SelectedModelDiagramSchemaName => GetSelectedModelDiagramState()?.SchemaName ?? string.Empty;

	public string SelectedModelDiagramLoadedAtText => GetSelectedModelDiagramState()?.LoadedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "--";

	public ObservableCollection<ModelTableNode> SelectedModelDiagramTables => GetSelectedModelDiagramState()?.FilteredTables ?? [];

	public ObservableCollection<ModelDiagramNodeState> SelectedModelDiagramVisibleNodes => GetSelectedModelDiagramState()?.VisibleNodes ?? [];

	public ObservableCollection<ModelDiagramRelationState> SelectedModelDiagramVisibleRelations => GetSelectedModelDiagramState()?.VisibleRelations ?? [];

	public ObservableCollection<ModelColumnNode> SelectedModelDiagramSelectedTableColumns => GetSelectedModelDiagramState()?.SelectedTableColumns ?? [];

	public ObservableCollection<string> SelectedModelDiagramSelectedTableReferences => GetSelectedModelDiagramState()?.SelectedTableReferenceTexts ?? [];

	public ObservableCollection<string> SelectedModelDiagramSelectedTableReferencedBy => GetSelectedModelDiagramState()?.SelectedTableReferencedByTexts ?? [];

	public ModelDiagramRelationState? SelectedModelDiagramSelectedRelation => GetSelectedModelDiagramState()?.SelectedRelation;

	public ModelTableNode? SelectedModelDiagramSelectedTable
	{
		get
		{
			return GetSelectedModelDiagramState()?.SelectedTable;
		}
		set
		{
			if (GetSelectedModelDiagramState() is { } state && !ReferenceEquals(state.SelectedTable, value))
			{
				state.SelectedTable = value;
			}
		}
	}

	public string SelectedModelDiagramSelectedTableName => GetSelectedModelDiagramState()?.SelectedTableName ?? string.Empty;

	public string SelectedModelDiagramSelectedTableCommentText => GetSelectedModelDiagramState()?.SelectedTableCommentText ?? string.Empty;

	public string SelectedModelDiagramSelectedTablePrimaryKeysText => GetSelectedModelDiagramState()?.SelectedTablePrimaryKeysText ?? string.Empty;

	public string SelectedModelDiagramSelectedTablePrimaryKeysDisplay =>
		string.Format(CultureInfo.CurrentCulture, UiText.PrimaryKeysFormat, SelectedModelDiagramSelectedTablePrimaryKeysText);

	public string SelectedModelDiagramSelectedTableColumnCountDisplay =>
		string.Format(CultureInfo.CurrentCulture, UiText.ColumnCountFormat, SelectedModelDiagramSelectedTableColumns.Count);

	public string SelectedModelDiagramSelectedRelationConstraintName => GetSelectedModelDiagramState()?.SelectedRelationConstraintName ?? string.Empty;

	public string SelectedModelDiagramSelectedRelationSummaryText => GetSelectedModelDiagramState()?.SelectedRelationSummaryText ?? string.Empty;

	public string SelectedModelDiagramSelectedRelationParentColumnsText => GetSelectedModelDiagramState()?.SelectedRelationParentColumnsText ?? string.Empty;

	public string SelectedModelDiagramSelectedRelationChildColumnsText => GetSelectedModelDiagramState()?.SelectedRelationChildColumnsText ?? string.Empty;

	public string SelectedModelDiagramSelectedRelationConstraintDisplay =>
		string.Format(CultureInfo.CurrentCulture, UiText.ConstraintFormat, SelectedModelDiagramSelectedRelationConstraintName);

	public string SelectedModelDiagramSelectedRelationSummaryDisplay =>
		string.Format(CultureInfo.CurrentCulture, UiText.RelationFormat, SelectedModelDiagramSelectedRelationSummaryText);

	public string SelectedModelDiagramSelectedRelationParentColumnsDisplay =>
		string.Format(CultureInfo.CurrentCulture, UiText.ParentColumnsFormat, SelectedModelDiagramSelectedRelationParentColumnsText);

	public string SelectedModelDiagramSelectedRelationChildColumnsDisplay =>
		string.Format(CultureInfo.CurrentCulture, UiText.ChildColumnsFormat, SelectedModelDiagramSelectedRelationChildColumnsText);

	public string SelectedModelDiagramMessageText => GetSelectedModelDiagramState()?.MessageText ?? string.Empty;

	public bool SelectedModelDiagramOnlyRelatedTables
	{
		get
		{
			return GetSelectedModelDiagramState()?.OnlyRelatedTables ?? true;
		}
		set
		{
			if (GetSelectedModelDiagramState() is { } state && state.OnlyRelatedTables != value)
			{
				state.OnlyRelatedTables = value;
			}
		}
	}

	public bool SelectedModelDiagramOnlyNeighborhood
	{
		get
		{
			return GetSelectedModelDiagramState()?.OnlyNeighborhood ?? false;
		}
		set
		{
			if (GetSelectedModelDiagramState() is { } state && state.OnlyNeighborhood != value)
			{
				state.OnlyNeighborhood = value;
			}
		}
	}

	public bool SelectedModelDiagramShowColumns
	{
		get
		{
			return GetSelectedModelDiagramState()?.ShowColumns ?? false;
		}
		set
		{
			if (GetSelectedModelDiagramState() is { } state && state.ShowColumns != value)
			{
				state.ShowColumns = value;
			}
		}
	}

	public string SelectedModelDiagramSearchKeyword
	{
		get
		{
			return GetSelectedModelDiagramState()?.SearchKeyword ?? string.Empty;
		}
		set
		{
			if (GetSelectedModelDiagramState() is { } state && !string.Equals(state.SearchKeyword, value, StringComparison.Ordinal))
			{
				state.SearchKeyword = value ?? string.Empty;
			}
		}
	}

	public int SelectedModelDiagramNeighborhoodDepth => GetSelectedModelDiagramState()?.NeighborhoodDepth ?? 1;

	public string SelectedModelDiagramNeighborhoodDepthText =>
		string.Format(CultureInfo.CurrentCulture, UiText.CurrentDepthFormat, SelectedModelDiagramNeighborhoodDepth);

	public double SelectedModelDiagramZoom => GetSelectedModelDiagramState()?.Zoom ?? 1d;

	public string SelectedModelDiagramZoomText => GetSelectedModelDiagramState()?.ZoomText ?? "100%";

	public int SelectedModelDiagramRenderVersion => GetSelectedModelDiagramState()?.RenderVersion ?? 0;

	public double SelectedModelDiagramCanvasWidth
	{
		get
		{
			ModelDiagramWorkspaceState? state = GetSelectedModelDiagramState();
			return state == null ? 1200d : state.CanvasWidth * state.Zoom;
		}
	}

	public double SelectedModelDiagramCanvasHeight
	{
		get
		{
			ModelDiagramWorkspaceState? state = GetSelectedModelDiagramState();
			return state == null ? 720d : state.CanvasHeight * state.Zoom;
		}
	}

	public bool SelectedModelDiagramCanReload => SelectedDocumentIsModelDiagram && SelectedDocumentConnectionProfile != null;

	public bool SelectedModelDiagramCanExport => SelectedDocumentIsModelDiagram && (GetSelectedModelDiagramState()?.Relations.Count ?? 0) > 0;

	public bool SelectedModelDiagramCanExportImage => SelectedDocumentIsModelDiagram && (GetSelectedModelDiagramState()?.VisibleNodes.Count ?? 0) > 0;

	public bool SelectedModelDiagramCanExpandNeighborhood => SelectedDocumentIsModelDiagram && GetSelectedModelDiagramState()?.SelectedTable != null;

	public bool SelectedModelDiagramHasSelectedRelation => GetSelectedModelDiagramState()?.SelectedRelation != null;

	public ObjectEditorState? SelectedObjectEditor => GetSelectedObjectEditorState();

	public string SelectedObjectEditorConnectionName => GetSelectedObjectEditorState()?.ConnectionName ?? string.Empty;

	public string SelectedObjectEditorProviderName => GetSelectedObjectEditorState()?.ProviderName ?? string.Empty;

	public string SelectedObjectEditorSchemaName => GetSelectedObjectEditorState()?.SchemaName ?? string.Empty;

	public string SelectedObjectEditorObjectName => GetSelectedObjectEditorState()?.ObjectName ?? string.Empty;

	public string SelectedObjectEditorObjectType => GetSelectedObjectEditorState()?.ObjectType ?? string.Empty;

	public string SelectedObjectEditorCapabilityText => GetSelectedObjectEditorState()?.CapabilityText ?? UiText.CapabilityUnsupported;

	public string SelectedObjectEditorCommentText => GetSelectedObjectEditorState()?.CommentText ?? string.Empty;

	public string SelectedObjectEditorReturnType => GetSelectedObjectEditorState()?.ReturnType ?? string.Empty;

	public string SelectedObjectEditorMessageText => GetSelectedObjectEditorState()?.MessageText ?? string.Empty;

	public string SelectedObjectEditorPreviewSql => GetSelectedObjectEditorState()?.PreviewSql ?? string.Empty;

	public string SelectedObjectEditorCompileResultText => GetSelectedObjectEditorState()?.CompileResultText ?? string.Empty;

	public bool SelectedObjectEditorIsLoaded => GetSelectedObjectEditorState()?.IsLoaded ?? false;

	public bool SelectedObjectEditorCanRefresh => SelectedDocumentIsObjectEditor && SelectedDocumentConnectionProfile != null;

	public bool SelectedObjectEditorCanPreview => SelectedObjectEditorIsLoaded;

	public bool SelectedObjectEditorCanValidate => SelectedObjectEditorIsLoaded;

	public bool SelectedObjectEditorCanSave =>
		SelectedDocumentIsObjectEditor &&
		SelectedDocumentConnectionProfile != null &&
		(GetSelectedObjectEditorState()?.Capability ?? ObjectEditorCapability.Unsupported) == ObjectEditorCapability.Editable &&
		SelectedDocument?.IsDirty == true;

	public string SelectedDocumentBusyText => SelectedDocumentIsObjectEditor
		? UiText.ProcessingObjectDefinition
		: SelectedDocumentIsCommentMaintenance
			? UiText.LoadingComments
			: UiText.QueryingData;

	public string SelectedDocumentExecutionStatus => SelectedDocumentIsObjectEditor
		? (GetSelectedObjectEditorState()?.MessageText ?? string.Empty)
		: GetSelectedDocumentState().ExecutionStatus;

	public string SelectedDocumentConnectionLabel => GetSelectedDocumentState().ConnectionLabel;

	public string SelectedDocumentConnectionForeground => GetSelectedDocumentState().ConnectionForeground;

	public ConnectionProfile? SelectedDocumentConnectionProfile
	{
		get
		{
			if (SelectedDocument == null || string.IsNullOrWhiteSpace(SelectedDocument.ConnectionProfileId))
			{
				return null;
			}
			return ConnectionProfiles.FirstOrDefault((ConnectionProfile item) => string.Equals(item.Id, SelectedDocument.ConnectionProfileId, StringComparison.OrdinalIgnoreCase));
		}
		set
		{
			if (SelectedDocument != null)
			{
				string text = value?.Id ?? string.Empty;
				if (!string.Equals(SelectedDocument.ConnectionProfileId, text, StringComparison.OrdinalIgnoreCase))
				{
					SelectedDocument.ConnectionProfileId = text;
					UpdateRecentFileDocumentBinding(SelectedDocument);
					UpdateSelectedDocumentConnectionLabel();
					OnPropertyChanged("SelectedDocumentConnectionProfile");
					NotifyWorkbenchContextChanged();
				}
			}
		}
	}

	public bool SelectedCommentWorkspaceOnlyEmpty
	{
		get
		{
			return GetSelectedCommentWorkspaceState()?.OnlyEmpty ?? false;
		}
		set
		{
			if (GetSelectedCommentWorkspaceState() is { } state && state.OnlyEmpty != value)
			{
				state.OnlyEmpty = value;
				NotifySelectedCommentWorkspaceChanged();
			}
		}
	}

	public bool SelectedCommentWorkspaceOnlyChanged
	{
		get
		{
			return GetSelectedCommentWorkspaceState()?.OnlyChanged ?? false;
		}
		set
		{
			if (GetSelectedCommentWorkspaceState() is { } state && state.OnlyChanged != value)
			{
				state.OnlyChanged = value;
				NotifySelectedCommentWorkspaceChanged();
			}
		}
	}

	public string SelectedCommentWorkspaceTableKeyword
	{
		get
		{
			return GetSelectedCommentWorkspaceState()?.TableKeyword ?? string.Empty;
		}
		set
		{
			if (GetSelectedCommentWorkspaceState() is { } state && !string.Equals(state.TableKeyword, value, StringComparison.Ordinal))
			{
				state.TableKeyword = value;
				NotifySelectedCommentWorkspaceChanged();
			}
		}
	}

	public string SelectedCommentWorkspaceColumnKeyword
	{
		get
		{
			return GetSelectedCommentWorkspaceState()?.ColumnKeyword ?? string.Empty;
		}
		set
		{
			if (GetSelectedCommentWorkspaceState() is { } state && !string.Equals(state.ColumnKeyword, value, StringComparison.Ordinal))
			{
				state.ColumnKeyword = value;
				NotifySelectedCommentWorkspaceChanged();
			}
		}
	}

	public string SelectedCommentWorkspaceTableFilter
	{
		get
		{
			return GetSelectedCommentWorkspaceState()?.SelectedTableFilter ?? string.Empty;
		}
		set
		{
			if (GetSelectedCommentWorkspaceState() is { } state && !string.Equals(state.SelectedTableFilter, value, StringComparison.Ordinal))
			{
				state.SelectedTableFilter = value;
				NotifySelectedCommentWorkspaceChanged();
			}
		}
	}

	public string SelectedDocumentDurationText => SelectedDocumentIsObjectEditor ? "--" : GetSelectedDocumentState().DurationText;

	public string SelectedDocumentMessageText => SelectedDocumentIsObjectEditor
		? (GetSelectedObjectEditorState()?.MessageText ?? string.Empty)
		: GetSelectedDocumentState().MessageText;

	public bool SelectedDocumentHasMessage => SelectedDocumentIsObjectEditor
		? !string.IsNullOrWhiteSpace(GetSelectedObjectEditorState()?.MessageText)
		: !string.IsNullOrWhiteSpace(GetSelectedDocumentState().MessageText);

	public QueryExecutionErrorInfo? SelectedDocumentLastExecutionError => GetSelectedDocumentState().LastError;

	public ResultValueDetailState? SelectedDocumentValueDetail => GetSelectedDocumentState().ValueDetail;

	public bool SelectedDocumentValueDetailPanelVisible =>
		SelectedDocumentIsQuery &&
		SelectedDocumentSelectedWorkspaceTabIsResult &&
		GetSelectedDocumentState().IsValueDetailPanelOpen &&
		GetSelectedDocumentState().ValueDetail != null;

	public bool SelectedDocumentValueDetailButtonVisible =>
		SelectedDocumentIsQuery &&
		SelectedDocumentSelectedWorkspaceTabIsResult &&
		!GetSelectedDocumentState().IsValueDetailPanelOpen &&
		GetSelectedDocumentState().ValueDetail != null;

	public ExecutionPlanViewItem? SelectedDocumentExecutionPlan => GetSelectedDocumentState().ExecutionPlan;

	public bool SelectedDocumentHasExecutionPlan => GetSelectedDocumentState().ExecutionPlan?.HasContent ?? false;

	public string SelectedDocumentExecutionPlanSummary => GetSelectedDocumentState().ExecutionPlan?.Summary ?? string.Empty;

	public string SelectedDocumentExecutionPlanText => GetSelectedDocumentState().ExecutionPlan?.FormattedText ?? UiText.PlanEmptyText;

	public string SelectedDocumentExecutionPlanFindingsText
	{
		get
		{
			ExecutionPlanViewItem? executionPlan = GetSelectedDocumentState().ExecutionPlan;
			return (executionPlan != null && executionPlan.HasFindings) ? executionPlan.FindingsText : UiText.PlanNoFindings;
		}
	}

	public bool SelectedDocumentHasExecutionPlanFindings => GetSelectedDocumentState().ExecutionPlan?.HasFindings ?? false;

	public bool SelectedDocumentHasTabularResult
	{
		get
		{
			ResultSetViewItem? selectedResultSet = GetSelectedDocumentState().SelectedResultSet;
			return selectedResultSet != null && !selectedResultSet.IsMessageOnly && selectedResultSet.Columns.Count > 0;
		}
	}

	public bool SelectedDocumentHasMultipleResultSets => GetSelectedDocumentState().ResultSets.Count(ResultSetViewItemFactory.IsTabular) > 1;

	public bool SelectedDocumentShouldShowResultNavigationBar
	{
		get
		{
			DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
			int tabularResultCount = selectedDocumentState.ResultSets.Count(ResultSetViewItemFactory.IsTabular);
			return tabularResultCount > 1 ||
			       !string.IsNullOrWhiteSpace(selectedDocumentState.MessageText) ||
			       selectedDocumentState.ExecutionPlan?.HasContent == true;
		}
	}

	public bool SelectedDocumentShouldShowResultSetNavigator
	{
		get
		{
			DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
			return SelectedDocumentShouldShowResultNavigationBar && selectedDocumentState.ResultSets.Any(ResultSetViewItemFactory.IsTabular);
		}
	}

	public bool SelectedDocumentCanEnterEditMode
	{
		get
		{
			ResultSetViewItem? selectedResultSet = GetSelectedDocumentState().SelectedResultSet;
			return selectedResultSet != null &&
			       selectedResultSet.CanEdit &&
			       !selectedResultSet.IsEditMode &&
			       IsSelectedDocumentConnectionLinked() &&
			       !GetSelectedDocumentState().IsExecuting &&
			       !GetSelectedDocumentState().IsRenderingResults;
		}
	}

	public bool SelectedDocumentIsEditingResult => GetSelectedDocumentState().IsEditingResult;

	public bool SelectedDocumentCanSaveEditedResult
	{
		get
		{
			ResultSetViewItem? selectedResultSet = GetSelectedDocumentState().SelectedResultSet;
			return GetSelectedDocumentState().IsEditingResult &&
			       selectedResultSet != null &&
			       selectedResultSet.HasPendingChanges &&
			       IsSelectedDocumentConnectionLinked();
		}
	}

	public bool SelectedDocumentCanCancelEditedResult => GetSelectedDocumentState().IsEditingResult;

	public bool SelectedDocumentShowEditButton => !GetSelectedDocumentState().IsEditingResult;

	public bool SelectedDocumentShowEditActionButtons => GetSelectedDocumentState().IsEditingResult;

	public string SelectedDocumentEditDisabledReason =>
		GetSelectedDocumentState().SelectedResultSet?.EditDisabledReason ?? string.Empty;

	public bool SelectedDocumentHasPreviewTruncatedResult =>
		GetSelectedDocumentState().SelectedResultSet?.IsPreviewTruncated == true;

	public string SelectedDocumentPreviewTruncatedNotice
	{
		get
		{
			ResultSetViewItem? resultSet = GetSelectedDocumentState().SelectedResultSet;
			if (resultSet == null || !resultSet.IsPreviewTruncated)
			{
				return string.Empty;
			}

			int loadedRows = Math.Max(resultSet.LoadedRowCount, resultSet.PreviewLimit);
			return string.Format(CultureInfo.CurrentCulture, UiText.ResultPreviewTruncatedNoticeFormat, loadedRows);
		}
	}

	public bool SelectedDocumentCanLoadMoreRows
	{
		get
		{
			DocumentExecutionState state = GetSelectedDocumentState();
			// Rendering the current 500 rows should not disable "load more"; only a running query should.
			return SelectedDocumentHasPreviewTruncatedResult &&
			       !state.IsExecuting &&
			       !string.IsNullOrWhiteSpace(state.LastExecutedSql) &&
			       ResolveNextPreviewLimit(state) > state.PreviewRowLimit;
		}
	}

	public string SelectedDocumentLoadMoreRowsText
	{
		get
		{
			DocumentExecutionState state = GetSelectedDocumentState();
			int nextLimit = ResolveNextPreviewLimit(state);
			return nextLimit > state.PreviewRowLimit
				? string.Format(CultureInfo.CurrentCulture, UiText.LoadMoreRowsFormat, nextLimit)
				: UiText.PreviewLimitReachedText;
		}
	}

	public string SelectedDocumentRowCountText => SelectedDocumentIsObjectEditor
		? "--"
		: FormatSelectedDocumentRowCountText();

	public string SelectedDocumentRowCountDisplay => string.Format(UiText.ResultRowCountFormat, SelectedDocumentRowCountText);

	public string SelectedDocumentDurationDisplay => string.Format(UiText.DurationFormat, SelectedDocumentDurationText);

	public ObservableCollection<ResultSetViewItem> SelectedDocumentResultSets => GetSelectedDocumentState().ResultSets;

	public IReadOnlyList<ResultSetViewItem> SelectedDocumentTabularResultSets => GetSelectedDocumentState().ResultSets.Where(ResultSetViewItemFactory.IsTabular).ToArray();

	public string SelectedDocumentLastExecutedSql => GetSelectedDocumentState().LastExecutedSql;

	public int SelectedDocumentLastExecutedSqlBaseOffset => GetSelectedDocumentState().LastExecutedSqlBaseOffset;

	public bool SelectedDocumentLastExecutionIncludedPlan => GetSelectedDocumentState().LastExecutionIncludedPlan;

	public int SelectedDocumentNextPreviewLimit => ResolveNextPreviewLimit(GetSelectedDocumentState());

	public ObservableCollection<ResultWorkspaceTabItem> SelectedDocumentWorkspaceTabs => GetSelectedDocumentState().WorkspaceTabs;

	public ResultSetViewItem? SelectedDocumentSelectedResultSet
	{
		get
		{
			return GetSelectedDocumentState().SelectedResultSet;
		}
		set
		{
			DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
			if (selectedDocumentState.SelectedResultSet != value)
			{
				selectedDocumentState.SelectedResultSet = value;
				selectedDocumentState.IsEditingResult = value?.IsEditMode ?? false;
				selectedDocumentState.ValueDetail = null;
				selectedDocumentState.IsValueDetailPanelOpen = false;
				ResultWorkspaceTabItem? resultWorkspaceTabItem = ((value != null && !value.IsMessageOnly && value.Columns.Count > 0) ? selectedDocumentState.WorkspaceTabs.FirstOrDefault((ResultWorkspaceTabItem item) => item.IsResultTab) : null);
				if (selectedDocumentState.SelectedWorkspaceTab != resultWorkspaceTabItem)
				{
					selectedDocumentState.SelectedWorkspaceTab = resultWorkspaceTabItem;
					OnPropertyChanged("SelectedDocumentSelectedWorkspaceTab");
					OnPropertyChanged("SelectedDocumentSelectedWorkspaceTabIsResult");
					OnPropertyChanged("SelectedDocumentSelectedWorkspaceTabIsMessage");
					OnPropertyChanged("SelectedDocumentSelectedWorkspaceTabIsPlan");
				}
				OnPropertyChanged("SelectedDocumentSelectedResultSet");
				NotifySelectedDocumentValueDetailChanged();
				OnPropertyChanged("SelectedDocumentCanEnterEditMode");
				OnPropertyChanged("SelectedDocumentIsEditingResult");
				OnPropertyChanged("SelectedDocumentShowEditButton");
				OnPropertyChanged("SelectedDocumentShowEditActionButtons");
				OnPropertyChanged("SelectedDocumentCanSaveEditedResult");
				OnPropertyChanged("SelectedDocumentCanCancelEditedResult");
				OnPropertyChanged("SelectedDocumentEditDisabledReason");
				OnPropertyChanged("SelectedDocumentHasPreviewTruncatedResult");
				OnPropertyChanged("SelectedDocumentPreviewTruncatedNotice");
				OnPropertyChanged("SelectedDocumentCanLoadMoreRows");
				OnPropertyChanged("SelectedDocumentLoadMoreRowsText");
				OnPropertyChanged("SelectedDocumentNextPreviewLimit");
				OnPropertyChanged("SelectedDocumentRowCountDisplay");
			}
		}
	}

	public ResultWorkspaceTabItem? SelectedDocumentSelectedWorkspaceTab
	{
		get
		{
			return GetSelectedDocumentState().SelectedWorkspaceTab;
		}
		set
		{
			DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
			if (selectedDocumentState.SelectedWorkspaceTab == value)
			{
				return;
			}
			selectedDocumentState.SelectedWorkspaceTab = value;
			if (value != null && value.IsResultTab)
			{
				ResultSetViewItem? selectedResultSet = selectedDocumentState.SelectedResultSet;
				if (selectedResultSet == null || selectedResultSet.IsMessageOnly || selectedResultSet.Columns.Count == 0)
				{
					ResultSetViewItem? firstTabularResultSet = selectedDocumentState.ResultSets.FirstOrDefault((ResultSetViewItem item) => !item.IsMessageOnly && item.Columns.Count > 0);
					if (selectedDocumentState.SelectedResultSet != firstTabularResultSet)
					{
						selectedDocumentState.SelectedResultSet = firstTabularResultSet;
						OnPropertyChanged("SelectedDocumentSelectedResultSet");
					}
				}
			}
			OnPropertyChanged("SelectedDocumentSelectedWorkspaceTab");
			OnPropertyChanged("SelectedDocumentSelectedWorkspaceTabIsResult");
			OnPropertyChanged("SelectedDocumentSelectedWorkspaceTabIsMessage");
			OnPropertyChanged("SelectedDocumentSelectedWorkspaceTabIsPlan");
			OnPropertyChanged("SelectedDocumentValueDetailPanelVisible");
			OnPropertyChanged("SelectedDocumentValueDetailButtonVisible");
		}
	}

	private string FormatSelectedDocumentRowCountText()
	{
		DocumentExecutionState state = GetSelectedDocumentState();
		if (state.RowCount <= 0)
		{
			return "0";
		}

		string rowCount = state.RowCount.ToString(CultureInfo.CurrentCulture);
		return state.ResultSets.Any(static item => item.IsPreviewTruncated) ? rowCount + "+" : rowCount;
	}

	private static int ResolveNextPreviewLimit(DocumentExecutionState state)
	{
		int currentLimit = state.PreviewRowLimit <= 0 ? DefaultResultPreviewRowLimit : state.PreviewRowLimit;
		if (currentLimit >= MaxResultPreviewRowLimit)
		{
			return currentLimit;
		}

		long doubled = Math.Max(DefaultResultPreviewRowLimit, currentLimit) * 2L;
		return (int)Math.Min(MaxResultPreviewRowLimit, doubled);
	}

	public bool SelectedDocumentSelectedWorkspaceTabIsResult => GetSelectedDocumentState().SelectedWorkspaceTab?.IsResultTab ?? false;

	public bool SelectedDocumentSelectedWorkspaceTabIsMessage => GetSelectedDocumentState().SelectedWorkspaceTab?.IsMessageTab ?? false;

	public bool SelectedDocumentSelectedWorkspaceTabIsPlan => GetSelectedDocumentState().SelectedWorkspaceTab?.IsPlanTab ?? false;

	public void SetSelectedDocumentValueDetail(ResultSetViewItem? resultSet, ResultCellContext? context, int selectedCellCount)
	{
		DocumentExecutionState state = GetSelectedDocumentState();
		state.ValueDetail = ResultValueDetailBuilder.Build(resultSet, context, Math.Max(1, selectedCellCount), UiText);
		NotifySelectedDocumentValueDetailChanged();
	}

	public void OpenSelectedDocumentValueDetailPanel()
	{
		DocumentExecutionState state = GetSelectedDocumentState();
		if (state.ValueDetail == null || state.IsValueDetailPanelOpen)
		{
			return;
		}

		state.IsValueDetailPanelOpen = true;
		NotifySelectedDocumentValueDetailChanged();
	}

	public void CloseSelectedDocumentValueDetailPanel()
	{
		DocumentExecutionState state = GetSelectedDocumentState();
		if (!state.IsValueDetailPanelOpen)
		{
			return;
		}

		state.IsValueDetailPanelOpen = false;
		NotifySelectedDocumentValueDetailChanged();
	}

	public void ClearSelectedDocumentValueDetail()
	{
		DocumentExecutionState state = GetSelectedDocumentState();
		if (state.ValueDetail == null && !state.IsValueDetailPanelOpen)
		{
			return;
		}

		state.ValueDetail = null;
		state.IsValueDetailPanelOpen = false;
		NotifySelectedDocumentValueDetailChanged();
	}

	public ObservableCollection<string> SelectedDocumentSchemas => GetSelectedDocumentState().AvailableSchemas;

	public string SelectedDocumentSchema
	{
		get
		{
			return GetSelectedDocumentState().SelectedSchema;
		}
		set
		{
			if (_isPrioritizingSelectedDocumentSchemas)
			{
				return;
			}

			DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
			string text = NormalizeSchemaDisplaySelection(value);
			if (!string.Equals(selectedDocumentState.SelectedSchema, text, StringComparison.Ordinal))
			{
				selectedDocumentState.SelectedSchema = text;
				if (SelectedDocument != null)
				{
					SelectedDocument.DefaultSchema = NormalizeSchemaSelection(selectedDocumentState.SelectedSchema);
					UpdateRecentFileDocumentBinding(SelectedDocument);
				}
				RememberSelectedDocumentSchema();
				UpdateSelectedDocumentConnectionLabel();
				OnPropertyChanged("SelectedDocumentSchema");
				NotifyWorkbenchContextChanged();
			}
		}
	}

	public string LastResultRowCountText => (LastResultRowCount <= 0) ? "0" : LastResultRowCount.ToString();

	public string SelectedProviderPortHint => (_connectionEditorDraft == null) ? string.Empty : (ConnectionProfileUtilities.GetDefaultPort(_connectionEditorDraft.ProviderName)?.ToString() ?? string.Empty);

	private bool SetViewModelProperty<T>(
		ref T field,
		T value,
		string propertyName,
		Action<T>? afterSetBeforeNotify = null,
		Action<T>? afterNotify = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return false;
		}

		OnPropertyChanging(propertyName);
		field = value;
		afterSetBeforeNotify?.Invoke(value);
		OnPropertyChanged(propertyName);
		// 有些联动依赖“属性已经通知完”这个时机，比如连接编辑面板。
		afterNotify?.Invoke(value);
		return true;
	}

	public string ApplicationTitle
	{
		get => _applicationTitle;
		set => SetViewModelProperty(ref _applicationTitle, value, nameof(ApplicationTitle));
	}

	public string ConnectionStatus
	{
		get => _connectionStatus;
		set => SetViewModelProperty(ref _connectionStatus, value, nameof(ConnectionStatus));
	}

	public string ExecutionStatus
	{
		get => _executionStatus;
		set => SetViewModelProperty(ref _executionStatus, value, nameof(ExecutionStatus));
	}

	public EditorDocument? SelectedDocument
	{
		get => _selectedDocument;
		set => SetViewModelProperty(ref _selectedDocument, value, nameof(SelectedDocument), OnSelectedDocumentChanged);
	}

	public ObjectNode? SelectedObjectNode
	{
		get => _selectedObjectNode;
		set => SetViewModelProperty(ref _selectedObjectNode, value, nameof(SelectedObjectNode));
	}

	public ConnectionProfile? SelectedConnectionProfile
	{
		get => _selectedConnectionProfile;
		set => SetViewModelProperty(
			ref _selectedConnectionProfile,
			value,
			nameof(SelectedConnectionProfile),
			afterNotify: _ =>
			{
				if (_connectionEditorMode == ConnectionEditorModeKind.View)
				{
					ResetConnectionEditorToSelectedProfile();
				}
				else
				{
					NotifyConnectionEditorStateChanged();
				}
			});
	}

	public ConnectionProfile? ActiveConnectionProfile
	{
		get => _activeConnectionProfile;
		set => SetViewModelProperty(ref _activeConnectionProfile, value, nameof(ActiveConnectionProfile));
	}

	public ResultSetViewItem? SelectedResultSet
	{
		get => _selectedResultSet;
		set => SetViewModelProperty(ref _selectedResultSet, value, nameof(SelectedResultSet));
	}

	public string ResultPreview
	{
		get => _resultPreview;
		set => SetViewModelProperty(ref _resultPreview, value, nameof(ResultPreview));
	}

	public string LogPreview
	{
		get => _logPreview;
		set => SetViewModelProperty(ref _logPreview, value, nameof(LogPreview));
	}

	public string SearchText
	{
		get => _searchText;
		set => SetViewModelProperty(ref _searchText, value, nameof(SearchText));
	}

	public string ReplaceText
	{
		get => _replaceText;
		set => SetViewModelProperty(ref _replaceText, value, nameof(ReplaceText));
	}

	public bool IsReplaceVisible
	{
		get => _isReplaceVisible;
		set => SetViewModelProperty(ref _isReplaceVisible, value, nameof(IsReplaceVisible));
	}

	public bool IsSearchVisible
	{
		get => _isSearchVisible;
		set => SetViewModelProperty(ref _isSearchVisible, value, nameof(IsSearchVisible));
	}

	public bool IsCompletionOpen
	{
		get => _isCompletionOpen;
		set => SetViewModelProperty(ref _isCompletionOpen, value, nameof(IsCompletionOpen));
	}

	public CompletionItem? SelectedCompletionItem
	{
		get => _selectedCompletionItem;
		set => SetViewModelProperty(ref _selectedCompletionItem, value, nameof(SelectedCompletionItem));
	}

	public UiTextSet UiText
	{
		get => _uiText;
		set => SetViewModelProperty(ref _uiText, value, nameof(UiText));
	}

	public string CurrentLanguageCode
	{
		get => _currentLanguageCode;
		set => SetViewModelProperty(ref _currentLanguageCode, value, nameof(CurrentLanguageCode), OnCurrentLanguageCodeChanged);
	}

	public bool IsConnectionDialogOpen
	{
		get => _isConnectionDialogOpen;
		set => SetViewModelProperty(ref _isConnectionDialogOpen, value, nameof(IsConnectionDialogOpen));
	}

	public bool IsWordWrapEnabled
	{
		get => _isWordWrapEnabled;
		set => SetViewModelProperty(ref _isWordWrapEnabled, value, nameof(IsWordWrapEnabled));
	}

	public int LastResultRowCount
	{
		get => _lastResultRowCount;
		set => SetViewModelProperty(ref _lastResultRowCount, value, nameof(LastResultRowCount));
	}

	public string LastExecutionTimeText
	{
		get => _lastExecutionTimeText;
		set => SetViewModelProperty(ref _lastExecutionTimeText, value, nameof(LastExecutionTimeText));
	}

	public string ConnectionSearchText
	{
		get => _connectionSearchText;
		set => SetViewModelProperty(ref _connectionSearchText, value, nameof(ConnectionSearchText), OnConnectionSearchTextChanged);
	}

	public string SelectedEnvironmentFilter
	{
		get => _selectedEnvironmentFilter;
		set => SetViewModelProperty(ref _selectedEnvironmentFilter, value, nameof(SelectedEnvironmentFilter), OnSelectedEnvironmentFilterChanged);
	}

	public string SelectedGroupFilter
	{
		get => _selectedGroupFilter;
		set => SetViewModelProperty(ref _selectedGroupFilter, value, nameof(SelectedGroupFilter), OnSelectedGroupFilterChanged);
	}

	public string SelectedCapabilityFilter
	{
		get => _selectedCapabilityFilter;
		set => SetViewModelProperty(ref _selectedCapabilityFilter, value, nameof(SelectedCapabilityFilter), OnSelectedCapabilityFilterChanged);
	}

	public bool FavoritesOnly
	{
		get => _favoritesOnly;
		set => SetViewModelProperty(ref _favoritesOnly, value, nameof(FavoritesOnly), OnFavoritesOnlyChanged);
	}

	public string SelectedResultHeaderMode
	{
		get => _selectedResultHeaderMode;
		set => SetViewModelProperty(ref _selectedResultHeaderMode, value, nameof(SelectedResultHeaderMode), OnSelectedResultHeaderModeChanged);
	}

	public MainWindowViewModel(IDatabaseProviderCatalog providerCatalog, IDatabaseExplorerService databaseExplorerService, ICommentMaintenanceService commentMaintenanceService, IModelDiagramService modelDiagramService, IObjectEditorService objectEditorService, IConnectionProfileStore connectionProfileStore, IEditorSessionStore editorSessionStore, ILocalizationResolver localizationResolver, ISqlExecutionService sqlExecutionService, ISqlFormatterService sqlFormatterService)
	{
		_providerCatalog = providerCatalog;
		_databaseExplorerService = databaseExplorerService;
		_commentMaintenanceService = commentMaintenanceService;
		_modelDiagramService = modelDiagramService;
		_objectEditorService = objectEditorService;
		_connectionProfileStore = connectionProfileStore;
		_editorSessionStore = editorSessionStore;
		_localizationResolver = localizationResolver;
		_sqlExecutionService = sqlExecutionService;
		_sqlFormatterService = sqlFormatterService;
		_completionSnapshots = new CompletionSnapshotCoordinator(databaseExplorerService, RelationLocatorWarmupTimeout);
		_workspaceStates = new DocumentWorkspaceStateStore(
			CommentWorkspaceState_PropertyChanged,
			ModelDiagramState_PropertyChanged,
			ObjectEditorState_PropertyChanged);
		ExplorerNodes = new ObservableCollection<ObjectNode>();
		Documents = new ObservableCollection<EditorDocument>();
		Logs = new ObservableCollection<string>();
		ConnectionProfiles = new ObservableCollection<ConnectionProfile>();
		ResultSets = new ObservableCollection<ResultSetViewItem>();
		CompletionItems = new ObservableCollection<CompletionItem>();
		ApplicationTitle = UiText.ApplicationTitle;
		ExecutionStatus = UiText.ExecutionReady;
		ResultPreview = UiText.ResultPlaceholder;
		LogPreview = UiText.WorkspaceBootstrapped;
		SelectedEnvironmentFilter = UiText.AllEnvironments;
		SelectedGroupFilter = UiText.AllGroups;
		SelectedCapabilityFilter = UiText.AllCapabilities;
	}

	public async Task InitializeAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<ConnectionProfile> profiles = await _connectionProfileStore.LoadAsync(cancellationToken);
		EditorSessionState session = await _editorSessionStore.LoadAsync(cancellationToken);
		RestoreProfiles(profiles);
		RestoreDocuments(session);
		RestoreRecentFiles(session);
		RestoreQueryHistory(session);
		RestoreCompletionUsage(session);
		RestoreRecentSchemas(session);
		SelectedResultHeaderMode = (string.IsNullOrWhiteSpace(session.ResultHeaderMode) ? "Dual" : session.ResultHeaderMode);
		BuildDisconnectedExplorerTree();
		SeedLogs(session);
		UpdateConnectionStatus();
		UpdateResultPreview(UiText.ResultPlaceholder);
	}

	public async Task PersistAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		List<EditorDocument> persistedDocuments = Documents
			.Where(ShouldPersistDocument)
			.Select(EditorDocumentUtilities.Clone)
			.ToList();

		int selectedIndex = 0;
		if (SelectedDocument != null)
		{
			int persistedIndex = Documents
				.Where(ShouldPersistDocument)
				.Select((EditorDocument document, int index) => new { document, index })
				.Where(item => string.Equals(item.document.Id, SelectedDocument.Id, StringComparison.OrdinalIgnoreCase))
				.Select(item => item.index)
				.DefaultIfEmpty(0)
				.First();
			selectedIndex = persistedIndex;
		}

		EditorSessionState state = new EditorSessionState
		{
			SelectedIndex = selectedIndex,
			Documents = persistedDocuments,
			RecentFiles = RecentFiles.ToArray(),
			QueryHistory = QueryHistoryEntries.Take(100).ToArray(),
			RecentCompletionUsage = new Dictionary<string, int>(_recentCompletionUsage, StringComparer.OrdinalIgnoreCase),
			RecentSchemasByConnectionId = new Dictionary<string, string>(_schemaStates.RememberedSchemasByConnectionId, StringComparer.OrdinalIgnoreCase),
			ResultHeaderMode = SelectedResultHeaderMode
		};
		await _editorSessionStore.SaveAsync(state, cancellationToken);
		await _connectionProfileStore.SaveAsync(ConnectionProfiles.Select(CloneConnectionProfile).ToArray(), cancellationToken);
	}

	public Task SaveConnectionsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return _connectionProfileStore.SaveAsync(ConnectionProfiles.Select(CloneConnectionProfile).ToArray(), cancellationToken);
	}

	public void ToggleConnectionDialog(bool? open = null)
	{
		IsConnectionDialogOpen = open ?? (!IsConnectionDialogOpen);
	}
	public EditorDocument CreateDocument(string? title = null, string? content = null)
	{
		(string connectionProfileId, string schemaName) = ResolvePreferredNewQueryContext();
		EditorDocument editorDocument = CreateQueryDocument(
			title,
			content ?? string.Empty,
			connectionProfileId,
			schemaName);
		ExecutionStatus = UiText.ExecutionReady;
		AddLog("Opened " + editorDocument.Title + ".");
		return editorDocument;
	}

	public async Task<EditorDocument> CreateDocumentAsync(string? title = null, string? content = null, CancellationToken cancellationToken = default)
	{
		EditorDocument document = CreateDocument(title, content);
		await EnsureSelectedDocumentContextReadyAsync(cancellationToken);
		return document;
	}

	public EditorDocument OpenQueryHistoryDocument()
	{
		EditorDocument? existing = Documents.FirstOrDefault(item =>
			string.Equals(item.DocumentKind, "QueryHistory", StringComparison.OrdinalIgnoreCase));
		if (existing != null)
		{
			SelectedDocument = existing;
			return existing;
		}

		EditorDocument document = new EditorDocument
		{
			Title = UiText.QueryHistory,
			DocumentKind = "QueryHistory",
			Content = string.Empty
		};
		Documents.Add(document);
		SelectedDocument = document;
		AddLog("Opened query history workspace.");
		return document;
	}

	public EditorDocument OpenQueryFromHistory(QueryHistoryEntry entry)
	{
		if (entry == null)
		{
			throw new ArgumentNullException(nameof(entry));
		}
		EditorDocument document = CreateQueryDocument(
			title: $"History {Documents.Count + 1}",
			content: entry.Sql,
			connectionProfileId: entry.ConnectionProfileId,
			schemaName: entry.SchemaName);
		AddLog("Opened SQL from query history.");
		return document;
	}
	public EditorDocument? OpenCommentMaintenanceDocument()
	{
		if (!TryResolveWorkbenchSchemaContext(out ConnectionProfile profile, out string schemaName))
		{
			return null;
		}

		return OpenCommentMaintenanceDocument(profile, schemaName);
	}

	public EditorDocument? OpenCommentMaintenanceDocument(ObjectNode node)
	{
		ConnectionProfile profile = ResolveMetadataProfile(node);
		string schemaName = string.Equals(node.Type, "schema", StringComparison.OrdinalIgnoreCase)
			? node.Name
			: (string.IsNullOrWhiteSpace(node.SchemaName) ? profile.Schema ?? string.Empty : node.SchemaName);
		if (!IsSchemaOpen(profile, schemaName))
		{
			throw new InvalidOperationException("当前模式未打开。请先在左侧连接树中打开模式。");
		}

		return OpenCommentMaintenanceDocument(profile, schemaName);
	}

	private EditorDocument OpenCommentMaintenanceDocument(ConnectionProfile profile, string schemaName)
	{
		string normalizedSchema = NormalizeSchemaSelection(schemaName);
		SynchronizeWorkbenchSchemaOpenState(profile, normalizedSchema);
		string schemaDisplay = NormalizeSchemaDisplaySelection(normalizedSchema);
		string workspaceKey = WorkspaceDocumentNaming.BuildCommentWorkspaceKey(profile.Id, schemaDisplay);
		EditorDocument? existing = Documents.FirstOrDefault(item =>
			string.Equals(item.DocumentKind, "CommentMaintenance", StringComparison.OrdinalIgnoreCase) &&
			string.Equals(item.WorkspaceKey, workspaceKey, StringComparison.OrdinalIgnoreCase));
		if (existing != null)
		{
			SelectedDocument = existing;
			return existing;
		}

		EditorDocument document = new EditorDocument
		{
			Title = WorkspaceDocumentNaming.BuildCommentMaintenanceTitle(profile.Name, schemaDisplay),
			DocumentKind = "CommentMaintenance",
			WorkspaceKey = workspaceKey,
			ConnectionProfileId = profile.Id,
			DefaultSchema = normalizedSchema,
			Content = string.Empty
		};
		Documents.Add(document);
		SelectedDocument = document;
		AddLog($"Opened comment maintenance workspace for {profile.Name} / {schemaDisplay}.");
		return document;
	}
	public EditorDocument? OpenObjectEditorDocument(ObjectNode node)
	{
		ConnectionProfile profile = ResolveMetadataProfile(node);
		string schemaName = string.IsNullOrWhiteSpace(node.SchemaName) ? profile.Schema ?? string.Empty : node.SchemaName;
		string workspaceKey = WorkspaceDocumentNaming.BuildObjectEditorWorkspaceKey(profile.Id, schemaName, node.Type, node.Name);
		EditorDocument? existing = Documents.FirstOrDefault(item =>
			string.Equals(item.DocumentKind, "ObjectEditor", StringComparison.OrdinalIgnoreCase) &&
			string.Equals(item.WorkspaceKey, workspaceKey, StringComparison.OrdinalIgnoreCase));
		if (existing != null)
		{
			SelectedDocument = existing;
			return existing;
		}

		EditorDocument document = new EditorDocument
		{
			Title = WorkspaceDocumentNaming.BuildObjectEditorTitle(node.Name, node.Type),
			DocumentKind = "ObjectEditor",
			WorkspaceKey = workspaceKey,
			ConnectionProfileId = profile.Id,
			DefaultSchema = schemaName,
			ObjectSchemaName = schemaName,
			ObjectRawName = node.Name,
			ObjectDisplayName = string.IsNullOrWhiteSpace(node.DisplayName) ? node.Name : node.DisplayName,
			ObjectType = node.Type,
			Content = string.Empty
		};
		Documents.Add(document);
		SelectedDocument = document;
		AddLog($"Opened object editor for {schemaName}.{node.Name} ({node.Type}).");
		return document;
	}
	public EditorDocument? OpenModelDiagramDocument()
	{
		if (!TryResolveWorkbenchSchemaContext(out ConnectionProfile profile, out string schemaName))
		{
			return null;
		}

		string workspaceKey = WorkspaceDocumentNaming.BuildModelDiagramWorkspaceKey(profile.Id, schemaName, string.Empty);
		EditorDocument? existing = Documents.FirstOrDefault(item =>
			string.Equals(item.DocumentKind, "ModelDiagram", StringComparison.OrdinalIgnoreCase) &&
			string.Equals(item.WorkspaceKey, workspaceKey, StringComparison.OrdinalIgnoreCase));
		if (existing != null)
		{
			SelectedDocument = existing;
			return existing;
		}

		EditorDocument document = new EditorDocument
		{
			Title = WorkspaceDocumentNaming.BuildModelDiagramTitle(profile.Name, schemaName, string.Empty),
			DocumentKind = "ModelDiagram",
			WorkspaceKey = workspaceKey,
			ConnectionProfileId = profile.Id,
			DefaultSchema = schemaName,
			ModelSchemaName = schemaName,
			ModelFocusTableName = string.Empty,
			Content = string.Empty
		};
		Documents.Add(document);
		SelectedDocument = document;
		AddLog($"Opened model diagram workspace for {profile.Name} / {schemaName}.");
		return document;
	}
	public EditorDocument? OpenModelDiagramDocument(ObjectNode node)
	{
		ConnectionProfile profile = ResolveMetadataProfile(node);
		string schemaName = string.Equals(node.Type, "schema", StringComparison.OrdinalIgnoreCase)
			? node.Name
			: (string.IsNullOrWhiteSpace(node.SchemaName) ? profile.Schema ?? string.Empty : node.SchemaName);
		if (!IsSchemaOpen(profile, schemaName))
		{
			throw new InvalidOperationException("当前模式未打开。请先在左侧连接树中打开模式。");
		}
		string focusTableName = string.Equals(node.Type, "table", StringComparison.OrdinalIgnoreCase) ? node.Name : string.Empty;
		string workspaceKey = WorkspaceDocumentNaming.BuildModelDiagramWorkspaceKey(profile.Id, schemaName, focusTableName);
		EditorDocument? existing = Documents.FirstOrDefault(item =>
			string.Equals(item.DocumentKind, "ModelDiagram", StringComparison.OrdinalIgnoreCase) &&
			string.Equals(item.WorkspaceKey, workspaceKey, StringComparison.OrdinalIgnoreCase));
		if (existing != null)
		{
			SelectedDocument = existing;
			return existing;
		}

		EditorDocument document = new EditorDocument
		{
			Title = WorkspaceDocumentNaming.BuildModelDiagramTitle(profile.Name, schemaName, focusTableName),
			DocumentKind = "ModelDiagram",
			WorkspaceKey = workspaceKey,
			ConnectionProfileId = profile.Id,
			DefaultSchema = schemaName,
			ModelSchemaName = schemaName,
			ModelFocusTableName = focusTableName,
			Content = string.Empty
		};
		Documents.Add(document);
		SelectedDocument = document;
		AddLog(string.IsNullOrWhiteSpace(focusTableName)
			? $"Opened model diagram workspace for {profile.Name} / {schemaName}."
			: $"Opened model diagram workspace for {schemaName}.{focusTableName}.");
		return document;
	}

	public void SyncSelectedDocumentBindingsFromDocument()
	{
		if (SelectedDocument != null)
		{
			DocumentExecutionState documentExecutionState = EnsureDocumentState(SelectedDocument);
			documentExecutionState.SelectedSchema = ResolveInitialSchemaSelection(SelectedDocument);
			UpdateSelectedDocumentConnectionLabel();
			NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		}
	}

	public void CloseDocument(EditorDocument? document)
	{
		if (document == null)
		{
			return;
		}
		_workspaceStates.RemoveWorkspaceStates(document.Id);
		int documentIndex = Documents.IndexOf(document);
		if (documentIndex >= 0)
		{
			Documents.RemoveAt(documentIndex);
			if (Documents.Count == 0)
			{
				CreateDocument();
			}
			else
			{
				SelectedDocument = Documents[Math.Clamp(documentIndex - 1, 0, Documents.Count - 1)];
			}
		}
	}

	public void CreateConnectionProfile()
	{
		BeginCreateConnectionDraft();
	}

	public void BeginCreateConnectionDraft()
	{
		ConnectionProfile connectionProfile = CreateDefaultProfile();
		ConnectionProfileUtilities.ApplyVisuals(connectionProfile);
		SetConnectionEditorMode(ConnectionEditorModeKind.Create);
		SetConnectionEditorDraft(connectionProfile);
		AddLog("Created profile draft " + connectionProfile.Name + ".");
	}

	public void BeginEditConnectionDraft()
	{
		if (SelectedConnectionProfile == null)
		{
			return;
		}

		SetConnectionEditorMode(ConnectionEditorModeKind.Edit);
		SetConnectionEditorDraft(CloneConnectionProfile(SelectedConnectionProfile));
		AddLog("Editing connection profile " + SelectedConnectionProfile.Name + ".");
	}

	public void BeginDuplicateConnectionDraft()
	{
		if (SelectedConnectionProfile == null)
		{
			return;
		}

		ConnectionProfile connectionProfile = CloneConnectionProfile(SelectedConnectionProfile);
		connectionProfile.Id = Guid.NewGuid().ToString("N");
		connectionProfile.Name = string.IsNullOrWhiteSpace(connectionProfile.Name)
			? $"Connection {ConnectionProfiles.Count + 1}"
			: connectionProfile.Name + " Copy";
		connectionProfile.LastUsedAt = null;
		ConnectionProfileUtilities.ApplyVisuals(connectionProfile);
		SetConnectionEditorMode(ConnectionEditorModeKind.Create);
		SetConnectionEditorDraft(connectionProfile);
		AddLog("Created profile copy draft " + connectionProfile.Name + ".");
	}

	public void CancelConnectionEditorDraft()
	{
		ResetConnectionEditorToSelectedProfile();
	}

	public void ResetConnectionEditorToSelectedProfile()
	{
		SetConnectionEditorMode(ConnectionEditorModeKind.View);
		SetConnectionEditorDraft(SelectedConnectionProfile == null ? null : CloneConnectionProfile(SelectedConnectionProfile));
	}

	public void CommitConnectionEditorPassword(string? password)
	{
		if (_connectionEditorDraft != null)
		{
			_connectionEditorDraft.Password = password ?? string.Empty;
		}
	}

	public void ApplyConnectionEditorProvider(DatabaseProviderDefinition provider)
	{
		if (_connectionEditorDraft == null)
		{
			return;
		}

		ConnectionProfileUtilities.ApplyProviderSelection(_connectionEditorDraft, provider);
		NotifyConnectionEditorStateChanged();
		RefreshOracleUiState();
	}

	public bool SaveConnectionEditorDraft()
	{
		if (_connectionEditorDraft == null || _connectionEditorMode == ConnectionEditorModeKind.View)
		{
			return false;
		}

		ConnectionProfileUtilities.NormalizeEditorDraft(_connectionEditorDraft, ConnectionProfiles.Count, UiText.AllEnvironments);
		if (_connectionEditorMode == ConnectionEditorModeKind.Create)
		{
			ConnectionProfile savedProfile = CloneConnectionProfile(_connectionEditorDraft);
			ConnectionProfileUtilities.EnsureUniqueId(savedProfile, ConnectionProfiles);
			ConnectionProfiles.Add(savedProfile);
			SelectedConnectionProfile = savedProfile;
		}
		else if (SelectedConnectionProfile != null)
		{
			CopyConnectionProfileValues(SelectedConnectionProfile, _connectionEditorDraft, preserveId: true);
			ConnectionProfileUtilities.ApplyVisuals(SelectedConnectionProfile);
		}

		BuildDisconnectedExplorerTree();
		NotifyConnectionListsChanged();
		AddLog("Saved connection profile " + (SelectedConnectionProfile?.Name ?? string.Empty) + ".");
		ResetConnectionEditorToSelectedProfile();
		return true;
	}

	public void SaveSelectedConnection()
	{
		if (_connectionEditorMode != ConnectionEditorModeKind.View)
		{
			SaveConnectionEditorDraft();
		}
		else if (SelectedConnectionProfile != null)
		{
			NormalizeOracleSettings(SelectedConnectionProfile);
			ConnectionProfileUtilities.ApplyVisuals(SelectedConnectionProfile);
			if (string.IsNullOrWhiteSpace(SelectedConnectionProfile.Name))
			{
				SelectedConnectionProfile.Name = $"Connection {ConnectionProfiles.Count}";
			}
			BuildDisconnectedExplorerTree();
			NotifyConnectionListsChanged();
			ResetConnectionEditorToSelectedProfile();
			AddLog("Saved connection profile " + SelectedConnectionProfile.Name + ".");
		}
	}

	public void DeleteSelectedConnection()
	{
		if (SelectedConnectionProfile != null)
		{
			string name = SelectedConnectionProfile.Name;
			_connectedConnectionIds.Remove(SelectedConnectionProfile.Id);
			ConnectionProfiles.Remove(SelectedConnectionProfile);
			if (ActiveConnectionProfile != null && string.Equals(ActiveConnectionProfile.Id, SelectedConnectionProfile.Id, StringComparison.OrdinalIgnoreCase))
			{
				ActiveConnectionProfile = ResolveFallbackActiveConnectionProfile(null, SelectedConnectionProfile.Id);
			}
			SelectedConnectionProfile = ConnectionProfiles.FirstOrDefault();
			BuildDisconnectedExplorerTree();
			UpdateConnectionStatus();
			NotifyConnectionListsChanged();
			ResetConnectionEditorToSelectedProfile();
			AddLog("Deleted connection profile " + name + ".");
		}
	}

	public void DuplicateSelectedConnection()
	{
		BeginDuplicateConnectionDraft();
	}

	public async Task<string> ValidateConnectionEditorProfileAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (_connectionEditorDraft == null)
		{
			return UiText.NoConnectionSelected;
		}

		ConnectionProfile profile = CloneConnectionProfile(_connectionEditorDraft);
		ConnectionProfileUtilities.NormalizeEditorDraft(profile, ConnectionProfiles.Count, UiText.AllEnvironments);
		string message = await _databaseExplorerService.ValidateConnectionAsync(profile, cancellationToken);
		AddLog(message);
		return message;
	}

	private void SetConnectionEditorMode(ConnectionEditorModeKind mode)
	{
		if (_connectionEditorMode == mode)
		{
			return;
		}

		_connectionEditorMode = mode;
		NotifyConnectionEditorStateChanged();
	}

	private void SetConnectionEditorDraft(ConnectionProfile? profile)
	{
		_connectionEditorDraft = profile;
		NotifyConnectionEditorStateChanged();
		RefreshOracleUiState();
	}

	private void NotifyConnectionEditorStateChanged()
	{
		OnPropertiesChanged(MainWindowViewModelPropertyGroups.ConnectionEditor);
	}

	private void NotifyConnectionListsChanged()
	{
		OnPropertiesChanged(MainWindowViewModelPropertyGroups.ConnectionLists);
	}

	public void RegisterRecentFile(string filePath, string title, string connectionProfileId = "", string defaultSchema = "")
	{
		if (!string.IsNullOrWhiteSpace(filePath))
		{
			RecentFileEntry? recentFileEntry = RecentFiles.FirstOrDefault((RecentFileEntry item) => string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
			if (recentFileEntry != null)
			{
				RecentFiles.Remove(recentFileEntry);
			}
			RecentFiles.Insert(0, new RecentFileEntry
			{
				FilePath = filePath,
				Title = (string.IsNullOrWhiteSpace(title) ? Path.GetFileName(filePath) : title),
				LastOpenedAt = DateTimeOffset.UtcNow,
				ConnectionProfileId = (connectionProfileId ?? string.Empty),
				DefaultSchema = (defaultSchema ?? string.Empty)
			});
			while (RecentFiles.Count > 15)
			{
				RecentFiles.RemoveAt(RecentFiles.Count - 1);
			}
		}
	}

	public async Task SetActiveConnectionAsync(ConnectionProfile? profile, CancellationToken cancellationToken = default(CancellationToken))
	{
		ConnectionProfile? previousActiveConnection = (ActiveConnectionProfile == null) ? null : CloneConnectionProfile(ActiveConnectionProfile);
		if (profile == null)
		{
			ActiveConnectionProfile = ResolveFallbackActiveConnectionProfile(previousActiveConnection);
			BuildDisconnectedExplorerTree();
			NotifyExplorerFocusChanged();
			UpdateConnectionStatus();
			UpdateSelectedDocumentConnectionLabel();
			return;
		}

		profile.LastUsedAt = DateTimeOffset.UtcNow;
		ConnectionProfileUtilities.ApplyVisuals(profile);
		ConnectionProfile normalizedProfile = CloneConnectionProfile(profile);
		NormalizeOracleSettings(normalizedProfile);
		_localizationResolver.Invalidate(normalizedProfile);
		ActiveConnectionProfile = normalizedProfile;
		_connectedConnectionIds.Add(normalizedProfile.Id);
		UpdateConnectionStatus();
		OnPropertyChanged("FilteredConnectionProfiles");
		OnPropertyChanged("RecentConnectionProfiles");
		OnPropertyChanged("HasRecentConnections");
		BuildDisconnectedExplorerTree();

		ObjectNode? rootNode = FindExplorerConnectionRootNode(normalizedProfile.Id);
		if (rootNode != null)
		{
			rootNode.IsConnected = true;
			rootNode.IsExpanded = true;
			bool isDocumentProvider = string.Equals(_providerCatalog.Find(normalizedProfile.ProviderName)?.Kind, "Document", StringComparison.OrdinalIgnoreCase);
			if (!isDocumentProvider && rootNode.Children.Count == 0)
			{
				rootNode.IsLoaded = false;
				rootNode.HasUnloadedChildren = true;
			}
		}

		try
		{
			if (rootNode != null)
			{
				using CancellationTokenSource timeoutSource = CreateExplorerTimeout(cancellationToken);
				await EnsureNodeChildrenLoadedAsync(rootNode, timeoutSource.Token);
				if (ShouldReloadSchemasForSelectedDocument(normalizedProfile))
				{
					await LoadSchemasForSelectedDocumentAsync(timeoutSource.Token);
				}
			}

			if (string.IsNullOrWhiteSpace(_focusedExplorerConnectionProfileId))
			{
				SetExplorerFocus(rootNode);
			}
			NotifyExplorerFocusChanged();
			UpdateSelectedDocumentConnectionLabel();
			AddLog("Loaded metadata roots for " + normalizedProfile.Name + ".");
			string selectedSchema = NormalizeSchemaSelection(SelectedDocumentSchema);
			if (ShouldReloadSchemasForSelectedDocument(normalizedProfile) && IsSchemaOpen(normalizedProfile, selectedSchema))
			{
				_completionSnapshots.QueueStandardWarmups(CloneConnectionProfile(normalizedProfile), selectedSchema);
			}
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			_connectedConnectionIds.Remove(normalizedProfile.Id);
			ActiveConnectionProfile = ResolveFallbackActiveConnectionProfile(previousActiveConnection, normalizedProfile.Id);
			BuildDisconnectedExplorerTree();
			NotifyExplorerFocusChanged();
			UpdateConnectionStatus();
			UpdateSelectedDocumentConnectionLabel();
			AddLog("Metadata load timed out for " + normalizedProfile.Name + ".");
			throw new TimeoutException($"Connection metadata load timed out after {ExplorerOperationTimeout.TotalSeconds:0} seconds.");
		}
		catch (Exception ex2)
		{
			_connectedConnectionIds.Remove(normalizedProfile.Id);
			ActiveConnectionProfile = ResolveFallbackActiveConnectionProfile(previousActiveConnection, normalizedProfile.Id);
			BuildDisconnectedExplorerTree();
			NotifyExplorerFocusChanged();
			UpdateConnectionStatus();
			UpdateSelectedDocumentConnectionLabel();
			AddLog("Metadata load failed: " + ex2.Message);
			throw;
		}
	}
	public async Task ApplySelectedDocumentConnectionAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		ConnectionProfile? profile = SelectedDocumentConnectionProfile;
		if (profile == null)
		{
			RestoreSelectedDocumentSchemasWithoutConnection();
		}
		else
		{
			profile.LastUsedAt = DateTimeOffset.UtcNow;
			ConnectionProfileUtilities.ApplyVisuals(profile);
			OnPropertyChanged("FilteredConnectionProfiles");
			OnPropertyChanged("RecentConnectionProfiles");
			OnPropertyChanged("HasRecentConnections");
			if (IsConnectionProfileConnected(profile))
			{
				await LoadSchemasForSelectedDocumentAsync(cancellationToken);
				string selectedSchema = NormalizeSchemaSelection(SelectedDocumentSchema);
				if (!string.IsNullOrWhiteSpace(selectedSchema))
				{
					_completionSnapshots.QueueStandardWarmups(CloneConnectionProfile(profile), selectedSchema);
				}
			}
			else
			{
				RestoreSelectedDocumentSchemasWithoutConnection();
			}
		}
		UpdateSelectedDocumentConnectionLabel();
		OnPropertyChanged("SelectedDocumentConnectionProfile");
		NotifyWorkbenchContextChanged();
	}
	public Task DisconnectConnectionAsync(ConnectionProfile? profile)
	{
		if (profile == null)
		{
			return Task.CompletedTask;
		}

		_connectedConnectionIds.Remove(profile.Id);
		RemoveOpenedSchemasForConnection(profile.Id);
		ObjectNode? rootNode = FindExplorerConnectionRootNode(profile.Id);
		if (rootNode != null)
		{
			rootNode.Children.Clear();
			rootNode.IsConnected = false;
			rootNode.IsExpanded = false;
			rootNode.IsLoaded = true;
			rootNode.HasUnloadedChildren = false;
		}

		if (ActiveConnectionProfile != null &&
			string.Equals(ActiveConnectionProfile.Id, profile.Id, StringComparison.OrdinalIgnoreCase))
		{
			ActiveConnectionProfile = ResolveFallbackActiveConnectionProfile(null, profile.Id);
		}

		BuildDisconnectedExplorerTree();
		if (string.Equals(_focusedExplorerConnectionProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
		{
			SetExplorerFocus(FindExplorerConnectionRootNode(profile.Id));
		}
		NotifyExplorerFocusChanged();
		UpdateConnectionStatus();
		UpdateSelectedDocumentConnectionLabel();
		NotifyWorkbenchContextChanged();
		return Task.CompletedTask;
	}

	private bool ShouldReloadSchemasForSelectedDocument(ConnectionProfile profile)
	{
		return SelectedDocumentConnectionProfile != null &&
			string.Equals(SelectedDocumentConnectionProfile.Id, profile.Id, StringComparison.OrdinalIgnoreCase);
	}
	private ConnectionProfile? ResolveFallbackActiveConnectionProfile(ConnectionProfile? preferredProfile, string? excludingConnectionId = null)
	{
		if (preferredProfile != null &&
			_connectedConnectionIds.Contains(preferredProfile.Id) &&
			!string.Equals(preferredProfile.Id, excludingConnectionId, StringComparison.OrdinalIgnoreCase))
		{
			return CloneConnectionProfile(preferredProfile);
		}

		string? nextConnectedId = _connectedConnectionIds
			.FirstOrDefault(item => !string.Equals(item, excludingConnectionId, StringComparison.OrdinalIgnoreCase));
		if (string.IsNullOrWhiteSpace(nextConnectedId))
		{
			return null;
		}

		ConnectionProfile? profile = ConnectionProfiles.FirstOrDefault(item =>
			string.Equals(item.Id, nextConnectedId, StringComparison.OrdinalIgnoreCase));
		return (profile == null) ? null : CloneConnectionProfile(profile);
	}

	private ObjectNode? FindExplorerConnectionRootNode(string? connectionId)
	{
		if (string.IsNullOrWhiteSpace(connectionId))
		{
			return null;
		}

		return ExplorerNodes.FirstOrDefault(node =>
			node.IsConnectionNode &&
			string.Equals(node.Key, "saved-connection:" + connectionId, StringComparison.OrdinalIgnoreCase));
	}

	public async Task EnsureNodeChildrenLoadedAsync(ObjectNode? node, CancellationToken cancellationToken = default(CancellationToken))
	{
		ConnectionProfile? profile = ResolveConnectionProfileForNode(node);
		if (node == null || profile == null || !node.HasUnloadedChildren || node.IsLoaded)
		{
			return;
		}
		if (node.IsSchemaNode && !IsSchemaOpen(profile, node.Name))
		{
			return;
		}
		node.Children.Clear();
		node.Children.Add(new ObjectNode
		{
			Name = "Loading...",
			Type = "status",
			Key = node.Key + ":loading",
			ParentKey = node.Key,
			IsLoaded = true
		});
		try
		{
			using CancellationTokenSource timeoutSource = CreateExplorerTimeout(cancellationToken);
			IReadOnlyList<ObjectNode> children = await _databaseExplorerService.LoadChildNodesAsync(profile, node, timeoutSource.Token);
			node.Children.Clear();
			foreach (ObjectNode child in children)
			{
				node.Children.Add(child);
			}
			ExplorerNodeUtilities.ApplySubtreeState(node, profile.Id, isConnected: true, IsSchemaOpen);
			node.IsLoaded = true;
			node.HasUnloadedChildren = false;
			AddLog($"Loaded {children.Count} child node(s) for {node.Name}.");
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			node.Children.Clear();
			node.Children.Add(new ObjectNode
			{
				Name = $"Load timed out after {ExplorerOperationTimeout.TotalSeconds:0}s",
				Type = "error",
				Key = node.Key + ":timeout",
				ParentKey = node.Key,
				IsLoaded = true
			});
			node.IsLoaded = false;
			node.HasUnloadedChildren = true;
			AddLog("Timed out loading child nodes for " + node.Name + ".");
		}
		catch (Exception ex2)
		{
			node.Children.Clear();
			node.Children.Add(new ObjectNode
			{
				Name = "Load failed: " + ex2.Message,
				Type = "error",
				Key = node.Key + ":error",
				ParentKey = node.Key,
				IsLoaded = true
			});
			node.IsLoaded = false;
			node.HasUnloadedChildren = true;
			AddLog("Failed to load child nodes for " + node.Name + ": " + ex2.Message);
		}
	}

	public Task RetryNodeLoadAsync(ObjectNode? retryNode, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (retryNode == null || string.IsNullOrWhiteSpace(retryNode.ParentKey))
		{
			return Task.CompletedTask;
		}
		ObjectNode? objectNode = ExplorerNodeUtilities.FindNodeByKey(ExplorerNodes, retryNode.ParentKey);
		if (objectNode == null)
		{
			return Task.CompletedTask;
		}
		objectNode.IsLoaded = false;
		objectNode.HasUnloadedChildren = true;
		return EnsureNodeChildrenLoadedAsync(objectNode, cancellationToken);
	}

	private static CancellationTokenSource CreateExplorerTimeout(CancellationToken cancellationToken)
	{
		CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cancellationTokenSource.CancelAfter(ExplorerOperationTimeout);
		return cancellationTokenSource;
	}

	public Task RefreshNodeAsync(ObjectNode? node, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (node == null)
		{
			return Task.CompletedTask;
		}
		if (node.IsRetryNode)
		{
			return RetryNodeLoadAsync(node, cancellationToken);
		}
		if (node.IsConnectionNode)
		{
			ConnectionProfile? profile = ResolveConnectionProfileForNode(node);
			if (profile == null)
			{
				return Task.CompletedTask;
			}

			ObjectNode? rootNode = FindExplorerConnectionRootNode(profile.Id);
			if (rootNode != null)
			{
				rootNode.IsLoaded = false;
				rootNode.HasUnloadedChildren = true;
			}

			return SetActiveConnectionAsync(profile, cancellationToken);
		}
		ObjectNode objectNode = node;
		if (!objectNode.HasUnloadedChildren && !string.IsNullOrWhiteSpace(objectNode.ParentKey))
		{
			ObjectNode? parentNode = ExplorerNodeUtilities.FindNodeByKey(ExplorerNodes, objectNode.ParentKey);
			if (parentNode != null)
			{
				objectNode = parentNode;
			}
		}
		objectNode.IsLoaded = false;
		objectNode.HasUnloadedChildren = true;
		return EnsureNodeChildrenLoadedAsync(objectNode, cancellationToken);
	}

	public void OpenQueryForObject(ObjectNode? node)
	{
		OpenQueryForObject(node, "Select");
	}
	public void OpenQueryForObject(ObjectNode? node, string templateKind)
	{
		string text = SqlTemplateBuilder.ResolveQueryTargetName(node);
		if (!string.IsNullOrWhiteSpace(text))
		{
			ConnectionProfile? profile = ResolveConnectionProfileForNode(node);
			string schemaName = ResolveSchemaNameForNode(node);
			string sql = SqlTemplateBuilder.BuildObjectTemplate(text, templateKind);
			EditorDocument document = CreateQueryDocument(null, sql, profile?.Id ?? string.Empty, schemaName);
			ExecutionStatus = UiText.ExecutionReady;
			AddLog("Opened " + document.Title + ".");
			AddLog("Generated SQL template for " + text + ".");
		}
	}

	public async Task<TableDesignModel> LoadTableDesignAsync(ObjectNode node, CancellationToken cancellationToken = default(CancellationToken))
	{
		ConnectionProfile profile = ResolveMetadataProfile(node);
		TableDesignModel design = await _databaseExplorerService.LoadTableDesignAsync(profile, node.SchemaName, node.Name, cancellationToken);
		AddLog($"Loaded table design for {node.SchemaName}.{node.Name}.");
		return design;
	}

	public async Task SaveTableDesignAsync(ObjectNode node, TableDesignModel originalDesign, TableDesignModel updatedDesign, CancellationToken cancellationToken = default(CancellationToken))
	{
		ConnectionProfile profile = ResolveMetadataProfile(node);
		await _databaseExplorerService.SaveTableDesignAsync(profile, originalDesign, updatedDesign, cancellationToken);
		AddLog($"Saved table design for {node.SchemaName}.{node.Name}.");
	}

	public async Task<string> ExportTableStructureAsync(ObjectNode node, CancellationToken cancellationToken = default(CancellationToken))
	{
		ConnectionProfile profile = ResolveMetadataProfile(node);
		string script = await _databaseExplorerService.ExportTableStructureAsync(profile, node.SchemaName, node.Name, cancellationToken);
		AddLog($"Exported structure for {node.SchemaName}.{node.Name}.");
		return script;
	}

	public async Task<string> ExportTableDataAsync(ObjectNode node, CancellationToken cancellationToken = default(CancellationToken))
	{
		ConnectionProfile profile = ResolveMetadataProfile(node);
		string script = await _databaseExplorerService.ExportTableDataAsync(profile, node.SchemaName, node.Name, cancellationToken);
		AddLog($"Exported data for {node.SchemaName}.{node.Name}.");
		return script;
	}

	public ConnectionProfile? ResolveConnectionProfileForNode(ObjectNode? node)
	{
		if (node == null)
		{
			return null;
		}

		if (!string.IsNullOrWhiteSpace(node.ConnectionProfileId))
		{
			ConnectionProfile? directProfile = ConnectionProfiles.FirstOrDefault(item => string.Equals(item.Id, node.ConnectionProfileId, StringComparison.OrdinalIgnoreCase));
			if (directProfile != null)
			{
				return CloneConnectionProfile(directProfile);
			}
		}

		ObjectNode? currentNode = node;
		while (currentNode != null)
		{
			if (ExplorerNodeUtilities.TryGetConnectionIdFromNodeKey(currentNode.Key, out string connectionId))
			{
				ConnectionProfile? connectionProfile = ConnectionProfiles.FirstOrDefault(item => string.Equals(item.Id, connectionId, StringComparison.OrdinalIgnoreCase));
				return (connectionProfile == null) ? null : CloneConnectionProfile(connectionProfile);
			}

			if (string.IsNullOrWhiteSpace(currentNode.ParentKey))
			{
				break;
			}

			currentNode = ExplorerNodeUtilities.FindNodeByKey(ExplorerNodes, currentNode.ParentKey);
		}

		return (ActiveConnectionProfile == null) ? null : CloneConnectionProfile(ActiveConnectionProfile);
	}

	private ConnectionProfile ResolveMetadataProfile(ObjectNode? node)
	{
		if (node == null)
		{
			throw new InvalidOperationException("No database object selected.");
		}
		ConnectionProfile? profile = ResolveConnectionProfileForNode(node);
		if (profile == null)
		{
			throw new InvalidOperationException("No database connection available for the selected object.");
		}
		return profile;
	}

	public async Task<string> ValidateSelectedConnectionAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (SelectedConnectionProfile == null)
		{
			return "No connection profile selected.";
		}
		ConnectionProfile profile = CloneConnectionProfile(SelectedConnectionProfile);
		NormalizeOracleSettings(profile);
		string message = await _databaseExplorerService.ValidateConnectionAsync(profile, cancellationToken);
		AddLog(message);
		return message;
	}

	public async Task ExecuteSelectedDocumentAsync(
		string sql,
		bool includePlan,
		int sqlBaseOffset = 0,
		int maxPreviewRows = DefaultResultPreviewRowLimit,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		if (SelectedDocument != null)
		{
			DocumentExecutionState state = GetSelectedDocumentState();
			int previewRowLimit = Math.Clamp(maxPreviewRows <= 0 ? DefaultResultPreviewRowLimit : maxPreviewRows, 1, MaxResultPreviewRowLimit);
			state.IsExecuting = true;
			state.ExecutionStatus = (includePlan ? UiText.ExecutionPlanRunning : UiText.ExecutionRunning);
			state.ResultSets.Clear();
			state.SelectedResultSet = null;
			state.RowCount = 0;
			state.DurationText = "--";
			state.MessageText = string.Empty;
			state.LastExecutedSql = sql;
			state.LastExecutedSqlBaseOffset = Math.Max(0, sqlBaseOffset);
			state.LastExecutionIncludedPlan = includePlan;
			state.PreviewRowLimit = previewRowLimit;
			state.LastError = null;
			state.ValueDetail = null;
			state.IsValueDetailPanelOpen = false;
			UpdateSelectedDocumentConnectionLabel();
			NotifySelectedDocumentStateChanged();
			Stopwatch executeStopwatch = Stopwatch.StartNew();
			string documentTitle = SelectedDocument.Title;
			string connectionName = SelectedDocumentConnectionProfile?.Name ?? "(no-connection)";
			string schemaName = NormalizeSchemaSelection(state.SelectedSchema);
			AppendExecutionPipelineLog($"ExecuteSelectedDocumentAsync:start; doc={documentTitle}; connection={connectionName}; schema={schemaName}; includePlan={includePlan}; sqlLength={sql.Length}");
			Stopwatch serviceStopwatch = Stopwatch.StartNew();
			QueryExecutionResult result = await _sqlExecutionService.ExecuteAsync(new QueryExecutionRequest
			{
				Connection = ((SelectedDocumentConnectionProfile == null) ? null : CloneConnectionProfile(SelectedDocumentConnectionProfile)),
				Sql = sql,
				SqlBaseOffset = state.LastExecutedSqlBaseOffset,
				Schema = schemaName,
				IncludeExecutionPlan = includePlan,
				MaxPreviewRows = previewRowLimit
			}, cancellationToken);
			serviceStopwatch.Stop();
			AppendExecutionPipelineLog($"ExecuteSelectedDocumentAsync:service-returned; doc={documentTitle}; elapsedMs={serviceStopwatch.ElapsedMilliseconds}; resultSets={result.ResultSets.Count}; durationMs={result.Duration.TotalMilliseconds:0}; summary={result.Summary}");
			Stopwatch buildStopwatch = Stopwatch.StartNew();
			string resultProviderName = SelectedDocumentConnectionProfile?.ProviderName ?? ActiveConnectionProfile?.ProviderName ?? string.Empty;
			string resultNavigationLabel = UiText.Results;
			UiTextSet uiText = UiText;
			// Building cell view models can be expensive for wide result sets, so keep it off the UI thread.
			ApplyExecutionResultToState(builtResultSets: await Task.Run(() => ResultSetViewItemFactory.BuildItems(result.ResultSets, resultProviderName, resultNavigationLabel, uiText, AppendExecutionPipelineLog), cancellationToken), state: state, result: result, includePlan: includePlan);
			buildStopwatch.Stop();
			AppendExecutionPipelineLog($"ExecuteSelectedDocumentAsync:state-built; doc={documentTitle}; elapsedMs={buildStopwatch.ElapsedMilliseconds}; stateResultSets={state.ResultSets.Count}; rowCount={state.RowCount}");
			Stopwatch enrichStopwatch = Stopwatch.StartNew();
			await EnrichResultColumnCommentsAsync(state.ResultSets, SelectedDocumentConnectionProfile, schemaName, cancellationToken);
			enrichStopwatch.Stop();
			AppendExecutionPipelineLog($"ExecuteSelectedDocumentAsync:comments-enriched; doc={documentTitle}; elapsedMs={enrichStopwatch.ElapsedMilliseconds}; resultSets={state.ResultSets.Count}");
			state.ExecutionStatus = UiText.ExecutionCompleted;
			state.IsExecuting = false;
			NotifySelectedDocumentStateChanged();
			if (!string.IsNullOrWhiteSpace(state.MessageText))
			{
				AddLog("Execution message: " + state.MessageText);
			}
			AddLog(result.Summary);
			RecordSelectedDocumentExecutionHistory(sql, includePlan, isSuccess: true, result.Summary, state.RowCount, state.DurationText);
			AppendExecutionPipelineLog($"ExecuteSelectedDocumentAsync:complete; doc={documentTitle}; elapsedMs={executeStopwatch.ElapsedMilliseconds}");
		}
	}
	public void RecordSelectedDocumentExecutionHistory(
		string sql,
		bool includePlan,
		bool isSuccess,
		string summary,
		int rowCount,
		string durationText)
	{
		if (string.IsNullOrWhiteSpace(sql) || !SelectedDocumentIsQuery)
		{
			return;
		}

		DocumentExecutionState state = GetSelectedDocumentState();
		ConnectionProfile? profile = SelectedDocumentConnectionProfile;
		QueryHistoryEntry entry = new()
		{
			ExecutedAt = DateTimeOffset.UtcNow,
			ConnectionProfileId = profile?.Id ?? string.Empty,
			ConnectionName = profile?.Name ?? string.Empty,
			ProviderName = profile?.ProviderName ?? string.Empty,
			SchemaName = NormalizeSchemaSelection(state.SelectedSchema),
			Sql = sql,
			IncludePlan = includePlan,
			IsSuccess = isSuccess,
			RowCount = rowCount,
			DurationText = string.IsNullOrWhiteSpace(durationText) ? "--" : durationText,
			Summary = summary ?? string.Empty
		};

		QueryHistoryEntries.Insert(0, entry);
		while (QueryHistoryEntries.Count > 100)
		{
			QueryHistoryEntries.RemoveAt(QueryHistoryEntries.Count - 1);
		}

		OnPropertyChanged(nameof(QueryHistoryEntries));
	}

	public bool IsSelectedDocumentConnectionLinked()
	{
		ConnectionProfile? selectedDocumentConnectionProfile = SelectedDocumentConnectionProfile;
		if (selectedDocumentConnectionProfile == null)
		{
			return false;
		}
		if (!IsConnectionProfileConnected(selectedDocumentConnectionProfile))
		{
			return false;
		}

		return true;
	}

	public void BlockSelectedDocumentExecutionBecauseConnectionNotLinked()
	{
		DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
		selectedDocumentState.ResultSets.Clear();
		selectedDocumentState.WorkspaceTabs.Clear();
		selectedDocumentState.SelectedResultSet = null;
		selectedDocumentState.SelectedWorkspaceTab = null;
		selectedDocumentState.RowCount = 0;
		selectedDocumentState.DurationText = "--";
		selectedDocumentState.MessageText = string.Empty;
		selectedDocumentState.ExecutionPlan = null;
		selectedDocumentState.IsExecuting = false;
		selectedDocumentState.IsRenderingResults = false;
		selectedDocumentState.ExecutionStatus = ResolveSelectedDocumentExecutionBlockReason();
		UpdateSelectedDocumentConnectionLabel();
		NotifySelectedDocumentStateChanged();
		UpdateResultPreview(UiText.ResultPlaceholder);
		AddLog("Skipped execution because the selected document connection is not linked.");
	}

	public void BeginSelectedDocumentExecution(CancellationTokenSource cancellationTokenSource)
	{
		DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
		selectedDocumentState.CancellationTokenSource?.Dispose();
		selectedDocumentState.CancellationTokenSource = cancellationTokenSource;
	}

	public void CompleteSelectedDocumentExecution()
	{
		DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
		bool wasExecuting = selectedDocumentState.IsExecuting || selectedDocumentState.CancellationTokenSource != null;
		selectedDocumentState.IsExecuting = false;
		selectedDocumentState.CancellationTokenSource?.Dispose();
		selectedDocumentState.CancellationTokenSource = null;
		if (wasExecuting)
		{
			NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		}
	}

	public void SetSelectedDocumentRendering(bool isRendering)
	{
		DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
		if (selectedDocumentState.IsRenderingResults != isRendering)
		{
			selectedDocumentState.IsRenderingResults = isRendering;
			NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		}
	}

	public bool ConsumeSelectedDocumentResultScrollResetPending()
	{
		DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
		bool resetResultScrollOnNextRender = selectedDocumentState.ResetResultScrollOnNextRender;
		selectedDocumentState.ResetResultScrollOnNextRender = false;
		return resetResultScrollOnNextRender;
	}

	public void CancelSelectedDocumentExecution()
	{
		DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
		selectedDocumentState.CancellationTokenSource?.Cancel();
		selectedDocumentState.ExecutionStatus = UiText.ExecutionCancelling;
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
	}

	public void SetSelectedDocumentResultWorkspaceOpen(bool isOpen)
	{
		if (SelectedDocument == null)
		{
			return;
		}

		DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
		if (selectedDocumentState.IsResultWorkspaceOpen == isOpen)
		{
			return;
		}

		selectedDocumentState.IsResultWorkspaceOpen = isOpen;
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
	}

	public void ToggleSelectedDocumentResultWorkspace()
	{
		if (SelectedDocument == null)
		{
			return;
		}

		DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
		SetSelectedDocumentResultWorkspaceOpen(!selectedDocumentState.IsResultWorkspaceOpen);
	}

	public void BeginSelectedResultEdit()
	{
		DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
		ResultSetViewItem? selectedResultSet = selectedDocumentState.SelectedResultSet;
		if (selectedResultSet == null || !selectedResultSet.CanEdit)
		{
			return;
		}

		selectedResultSet.IsEditMode = true;
		selectedDocumentState.IsEditingResult = true;
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
	}

	public void CancelSelectedResultEdit()
	{
		DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
		ResultSetViewItem? selectedResultSet = selectedDocumentState.SelectedResultSet;
		if (selectedResultSet == null)
		{
			return;
		}

		selectedResultSet.ResetPendingChanges();
		selectedResultSet.IsEditMode = false;
		selectedDocumentState.IsEditingResult = false;
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
	}

	public void RefreshSelectedResultEditState()
	{
		OnPropertyChanged("SelectedDocumentCanSaveEditedResult");
		OnPropertyChanged("SelectedDocumentCanCancelEditedResult");
	}

	public async Task<EditableResultMutationResult?> SaveSelectedResultEditAsync(CancellationToken cancellationToken = default)
	{
		DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
		ResultSetViewItem? selectedResultSet = selectedDocumentState.SelectedResultSet;
		ConnectionProfile? connectionProfile = SelectedDocumentConnectionProfile ?? ActiveConnectionProfile;
		if (selectedResultSet == null || !selectedResultSet.IsEditMode || connectionProfile == null)
		{
			return null;
		}

		EditableResultRowMutation[] changedRows = EditableResultMutationBuilder.BuildChangedRows(selectedResultSet);
		if (changedRows.Length == 0)
		{
			return new EditableResultMutationResult
			{
				AffectedRows = 0,
				Summary = "当前没有需要保存的修改。"
			};
		}

		EditableResultSaveRequest request = new EditableResultSaveRequest
		{
			Connection = connectionProfile,
			Schema = EditableResultMutationBuilder.ResolveSchema(selectedDocumentState, selectedResultSet),
			TableName = selectedResultSet.BaseTableName ?? string.Empty,
			Columns = EditableResultMutationBuilder.BuildColumns(selectedResultSet),
			Rows = changedRows
		};

		EditableResultMutationResult result = await _sqlExecutionService.SaveEditableResultAsync(request, cancellationToken);
		foreach (EditableResultRowMutation changedRow in changedRows)
		{
			if (changedRow.RowIndex >= 0 && changedRow.RowIndex < selectedResultSet.Rows.Count)
			{
				selectedResultSet.Rows[changedRow.RowIndex].AcceptChanges(
					selectedResultSet.Rows[changedRow.RowIndex].Values.Cast<object?>().ToArray());
			}
		}

		selectedResultSet.IsEditMode = false;
		selectedDocumentState.IsEditingResult = false;
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		return result;
	}

	public async Task<EditableResultMutationResult?> DeleteSelectedResultRowsAsync(IReadOnlyList<ResultRowViewItem> rows, CancellationToken cancellationToken = default)
	{
		DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
		ResultSetViewItem? selectedResultSet = selectedDocumentState.SelectedResultSet;
		ConnectionProfile? connectionProfile = SelectedDocumentConnectionProfile ?? ActiveConnectionProfile;
		if (selectedResultSet == null || !selectedResultSet.CanDeleteRows || connectionProfile == null || rows.Count == 0)
		{
			return null;
		}

		EditableResultRowMutation[] rowMutations = rows
			.Select(row => EditableResultMutationBuilder.BuildRow(selectedResultSet, row))
			.Where(static item => item != null)
			.Cast<EditableResultRowMutation>()
			.ToArray();

		if (rowMutations.Length == 0)
		{
			return null;
		}

		EditableResultDeleteRequest request = new EditableResultDeleteRequest
		{
			Connection = connectionProfile,
			Schema = EditableResultMutationBuilder.ResolveSchema(selectedDocumentState, selectedResultSet),
			TableName = selectedResultSet.BaseTableName ?? string.Empty,
			Columns = EditableResultMutationBuilder.BuildColumns(selectedResultSet),
			Rows = rowMutations
		};

		EditableResultMutationResult result = await _sqlExecutionService.DeleteEditableResultRowsAsync(request, cancellationToken);
		foreach (ResultRowViewItem row in rows.ToArray())
		{
			selectedResultSet.Rows.Remove(row);
		}

		selectedDocumentState.RowCount = selectedDocumentState.ResultSets.Sum(item => item.Rows.Count);
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		return result;
	}

	public async Task LoadSchemasForSelectedDocumentAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		DocumentExecutionState state = GetSelectedDocumentState();
		ConnectionProfile? documentConnection = SelectedDocumentConnectionProfile;
		if (documentConnection == null || !IsConnectionProfileConnected(documentConnection))
		{
			RestoreSelectedDocumentSchemasWithoutConnection(state);
			return;
		}
		IReadOnlyList<string> schemas = await _databaseExplorerService.LoadSchemasAsync(documentConnection, cancellationToken);
		string currentSchema = NormalizeSchemaSelection(state.SelectedSchema);
		string preferredSchema = ResolveInitialSchemaSelection(SelectedDocument);
		ResetAvailableSchemas(state, OrderSchemasForDocument(documentConnection, schemas, currentSchema, preferredSchema));
		state.AvailableSchemasConnectionProfileId = documentConnection.Id;
		if (state.AvailableSchemas.Count > 0)
		{
			if (ContainsSchemaSelection(state.AvailableSchemas, preferredSchema))
			{
				state.SelectedSchema = preferredSchema;
			}
			else if (!string.IsNullOrWhiteSpace(currentSchema) && ContainsSchemaSelection(state.AvailableSchemas, currentSchema))
			{
				state.SelectedSchema = currentSchema;
			}
			else if (string.IsNullOrWhiteSpace(state.SelectedSchema) || !ContainsSchemaSelection(state.AvailableSchemas, state.SelectedSchema))
			{
				state.SelectedSchema = "(Default)";
			}
		}
		else
		{
			state.SelectedSchema = "(Default)";
		}
		if (SelectedDocument != null)
		{
			SelectedDocument.DefaultSchema = NormalizeSchemaSelection(state.SelectedSchema);
			UpdateRecentFileDocumentBinding(SelectedDocument);
		}
		UpdateSelectedDocumentConnectionLabel();
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		NotifyWorkbenchContextChanged();
	}
	private void RememberSelectedDocumentSchema()
	{
		ConnectionProfile? profile = SelectedDocumentConnectionProfile;
		string schema = NormalizeSchemaSelection(GetSelectedDocumentState().SelectedSchema);
		if (profile != null && !string.IsNullOrWhiteSpace(schema))
		{
			_schemaStates.RememberSchema(profile.Id, schema);
		}
	}
	public void PrioritizeSelectedDocumentSchemas(ConnectionProfile? changedProfile = null)
	{
		DocumentExecutionState state = GetSelectedDocumentState();
		ConnectionProfile? profile = SelectedDocumentConnectionProfile;
		if (profile == null ||
			state.AvailableSchemas.Count <= 1 ||
			!string.Equals(state.AvailableSchemasConnectionProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		if (changedProfile != null &&
			!string.Equals(changedProfile.Id, profile.Id, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		string selectedSchema = NormalizeSchemaSelection(state.SelectedSchema);
		string preferredSchema = ResolveInitialSchemaSelection(SelectedDocument);
		string stableSelectedSchema = state.SelectedSchema;
		// 调整下拉顺序时不顺手改选中项，用户刚选的 schema 要稳住。
		_isPrioritizingSelectedDocumentSchemas = true;
		try
		{
			ApplyAvailableSchemaOrder(
				state,
				OrderSchemasForDocument(profile, state.AvailableSchemas, selectedSchema, preferredSchema));
			state.SelectedSchema = stableSelectedSchema;
			if (SelectedDocument != null)
			{
				SelectedDocument.DefaultSchema = NormalizeSchemaSelection(state.SelectedSchema);
				UpdateRecentFileDocumentBinding(SelectedDocument);
			}
		}
		finally
		{
			_isPrioritizingSelectedDocumentSchemas = false;
		}
		OnPropertyChanged("SelectedDocumentSchemas");
		OnPropertyChanged("SelectedDocumentSchema");
	}
	private static void ResetAvailableSchemas(DocumentExecutionState state, IEnumerable<string> orderedSchemas)
	{
		state.AvailableSchemas.Clear();
		state.AvailableSchemas.Add(DefaultSchemaOption);
		foreach (string schema in orderedSchemas)
		{
			string normalizedSchema = NormalizeSchemaSelection(schema);
			if (!string.IsNullOrWhiteSpace(normalizedSchema) &&
				!ContainsSchemaSelection(state.AvailableSchemas, normalizedSchema))
			{
				state.AvailableSchemas.Add(normalizedSchema);
			}
		}
	}
	private static void ApplyAvailableSchemaOrder(DocumentExecutionState state, IEnumerable<string> orderedSchemas)
	{
		if (state.AvailableSchemas.Count == 0)
		{
			state.AvailableSchemas.Add(DefaultSchemaOption);
		}
		if (!string.Equals(state.AvailableSchemas[0], DefaultSchemaOption, StringComparison.OrdinalIgnoreCase))
		{
			int defaultIndex = state.AvailableSchemas
				.Select((item, index) => new { item, index })
				.FirstOrDefault(item => string.Equals(item.item, DefaultSchemaOption, StringComparison.OrdinalIgnoreCase))?.index ?? -1;
			if (defaultIndex >= 0)
			{
				state.AvailableSchemas.Move(defaultIndex, 0);
			}
			else
			{
				state.AvailableSchemas.Insert(0, DefaultSchemaOption);
			}
		}

		int targetIndex = 1;
		foreach (string schema in orderedSchemas)
		{
			string normalizedSchema = NormalizeSchemaSelection(schema);
			if (string.IsNullOrWhiteSpace(normalizedSchema))
			{
				continue;
			}

			int currentIndex = -1;
			for (int i = 1; i < state.AvailableSchemas.Count; i++)
			{
				if (string.Equals(state.AvailableSchemas[i], normalizedSchema, StringComparison.OrdinalIgnoreCase))
				{
					currentIndex = i;
					break;
				}
			}
			if (currentIndex < 0)
			{
				continue;
			}
			if (currentIndex != targetIndex)
			{
				state.AvailableSchemas.Move(currentIndex, targetIndex);
			}
			targetIndex++;
		}
	}
	private IReadOnlyList<string> OrderSchemasForDocument(
		ConnectionProfile profile,
		IEnumerable<string> schemas,
		params string[] preferredSchemas)
	{
		List<string> originalOrder = [];
		HashSet<string> seenSchemas = new(StringComparer.OrdinalIgnoreCase);
		foreach (string schema in schemas)
		{
			string normalizedSchema = NormalizeSchemaSelection(schema);
			if (!string.IsNullOrWhiteSpace(normalizedSchema) && seenSchemas.Add(normalizedSchema))
			{
				originalOrder.Add(normalizedSchema);
			}
		}

		List<string> priorityNames = [];
		void AddPriority(string? schema)
		{
			string normalizedSchema = NormalizeSchemaSelection(schema);
			if (!string.IsNullOrWhiteSpace(normalizedSchema) &&
				!priorityNames.Any(item => string.Equals(item, normalizedSchema, StringComparison.OrdinalIgnoreCase)))
			{
				priorityNames.Add(normalizedSchema);
			}
		}

		foreach (string schema in preferredSchemas)
		{
			AddPriority(schema);
		}

		if (_schemaStates.TryGetRememberedSchema(profile.Id, out string rememberedSchema))
		{
			AddPriority(rememberedSchema);
		}

		AddPriority(profile.Schema);
		AddPriority(ResolvePreferredSchema(profile, originalOrder));
		foreach (string openedSchema in _schemaStates.GetOpenedSchemasForConnection(profile.Id))
		{
			AddPriority(openedSchema);
		}

		List<string> orderedSchemas = [];
		foreach (string priorityName in priorityNames)
		{
			string? actualSchema = originalOrder.FirstOrDefault(item => string.Equals(item, priorityName, StringComparison.OrdinalIgnoreCase));
			if (!string.IsNullOrWhiteSpace(actualSchema) &&
				!orderedSchemas.Any(item => string.Equals(item, actualSchema, StringComparison.OrdinalIgnoreCase)))
			{
				orderedSchemas.Add(actualSchema);
			}
		}

		foreach (string schema in originalOrder)
		{
			if (!orderedSchemas.Any(item => string.Equals(item, schema, StringComparison.OrdinalIgnoreCase)))
			{
				orderedSchemas.Add(schema);
			}
		}

		return orderedSchemas;
	}

	private void RestoreSelectedDocumentSchemasWithoutConnection()
	{
		RestoreSelectedDocumentSchemasWithoutConnection(GetSelectedDocumentState());
	}

	private void RestoreSelectedDocumentSchemasWithoutConnection(DocumentExecutionState state)
	{
		state.AvailableSchemas.Clear();
		state.AvailableSchemas.Add("(Default)");
		state.AvailableSchemasConnectionProfileId = string.Empty;
		string preferredSchema = ResolveInitialSchemaSelection(SelectedDocument);
		if (!string.IsNullOrWhiteSpace(preferredSchema) &&
			!ContainsSchemaSelection(state.AvailableSchemas, preferredSchema))
		{
			state.AvailableSchemas.Add(preferredSchema);
		}

		state.SelectedSchema = ContainsSchemaSelection(state.AvailableSchemas, preferredSchema)
			? preferredSchema
			: "(Default)";
		if (SelectedDocument != null)
		{
			SelectedDocument.DefaultSchema = NormalizeSchemaSelection(state.SelectedSchema);
			UpdateRecentFileDocumentBinding(SelectedDocument);
		}

		UpdateSelectedDocumentConnectionLabel();
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		NotifyWorkbenchContextChanged();
	}

	public async Task EnsureSelectedDocumentContextReadyAsync(CancellationToken cancellationToken = default)
	{
		if (!SelectedDocumentIsQuery || SelectedDocument == null)
		{
			NotifyWorkbenchContextChanged();
			return;
		}

		DocumentExecutionState state = GetSelectedDocumentState();
		ConnectionProfile? profile = SelectedDocumentConnectionProfile;
		if (profile == null || !IsConnectionProfileConnected(profile))
		{
			RestoreSelectedDocumentSchemasWithoutConnection(state);
			return;
		}

		if (!string.Equals(state.AvailableSchemasConnectionProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
		{
			await LoadSchemasForSelectedDocumentAsync(cancellationToken);
			return;
		}

		UpdateSelectedDocumentConnectionLabel();
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		NotifyWorkbenchContextChanged();
	}
	public async Task LoadSelectedCommentWorkspaceAsync(CancellationToken cancellationToken = default)
	{
		if (!SelectedDocumentIsCommentMaintenance || SelectedDocument == null)
		{
			return;
		}

		EditorDocument document = SelectedDocument;
		CommentMaintenanceWorkspaceState state = EnsureCommentWorkspaceState(document);
		if (state.IsBusy)
		{
			return;
		}

		ConnectionProfile? profile = SelectedDocumentConnectionProfile;
		if (profile == null)
		{
			throw new InvalidOperationException("当前工作台未绑定数据库连接。");
		}

		string schema = ResolveWorkbenchDocumentSchema(document, profile);
		if (!IsConnectionProfileConnected(profile))
		{
			throw new InvalidOperationException("当前工作台绑定的数据库未连接。");
		}
		if (!SynchronizeWorkbenchSchemaOpenState(profile, schema))
		{
			throw new InvalidOperationException("当前工作台绑定的模式未打开。");
		}
		state.IsBusy = true;
		NotifySelectedCommentWorkspaceChanged();
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		try
		{
			ConnectionProfile profileSnapshot = CloneConnectionProfile(profile);
			CommentMaintenanceWorkspace workspace = await Task.Run(
				async () => await _commentMaintenanceService.LoadWorkspaceAsync(profileSnapshot, schema, cancellationToken),
				cancellationToken);
			state.Load(workspace);
			state.SetLoadSummary();
			document.DefaultSchema = workspace.SchemaName;
			document.Title = WorkspaceDocumentNaming.BuildCommentMaintenanceTitle(profile.Name, NormalizeSchemaDisplaySelection(workspace.SchemaName));
			UpdateRecentFileDocumentBinding(document);
			NotifySelectedCommentWorkspaceChanged();
			AddLog($"Loaded comment maintenance workspace for {profile.Name} / {workspace.SchemaName}.");
		}
		finally
		{
			state.IsBusy = false;
			NotifySelectedCommentWorkspaceChanged();
			NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		}
	}
	private string ResolveWorkbenchDocumentSchema(EditorDocument document, ConnectionProfile profile)
	{
		string schema = NormalizeSchemaSelection(document.DefaultSchema);
		if (!string.IsNullOrWhiteSpace(schema))
		{
			return schema;
		}

		if (_schemaStates.TryGetRememberedSchema(profile.Id, out string rememberedSchema))
		{
			schema = NormalizeSchemaSelection(rememberedSchema);
			if (!string.IsNullOrWhiteSpace(schema))
			{
				document.DefaultSchema = schema;
				return schema;
			}
		}

		if (string.Equals(_focusedExplorerConnectionProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
		{
			schema = NormalizeSchemaSelection(_focusedExplorerSchemaName);
			if (!string.IsNullOrWhiteSpace(schema))
			{
				document.DefaultSchema = schema;
				return schema;
			}
		}

		schema = NormalizeSchemaSelection(profile.Schema);
		if (!string.IsNullOrWhiteSpace(schema))
		{
			document.DefaultSchema = schema;
		}

		return schema;
	}
	private bool SynchronizeWorkbenchSchemaOpenState(ConnectionProfile? profile, string? schemaName)
	{
		if (profile == null || !IsConnectionProfileConnected(profile))
		{
			return false;
		}

		string normalizedSchema = NormalizeSchemaSelection(schemaName);
		if (string.IsNullOrWhiteSpace(normalizedSchema))
		{
			return false;
		}

		bool changed = _schemaStates.MarkSchemaOpen(profile.Id, normalizedSchema);
		ObjectNode? schemaNode = ExplorerNodeUtilities.FindSchemaNodeByConnectionAndName(ExplorerNodes, profile.Id, normalizedSchema);
		if (schemaNode != null && !schemaNode.IsSchemaOpened)
		{
			schemaNode.IsSchemaOpened = true;
			changed = true;
		}

		_schemaStates.RememberSchema(profile.Id, normalizedSchema);
		if (changed)
		{
			NotifyExplorerFocusChanged();
			NotifyWorkbenchContextChanged();
		}

		return true;
	}
	public async Task LoadSelectedModelDiagramAsync(CancellationToken cancellationToken = default)
	{
		if (!SelectedDocumentIsModelDiagram || SelectedDocument == null)
		{
			return;
		}

		ConnectionProfile? profile = SelectedDocumentConnectionProfile;
		if (profile == null)
		{
			throw new InvalidOperationException("当前数据模型工作台未绑定数据库连接。");
		}

		string schemaName = string.IsNullOrWhiteSpace(SelectedDocument.ModelSchemaName)
			? NormalizeSchemaSelection(SelectedDocument.DefaultSchema)
			: SelectedDocument.ModelSchemaName;
		if (!IsConnectionProfileConnected(profile))
		{
			throw new InvalidOperationException("当前数据模型工作台绑定的数据库未连接。");
		}
		if (!IsSchemaOpen(profile, schemaName))
		{
			throw new InvalidOperationException("当前数据模型工作台未绑定有效模式。");
		}
		ModelDiagramWorkspace workspace = string.IsNullOrWhiteSpace(SelectedDocument.ModelFocusTableName)
			? await _modelDiagramService.LoadSchemaModelAsync(CloneConnectionProfile(profile), schemaName, cancellationToken)
			: await _modelDiagramService.LoadTableNeighborhoodAsync(CloneConnectionProfile(profile), schemaName, SelectedDocument.ModelFocusTableName, 1, cancellationToken);
		ModelDiagramWorkspaceState state = EnsureModelDiagramState(SelectedDocument);
		state.Load(workspace, SelectedDocument.ModelFocusTableName);
		state.SetMessageText(state.IsLargeMode
			? $"已加载 {workspace.SchemaName} 模式，共 {workspace.Tables.Count} 张表、{workspace.Relations.Count} 条关系。当前模式较大，已默认聚焦局部关系，建议优先使用搜索或“仅当前表上下游”。"
			: $"已加载 {workspace.SchemaName} 模式，共 {workspace.Tables.Count} 张表、{workspace.Relations.Count} 条关系。");
		SelectedDocument.DefaultSchema = workspace.SchemaName;
		SelectedDocument.ModelSchemaName = workspace.SchemaName;
		SelectedDocument.Title = WorkspaceDocumentNaming.BuildModelDiagramTitle(profile.Name, workspace.SchemaName, SelectedDocument.ModelFocusTableName);
		UpdateRecentFileDocumentBinding(SelectedDocument);
		NotifySelectedModelDiagramChanged();
		AddLog(string.IsNullOrWhiteSpace(SelectedDocument.ModelFocusTableName)
			? $"Loaded model diagram workspace for {profile.Name} / {workspace.SchemaName}."
			: $"Loaded model diagram workspace for {workspace.SchemaName}.{SelectedDocument.ModelFocusTableName}.");
	}
	public async Task ExportSelectedModelDiagramRelationsAsync(string filePath, CancellationToken cancellationToken = default)
	{
		if (!SelectedDocumentIsModelDiagram || SelectedDocument == null)
		{
			return;
		}

		ConnectionProfile? profile = SelectedDocumentConnectionProfile;
		if (profile == null)
		{
			throw new InvalidOperationException("当前数据模型工作台未绑定数据库连接。");
		}

		ModelDiagramWorkspaceState state = EnsureModelDiagramState(SelectedDocument);
		IReadOnlyList<ModelRelationExportRow> rows = await _modelDiagramService.ExportRelationRowsAsync(CloneConnectionProfile(profile), state.ToWorkspace(), cancellationToken);
		await File.WriteAllTextAsync(filePath, ModelDiagramRelationCsvBuilder.Build(rows), new UTF8Encoding(true), cancellationToken);
		state.SetMessageText($"已导出 {rows.Count} 条关系到 {Path.GetFileName(filePath)}。");
	}
	public void SelectModelDiagramRelation(ModelDiagramRelationState? relation)
	{
		if (relation == null)
		{
			return;
		}

		ModelDiagramWorkspaceState? state = GetSelectedModelDiagramState();
		if (state == null)
		{
			return;
		}

		state.FocusRelation(relation.ConstraintName, relation.FromTable, relation.ToTable);
	}
	public void ClearSelectedModelDiagramRelation()
	{
		ModelDiagramWorkspaceState? state = GetSelectedModelDiagramState();
		if (state?.SelectedRelation == null)
		{
			return;
		}

		state.SelectedRelation = null;
	}
	public void OpenQueryForModelDiagramRelation(ModelDiagramRelationState? relation)
	{
		if (!SelectedDocumentIsModelDiagram || SelectedDocument == null || relation == null)
		{
			return;
		}

		string schemaName = string.IsNullOrWhiteSpace(SelectedDocument.ModelSchemaName)
			? NormalizeSchemaSelection(SelectedDocument.DefaultSchema)
			: SelectedDocument.ModelSchemaName;
		string sql = SqlTemplateBuilder.BuildModelDiagramRelationQuery(schemaName, relation);
		EditorDocument queryDocument = CreateQueryDocument(
			$"Join {relation.ToTable}",
			sql,
			SelectedDocument.ConnectionProfileId,
			schemaName);
		ExecutionStatus = UiText.ExecutionReady;
		AddLog($"Generated relation query for {relation.FromTable} -> {relation.ToTable} into {queryDocument.Title}.");
	}
	public void MoveSelectedModelDiagramNode(string tableName, double deltaX, double deltaY)
	{
		ModelDiagramWorkspaceState? state = GetSelectedModelDiagramState();
		if (state == null || string.IsNullOrWhiteSpace(tableName))
		{
			return;
		}

		state.MoveNode(tableName, deltaX, deltaY);
	}
	public void ReloadSelectedModelDiagramLayout()
	{
		ModelDiagramWorkspaceState? state = GetSelectedModelDiagramState();
		if (state == null)
		{
			return;
		}

		state.ReloadLayout();
		state.SetMessageText("已重新布局当前数据模型。");
	}
	public void ExpandSelectedModelDiagramNeighborhood()
	{
		ModelDiagramWorkspaceState? state = GetSelectedModelDiagramState();
		if (state?.SelectedTable == null)
		{
			return;
		}

		state.ExpandNeighborhood();
		state.SetMessageText($"已展开到第 {state.NeighborhoodDepth} 层关联。");
	}
	public void ResetSelectedModelDiagramZoom()
	{
		ModelDiagramWorkspaceState? state = GetSelectedModelDiagramState();
		if (state == null)
		{
			return;
		}

		state.ResetZoom();
	}
	public void ZoomInSelectedModelDiagram()
	{
		ModelDiagramWorkspaceState? state = GetSelectedModelDiagramState();
		if (state == null)
		{
			return;
		}

		state.ZoomIn();
	}
	public void ZoomOutSelectedModelDiagram()
	{
		ModelDiagramWorkspaceState? state = GetSelectedModelDiagramState();
		if (state == null)
		{
			return;
		}

		state.ZoomOut();
	}
	public async Task<CommentImportResult> ImportSelectedCommentWorkspaceCsvAsync(string filePath, CancellationToken cancellationToken = default)
	{
		CommentMaintenanceWorkspaceState state = GetSelectedCommentWorkspaceState()
			?? throw new InvalidOperationException("当前没有可用的注释维护工作台。");
		CommentMaintenanceWorkspace workspace = state.ToWorkspace();
		CommentImportResult result = await _commentMaintenanceService.ImportCsvAsync(workspace, filePath, cancellationToken);
		state.Load(workspace);
		state.SetImportSummary(result);
		NotifySelectedCommentWorkspaceChanged();
		return result;
	}
	public async Task LoadSelectedObjectEditorAsync(CancellationToken cancellationToken = default)
	{
		if (!SelectedDocumentIsObjectEditor || SelectedDocument == null)
		{
			return;
		}

		ConnectionProfile? profile = SelectedDocumentConnectionProfile ?? ActiveConnectionProfile;
		if (profile == null)
		{
			throw new InvalidOperationException("当前对象编辑页未绑定数据库连接。");
		}

		ObjectEditorState state = EnsureObjectEditorState(SelectedDocument);
		state.IsBusy = true;
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		try
		{
			ObjectEditorModel model = await _objectEditorService.LoadObjectEditorModelAsync(
				CloneConnectionProfile(profile),
				SelectedDocument.ObjectSchemaName,
				SelectedDocument.ObjectRawName,
				SelectedDocument.ObjectType,
				cancellationToken);
			state.LoadFromModel(profile.Name, model);
			SelectedDocument.Content = model.OriginalDefinition ?? string.Empty;
			SelectedDocument.IsDirty = false;
			SelectedDocument.CaretOffset = 0;
			AddLog($"Loaded object definition for {model.SchemaName}.{model.ObjectName}.");
		}
		finally
		{
			state.IsBusy = false;
			NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		}
	}
	public bool IsConnectionProfileConnected(ConnectionProfile? profile)
	{
		return profile != null && _connectedConnectionIds.Contains(profile.Id);
	}
	public bool IsSchemaOpen(ConnectionProfile? profile, string? schemaName)
	{
		if (profile == null)
		{
			return false;
		}

		string normalizedSchema = NormalizeSchemaSelection(schemaName);
		if (string.IsNullOrWhiteSpace(normalizedSchema))
		{
			return false;
		}

		if (_schemaStates.ContainsOpenedSchema(profile.Id, normalizedSchema))
		{
			return true;
		}

		if (HasOpenedSchemaNode(profile.Id, normalizedSchema))
		{
			_schemaStates.MarkSchemaOpen(profile.Id, normalizedSchema);
			return true;
		}

		return false;
	}
	public async Task OpenSchemaAsync(ObjectNode? schemaNode, CancellationToken cancellationToken = default)
	{
		if (schemaNode == null || !schemaNode.IsSchemaNode)
		{
			return;
		}

		ConnectionProfile profile = ResolveMetadataProfile(schemaNode);
		if (!IsConnectionProfileConnected(profile))
		{
			throw new InvalidOperationException("当前数据库未连接。请先打开数据库。");
		}

		string schemaName = NormalizeSchemaSelection(schemaNode.Name);
		if (string.IsNullOrWhiteSpace(schemaName))
		{
			throw new InvalidOperationException("当前模式无效，无法打开。");
		}

		_schemaStates.MarkSchemaOpen(profile.Id, schemaName);
		_schemaStates.RememberSchema(profile.Id, schemaName);
		schemaNode.IsSchemaOpened = true;
		schemaNode.IsExpanded = true;
		schemaNode.IsLoaded = false;
		schemaNode.HasUnloadedChildren = true;
		BuildDisconnectedExplorerTree();
		ObjectNode? refreshedSchemaNode = ExplorerNodeUtilities.FindNodeByKey(ExplorerNodes, schemaNode.Key) ?? schemaNode;
		SetExplorerFocus(refreshedSchemaNode);
		NotifyExplorerFocusChanged();
		await EnsureNodeChildrenLoadedAsync(refreshedSchemaNode, cancellationToken);
		UpdateSelectedDocumentConnectionLabel();
		PrioritizeSelectedDocumentSchemas(profile);
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		NotifyWorkbenchContextChanged();
		_completionSnapshots.QueueStandardWarmups(CloneConnectionProfile(profile), schemaName);
		AddLog($"Opened schema {schemaName} for {profile.Name}.");
	}
	public void CloseSchema(ObjectNode? schemaNode)
	{
		if (schemaNode == null || !schemaNode.IsSchemaNode)
		{
			return;
		}

		ConnectionProfile profile = ResolveMetadataProfile(schemaNode);
		string schemaName = NormalizeSchemaSelection(schemaNode.Name);
		if (string.IsNullOrWhiteSpace(schemaName))
		{
			return;
		}

		_schemaStates.MarkSchemaClosed(profile.Id, schemaName);
		schemaNode.Children.Clear();
		schemaNode.IsExpanded = false;
		schemaNode.IsLoaded = false;
		schemaNode.HasUnloadedChildren = true;
		schemaNode.IsSchemaOpened = false;
		BuildDisconnectedExplorerTree();
		SetExplorerFocus(ExplorerNodeUtilities.FindNodeByKey(ExplorerNodes, schemaNode.Key) ?? schemaNode);
		NotifyExplorerFocusChanged();
		UpdateSelectedDocumentConnectionLabel();
		PrioritizeSelectedDocumentSchemas(profile);
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		NotifyWorkbenchContextChanged();
		AddLog($"Closed schema {schemaName} for {profile.Name}.");
	}
	public void SetExplorerFocus(ObjectNode? node)
	{
		string nextNodeKey = node?.Key ?? string.Empty;
		string nextConnectionProfileId = string.Empty;
		string nextSchemaName = string.Empty;
		string nextObjectName = node?.Name ?? string.Empty;

		if (node != null)
		{
			nextConnectionProfileId = ResolveConnectionProfileForNode(node)?.Id
				?? node.ConnectionProfileId
				?? string.Empty;
			nextSchemaName = ResolveSchemaNameForNode(node);
			if (!string.IsNullOrWhiteSpace(nextConnectionProfileId) && !string.IsNullOrWhiteSpace(nextSchemaName))
			{
				// 以后从连接节点新建查询时，优先回到最近看过的 schema。
				_schemaStates.RememberSchema(nextConnectionProfileId, nextSchemaName);
			}
			else if (node.IsConnectionNode &&
			         !string.IsNullOrWhiteSpace(nextConnectionProfileId) &&
			         _schemaStates.TryGetRememberedSchema(nextConnectionProfileId, out string rememberedSchema))
			{
				nextSchemaName = rememberedSchema;
			}
		}

		if (string.Equals(_focusedExplorerNodeKey, nextNodeKey, StringComparison.OrdinalIgnoreCase) &&
		    string.Equals(_focusedExplorerConnectionProfileId, nextConnectionProfileId, StringComparison.OrdinalIgnoreCase) &&
		    string.Equals(_focusedExplorerSchemaName, nextSchemaName, StringComparison.OrdinalIgnoreCase) &&
		    string.Equals(_focusedExplorerObjectName, nextObjectName, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		_focusedExplorerNodeKey = nextNodeKey;
		_focusedExplorerConnectionProfileId = nextConnectionProfileId;
		_focusedExplorerSchemaName = nextSchemaName;
		_focusedExplorerObjectName = nextObjectName;
		NotifyExplorerFocusChanged();
	}
	private void NotifyExplorerFocusChanged()
	{
		OnPropertyChanged("FocusedExplorerConnectionProfile");
		OnPropertyChanged("FocusedExplorerSchemaName");
		OnPropertyChanged("FocusedExplorerHasConnection");
		OnPropertyChanged("FocusedExplorerIsConnectionLinked");
		OnPropertyChanged("FocusedExplorerIsSchemaOpened");
		OnPropertyChanged("CanOpenFocusedCommentMaintenance");
		OnPropertyChanged("CanOpenFocusedModelDiagram");
		OnPropertyChanged("FocusedExplorerContextText");
	}

	private void NotifyWorkbenchContextChanged()
	{
		OnPropertyChanged("CanOpenFocusedCommentMaintenance");
		OnPropertyChanged("CanOpenFocusedModelDiagram");
	}
	private (string ConnectionProfileId, string SchemaName) ResolvePreferredNewQueryContext()
	{
		if (!string.IsNullOrWhiteSpace(_focusedExplorerConnectionProfileId))
		{
			ConnectionProfile? focusedProfile = ConnectionProfiles.FirstOrDefault(item =>
				string.Equals(item.Id, _focusedExplorerConnectionProfileId, StringComparison.OrdinalIgnoreCase));
			string focusedSchema = NormalizeSchemaSelection(_focusedExplorerSchemaName);
			if (string.IsNullOrWhiteSpace(focusedSchema))
			{
				focusedSchema = NormalizeSchemaSelection(focusedProfile?.Schema);
			}
			return (_focusedExplorerConnectionProfileId, focusedSchema);
		}

		if (SelectedDocument != null)
		{
			return (SelectedDocument.ConnectionProfileId, NormalizeSchemaSelection(SelectedDocument.DefaultSchema));
		}

		string fallbackConnectionId = ActiveConnectionProfile?.Id ?? string.Empty;
		string fallbackSchema = string.Empty;
		if (!string.IsNullOrWhiteSpace(fallbackConnectionId) &&
		    _schemaStates.TryGetRememberedSchema(fallbackConnectionId, out string rememberedSchema))
		{
			fallbackSchema = rememberedSchema;
		}
		else
		{
			fallbackSchema = ActiveConnectionProfile?.Schema ?? string.Empty;
		}

		return (fallbackConnectionId, NormalizeSchemaSelection(fallbackSchema));
	}
	private DatabaseContextState ResolveSelectedDocumentContext()
	{
		if (SelectedDocument == null || !SelectedDocumentCanProvideSchemaContext(SelectedDocument))
		{
			return new DatabaseContextState(null, string.Empty, false, false, false, "document");
		}

		ConnectionProfile? profile = SelectedDocumentConnectionProfile;
		string schemaName = ResolveSelectedDocumentSchemaForContext();
		bool isConnected = IsConnectionProfileConnected(profile);
		bool hasSchema = !string.IsNullOrWhiteSpace(schemaName);
		bool isSchemaOpened = hasSchema && IsSchemaOpen(profile, schemaName);
		return new DatabaseContextState(profile, schemaName, isConnected, hasSchema, isSchemaOpened, "document");
	}
	private DatabaseContextState ResolveExplorerFocusContext()
	{
		ConnectionProfile? profile = FocusedExplorerConnectionProfile;
		string schemaName = NormalizeSchemaSelection(_focusedExplorerSchemaName);
		bool isConnected = IsConnectionProfileConnected(profile);
		bool hasSchema = !string.IsNullOrWhiteSpace(schemaName);
		bool isSchemaOpened = hasSchema && IsSchemaOpen(profile, schemaName);
		return new DatabaseContextState(profile, schemaName, isConnected, hasSchema, isSchemaOpened, "explorer");
	}
	private DatabaseContextState ResolveWorkbenchContext()
	{
		DatabaseContextState selectedDocumentContext = ResolveSelectedDocumentContext();
		if (selectedDocumentContext.Profile != null)
		{
			if (CanOpenSchemaWorkbench(selectedDocumentContext))
			{
				return selectedDocumentContext;
			}

			DatabaseContextState explorerFocusContext = ResolveExplorerFocusContext();
			if (!selectedDocumentContext.HasSchema && CanOpenSchemaWorkbench(explorerFocusContext))
			{
				return explorerFocusContext;
			}

			return selectedDocumentContext;
		}

		return ResolveExplorerFocusContext();
	}

	private static bool CanOpenSchemaWorkbench(DatabaseContextState context)
	{
		return context.Profile != null &&
		       context.IsConnectionConnected &&
		       context.HasSchema &&
		       context.IsSchemaOpened;
	}

	private bool TryResolveWorkbenchSchemaContext(out ConnectionProfile profile, out string schemaName)
	{
		DatabaseContextState context = ResolveWorkbenchContext();
		schemaName = context.SchemaName;
		if (!CanOpenSchemaWorkbench(context))
		{
			profile = null!;
			return false;
		}

		profile = context.Profile!;
		return true;
	}
	private static bool SelectedDocumentCanProvideSchemaContext(EditorDocument document)
	{
		return string.Equals(document.DocumentKind, "Query", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(document.DocumentKind, "CommentMaintenance", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(document.DocumentKind, "ModelDiagram", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(document.DocumentKind, "ObjectEditor", StringComparison.OrdinalIgnoreCase);
	}
	private string ResolveSelectedDocumentSchemaForContext()
	{
		if (SelectedDocument == null)
		{
			return string.Empty;
		}

		if (SelectedDocumentIsQuery)
		{
			string selectedSchema = NormalizeSchemaSelection(GetSelectedDocumentState().SelectedSchema);
			if (!string.IsNullOrWhiteSpace(selectedSchema))
			{
				return selectedSchema;
			}
		}

		string documentSchema = NormalizeSchemaSelection(SelectedDocument.DefaultSchema);
		if (!string.IsNullOrWhiteSpace(documentSchema))
		{
			return documentSchema;
		}

		return NormalizeSchemaSelection(SelectedDocumentConnectionProfile?.Schema);
	}
	private string ResolveSchemaNameForNode(ObjectNode? node)
	{
		ObjectNode? currentNode = node;
		while (currentNode != null)
		{
			if (currentNode.IsSchemaNode)
			{
				return NormalizeSchemaSelection(currentNode.Name);
			}

			if (!string.IsNullOrWhiteSpace(currentNode.SchemaName))
			{
				return NormalizeSchemaSelection(currentNode.SchemaName);
			}

			if (string.IsNullOrWhiteSpace(currentNode.ParentKey))
			{
				break;
			}

			currentNode = ExplorerNodeUtilities.FindNodeByKey(ExplorerNodes, currentNode.ParentKey);
		}

		return string.Empty;
	}
	private bool IsSelectedDocumentSchemaOpened()
	{
		return IsSchemaOpen(SelectedDocumentConnectionProfile, NormalizeSchemaSelection(GetSelectedDocumentState().SelectedSchema));
	}
	private string ResolveSelectedDocumentExecutionBlockReason()
	{
		ConnectionProfile? profile = SelectedDocumentConnectionProfile;
		if (profile == null || !IsConnectionProfileConnected(profile))
		{
			return UiText.NotConnectedDatabase;
		}

		return UiText.NotConnectedDatabase;
	}
	public void NotifySelectedDocumentContentEdited()
	{
		if (!SelectedDocumentIsObjectEditor)
		{
			return;
		}

		NotifySelectedObjectEditorChanged();
	}
	public async Task<string> PreviewSelectedObjectEditorSqlAsync(CancellationToken cancellationToken = default)
	{
		if (!SelectedDocumentIsObjectEditor || SelectedDocument == null)
		{
			return string.Empty;
		}

		ConnectionProfile? profile = SelectedDocumentConnectionProfile ?? ActiveConnectionProfile;
		if (profile == null)
		{
			throw new InvalidOperationException("当前对象编辑页未绑定数据库连接。");
		}

		ObjectEditorState state = EnsureObjectEditorState(SelectedDocument);
		state.IsBusy = true;
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		try
		{
			string sql = await _objectEditorService.BuildPreviewSqlAsync(
				CloneConnectionProfile(profile),
				BuildSelectedObjectEditorSaveRequest(),
				cancellationToken);
			state.SetPreviewSql(sql);
			return sql;
		}
		finally
		{
			state.IsBusy = false;
			NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		}
	}
	public async Task<IReadOnlyList<ObjectCompileMessage>> ValidateSelectedObjectEditorAsync(CancellationToken cancellationToken = default)
	{
		if (!SelectedDocumentIsObjectEditor || SelectedDocument == null)
		{
			return Array.Empty<ObjectCompileMessage>();
		}

		ConnectionProfile? profile = SelectedDocumentConnectionProfile ?? ActiveConnectionProfile;
		if (profile == null)
		{
			throw new InvalidOperationException("当前对象编辑页未绑定数据库连接。");
		}

		ObjectEditorState state = EnsureObjectEditorState(SelectedDocument);
		state.IsBusy = true;
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		try
		{
			IReadOnlyList<ObjectCompileMessage> messages = await _objectEditorService.ValidateObjectAsync(
				CloneConnectionProfile(profile),
				BuildSelectedObjectEditorSaveRequest(),
				cancellationToken);
			state.SetExecutionResult(messages.Count == 0 ? "未返回校验消息。" : "对象校验完成。", messages);
			return messages;
		}
		finally
		{
			state.IsBusy = false;
			NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		}
	}
	public async Task<ObjectEditorSaveResult> SaveSelectedObjectEditorAsync(CancellationToken cancellationToken = default)
	{
		if (!SelectedDocumentIsObjectEditor || SelectedDocument == null)
		{
			throw new InvalidOperationException("当前没有可保存的对象编辑页。");
		}

		ConnectionProfile? profile = SelectedDocumentConnectionProfile ?? ActiveConnectionProfile;
		if (profile == null)
		{
			throw new InvalidOperationException("当前对象编辑页未绑定数据库连接。");
		}

		ObjectEditorState state = EnsureObjectEditorState(SelectedDocument);
		state.IsBusy = true;
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		try
		{
			ObjectEditorSaveResult result = await _objectEditorService.SaveObjectAsync(
				CloneConnectionProfile(profile),
				BuildSelectedObjectEditorSaveRequest(),
				cancellationToken);
			state.MarkSaved(SelectedDocument.Content ?? string.Empty);
			state.SetPreviewSql(result.ExecutedSql);
			state.SetExecutionResult(result.Message, result.CompileMessages);
			SelectedDocument.IsDirty = false;
			AddLog($"Saved object definition for {SelectedDocument.ObjectSchemaName}.{SelectedDocument.ObjectRawName}.");
			return result;
		}
		finally
		{
			state.IsBusy = false;
			NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
		}
	}
	public void DiscardSelectedObjectEditorChanges()
	{
		if (!SelectedDocumentIsObjectEditor || SelectedDocument == null)
		{
			return;
		}

		ObjectEditorState state = EnsureObjectEditorState(SelectedDocument);
		SelectedDocument.Content = state.OriginalDefinition ?? string.Empty;
		SelectedDocument.IsDirty = false;
		SelectedDocument.CaretOffset = 0;
		state.SetExecutionResult("已放弃未保存的对象修改。", Array.Empty<ObjectCompileMessage>());
		NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
	}
	public Task ExportSelectedCommentWorkspaceCsvAsync(string filePath, CancellationToken cancellationToken = default)
	{
		CommentMaintenanceWorkspaceState state = GetSelectedCommentWorkspaceState()
			?? throw new InvalidOperationException("当前没有可用的注释维护工作台。");
		return ExportSelectedCommentWorkspaceCsvCoreAsync(state, filePath, cancellationToken);
	}
	public string BuildSelectedCommentWorkspaceSqlPreview()
	{
		CommentMaintenanceWorkspaceState state = GetSelectedCommentWorkspaceState()
			?? throw new InvalidOperationException("当前没有可用的注释维护工作台。");
		IReadOnlyList<CommentSqlPreviewItem> items = _commentMaintenanceService.BuildSqlPreview(state.ToWorkspace());
		state.SetPreviewSummary(items.Count);
		NotifySelectedCommentWorkspaceChanged();
		return string.Join(Environment.NewLine + Environment.NewLine, items.Select(item => item.SqlText));
	}
	public async Task<int> ApplySelectedCommentWorkspaceAsync(CancellationToken cancellationToken = default)
	{
		if (SelectedDocument == null)
		{
			return 0;
		}

		CommentMaintenanceWorkspaceState state = GetSelectedCommentWorkspaceState()
			?? throw new InvalidOperationException("当前没有可用的注释维护工作台。");
		ConnectionProfile? profile = SelectedDocumentConnectionProfile ?? ActiveConnectionProfile;
		if (profile == null)
		{
			throw new InvalidOperationException("当前工作台未绑定数据库连接。");
		}

		IReadOnlyList<CommentSqlPreviewItem> items = _commentMaintenanceService.BuildSqlPreview(state.ToWorkspace());
		if (items.Count == 0)
		{
			state.SetApplySummary(0);
			NotifySelectedCommentWorkspaceChanged();
			return 0;
		}

		int affected = await _commentMaintenanceService.ApplyChangesAsync(profile, items, cancellationToken);
		_localizationResolver.Invalidate(profile);
		if (ActiveConnectionProfile != null &&
			string.Equals(ActiveConnectionProfile.Id, profile.Id, StringComparison.OrdinalIgnoreCase))
		{
			await SetActiveConnectionAsync(CloneConnectionProfile(profile), cancellationToken);
		}

		await RefreshOpenResultColumnCommentsForSchemaAsync(profile, NormalizeSchemaSelection(SelectedDocument.DefaultSchema), cancellationToken);
		await LoadSelectedCommentWorkspaceAsync(cancellationToken);
		EnsureCommentWorkspaceState(SelectedDocument).SetApplySummary(affected);
		NotifySelectedCommentWorkspaceChanged();
		return affected;
	}
	public void ClearSelectedCommentWorkspaceFilters()
	{
		GetSelectedCommentWorkspaceState()?.ClearFilters();
		NotifySelectedCommentWorkspaceChanged();
	}

	public void MergeImportedProfiles(IEnumerable<ConnectionProfile> profiles)
	{
		ConnectionProfile[] importedProfiles = profiles.Select(CloneConnectionProfile).ToArray();
		foreach (ConnectionProfile profile in importedProfiles)
		{
			NormalizeOracleSettings(profile);
			ConnectionProfile? connectionProfile = ConnectionProfiles.FirstOrDefault((ConnectionProfile item) => string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(item.ProviderName, profile.ProviderName, StringComparison.OrdinalIgnoreCase) && string.Equals(item.Server, profile.Server, StringComparison.OrdinalIgnoreCase));
			if (connectionProfile != null)
			{
				ConnectionProfiles.Remove(connectionProfile);
			}
			ConnectionProfiles.Add(profile);
		}
		SelectedConnectionProfile = ConnectionProfiles.FirstOrDefault();
		BuildDisconnectedExplorerTree();
		UpdateConnectionStatus();
		AddLog($"Imported {importedProfiles.Length} connection profile(s).");
	}
	public async Task<ConnectionImportResult> ImportSelectedConnectionsAsync(IEnumerable<ConnectionImportPreviewItem> previewItems, CancellationToken cancellationToken = default(CancellationToken))
	{
		ConnectionImportResult result = MergeSelectedImportedProfiles(previewItems);
		if (result.ImportedCount > 0)
		{
			await SaveConnectionsAsync(cancellationToken);
		}
		return result;
	}
	private ConnectionImportResult MergeSelectedImportedProfiles(IEnumerable<ConnectionImportPreviewItem> previewItems)
	{
		ConnectionImportMergeOutcome outcome = ConnectionImportMerger.MergePreviewItems(ConnectionProfiles, previewItems, ActiveConnectionProfile);
		if (outcome.ReplacementActiveConnectionProfile != null)
		{
			ActiveConnectionProfile = outcome.ReplacementActiveConnectionProfile;
		}

		if (outcome.FirstImportedProfile != null)
		{
			SelectedConnectionProfile = outcome.FirstImportedProfile;
		}
		else if (SelectedConnectionProfile == null)
		{
			SelectedConnectionProfile = ConnectionProfiles.FirstOrDefault();
		}

		BuildDisconnectedExplorerTree();
		UpdateConnectionStatus();
		RefreshOracleUiState();
		NotifyConnectionListsChanged();
		ConnectionImportResult result = outcome.Result;
		AddLog($"Imported connection profiles. added={result.AddedCount}; renamed={result.RenamedCount}; replaced={result.ReplacedCount}; skipped={result.SkippedCount}.");
		return outcome.Result;
	}

	public IReadOnlyList<ConnectionProfile> SnapshotConnections()
	{
		return ConnectionProfiles.Select(CloneConnectionProfile).ToArray();
	}

	public Task<IReadOnlyList<ConnectionProfile>> ImportConnectionsAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken))
	{
		return _connectionProfileStore.ImportAsync(filePath, cancellationToken);
	}

	public Task ExportConnectionsAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken))
	{
		return _connectionProfileStore.ExportAsync(filePath, SnapshotConnections(), cancellationToken);
	}

	public Task ExportConnectionsAsync(string filePath, IReadOnlyList<ConnectionProfile> selectedProfiles, CancellationToken cancellationToken = default(CancellationToken))
	{
		return _connectionProfileStore.ExportAsync(filePath, (selectedProfiles ?? Array.Empty<ConnectionProfile>()).Select(CloneConnectionProfile).ToArray(), cancellationToken);
	}

	public async Task<QueryExecutionResult> ExecuteSqlAsync(string sql, bool includePlan, CancellationToken cancellationToken = default(CancellationToken))
	{
		ExecutionStatus = (includePlan ? UiText.ExecutionPlanRunning : UiText.ExecutionRunning);
		QueryExecutionResult result = await _sqlExecutionService.ExecuteAsync(new QueryExecutionRequest
		{
			Connection = ((ActiveConnectionProfile == null) ? null : CloneConnectionProfile(ActiveConnectionProfile)),
			Sql = sql,
			IncludeExecutionPlan = includePlan,
			MaxPreviewRows = DefaultResultPreviewRowLimit
		}, cancellationToken);
		ApplyExecutionResult(result, includePlan);
		ExecutionStatus = UiText.ExecutionCompleted;
		AddLog(result.Summary);
		return result;
	}

	public string FormatText(string text)
	{
		return _sqlFormatterService.Format(text);
	}

	public Task<IReadOnlyList<CompletionItem>> BuildCompletionItemsAsync(
		string prefix,
		ConnectionProfile? documentConnection,
		string selectedSchema,
		string completionContext,
		string? resolvedObjectName = null,
		string? singleRelationName = null,
		bool allowEmptyPrefix = false,
		IReadOnlyList<CompletionController.CompletionRelationReference>? relationReferences = null,
		string? qualifier = null,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		string normalizedPrefix = prefix?.Trim() ?? string.Empty;
		if (!allowEmptyPrefix && string.IsNullOrWhiteSpace(normalizedPrefix))
		{
			return Task.FromResult<IReadOnlyList<CompletionItem>>(Array.Empty<CompletionItem>());
		}
		bool hasSelectedSchema = !string.IsNullOrWhiteSpace(selectedSchema) && !string.Equals(selectedSchema, "(Default)", StringComparison.OrdinalIgnoreCase);
		string preferredSchema = hasSelectedSchema ? selectedSchema : string.Empty;
		IReadOnlyList<CompletionController.CompletionRelationReference> normalizedRelations = CompletionMetadataRules.NormalizeRelationReferences(relationReferences);
		string? preferredObject = CompletionMetadataRules.ResolvePreferredObject(completionContext, resolvedObjectName, singleRelationName);
		return BuildCompletionItemsCoreAsync(
			normalizedPrefix,
			documentConnection,
			preferredSchema,
			completionContext,
			resolvedObjectName,
			singleRelationName,
			preferredObject,
			allowEmptyPrefix,
			normalizedRelations,
			qualifier,
			cancellationToken);
	}

	public async Task<IReadOnlyList<CompletionEntry>> SearchRelationCandidatesAsync(string prefix, ConnectionProfile? documentConnection, string selectedSchema, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (documentConnection == null || string.IsNullOrWhiteSpace(prefix))
		{
			return Array.Empty<CompletionEntry>();
		}
		string preferredSchema = (string.Equals(selectedSchema, "*", StringComparison.OrdinalIgnoreCase) ? "*" : ((!string.IsNullOrWhiteSpace(selectedSchema) && !string.Equals(selectedSchema, "(Default)", StringComparison.OrdinalIgnoreCase)) ? selectedSchema : string.Empty));
		return await _databaseExplorerService.SearchRelationCompletionEntriesAsync(documentConnection, prefix.Trim(), preferredSchema, cancellationToken);
	}

	public async Task PreloadRelationLocatorSnapshotAsync(ConnectionProfile? profile, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (profile == null)
		{
			return;
		}
		try
		{
			using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeoutSource.CancelAfter(RelationLocatorWarmupTimeout);
			IReadOnlyList<CompletionEntry> entries = await _databaseExplorerService.LoadRelationCompletionSnapshotAsync(profile, "*", timeoutSource.Token);
			AddLog($"Preloaded relation locator metadata for {profile.Name}: {entries.Count} object(s).");
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			AddLog("Relation locator metadata preload timed out for " + profile.Name + ".");
		}
		catch (Exception ex2)
		{
			AddLog("Relation locator metadata preload failed for " + profile.Name + ": " + ex2.Message);
		}
	}

	public IReadOnlyList<CompletionItem> BuildImmediateCompletionItems(
		string prefix,
		ConnectionProfile? documentConnection,
		string selectedSchema,
		string completionContext,
		string? resolvedObjectName = null,
		string? singleRelationName = null,
		bool allowEmptyPrefix = false,
		IReadOnlyList<CompletionController.CompletionRelationReference>? relationReferences = null,
		string? qualifier = null)
	{
		string normalizedPrefix = prefix?.Trim() ?? string.Empty;
		if (!allowEmptyPrefix && string.IsNullOrWhiteSpace(normalizedPrefix))
		{
			return Array.Empty<CompletionItem>();
		}
		bool hasSelectedSchema = !string.IsNullOrWhiteSpace(selectedSchema) && !string.Equals(selectedSchema, "(Default)", StringComparison.OrdinalIgnoreCase);
		string preferredSchema = hasSelectedSchema ? selectedSchema : string.Empty;
		IReadOnlyList<CompletionController.CompletionRelationReference> normalizedRelations = CompletionMetadataRules.NormalizeRelationReferences(relationReferences);
		string? preferredObject = CompletionMetadataRules.ResolvePreferredObject(completionContext, resolvedObjectName, singleRelationName);
		List<CompletionItem> completionItems = SqlCompletionKeywordProvider.BuildItems(normalizedPrefix, completionContext, documentConnection?.ProviderName).ToList();
		if (documentConnection != null)
		{
			IReadOnlyList<CompletionEntry> cachedCompletionSnapshot = _completionSnapshots.GetCachedEntries(
				documentConnection,
				preferredSchema,
				completionContext,
				preferredObject,
				normalizedRelations);
			if (cachedCompletionSnapshot.Count > 0)
			{
				foreach (CompletionEntry item in CompletionMetadataRules.FilterEntries(cachedCompletionSnapshot, normalizedPrefix, preferredSchema, completionContext, resolvedObjectName, singleRelationName, allowEmptyPrefix))
				{
					completionItems.Add(CompletionMetadataRules.CreateItem(item, completionContext));
				}
			}
		}
		int take = Math.Min(CompletionCandidateRules.GetTakeLimit(completionContext), 40);
		int Rank(CompletionItem entry) => CompletionCandidateRules.GetRank(entry, normalizedPrefix, preferredSchema, completionContext, resolvedObjectName, singleRelationName, _recentCompletionUsage);
		return (from @group in completionItems.GroupBy((CompletionItem entry) => entry.InsertText, StringComparer.OrdinalIgnoreCase)
			select @group.OrderBy(Rank)
				.ThenBy((CompletionItem entry) => string.Equals(entry.Kind, "keyword", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
				.First() into entry
			orderby Rank(entry), entry.InsertText.Length
			select entry).ThenBy((CompletionItem entry) => entry.InsertText, StringComparer.OrdinalIgnoreCase).Take(take).ToArray();
	}

	private async Task<IReadOnlyList<CompletionItem>> BuildCompletionItemsCoreAsync(
		string normalizedPrefix,
		ConnectionProfile? documentConnection,
		string preferredSchema,
		string completionContext,
		string? resolvedObjectName,
		string? singleRelationName,
		string? preferredObject,
		bool allowEmptyPrefix,
		IReadOnlyList<CompletionController.CompletionRelationReference> relationReferences,
		string? qualifier,
		CancellationToken cancellationToken)
	{
		List<CompletionItem> items = SqlCompletionKeywordProvider.BuildItems(normalizedPrefix, completionContext, documentConnection?.ProviderName).ToList();
		if (documentConnection != null)
		{
			try
			{
				IReadOnlyList<CompletionEntry> snapshot = _completionSnapshots.GetCachedEntries(
					documentConnection,
					preferredSchema,
					completionContext,
					preferredObject,
					relationReferences);
				if (CompletionMetadataRules.ShouldLoadMetadata(snapshot, normalizedPrefix, completionContext, allowEmptyPrefix, relationReferences) &&
				    IsConnectionProfileConnected(documentConnection))
				{
					snapshot = await _completionSnapshots.LoadEntriesAsync(
						documentConnection,
						preferredSchema,
						completionContext,
						preferredObject,
						relationReferences,
						cancellationToken);
				}
				else if (snapshot.Count == 0 && documentConnection != null && IsConnectionProfileConnected(documentConnection))
				{
					_completionSnapshots.QueueContextWarmup(documentConnection, preferredSchema, completionContext, preferredObject, relationReferences);
				}

				items.AddRange(CompletionMetadataRules.FilterEntries(snapshot, normalizedPrefix, preferredSchema, completionContext, resolvedObjectName, singleRelationName, allowEmptyPrefix).Select(item => CompletionMetadataRules.CreateItem(item, completionContext)));
			}
			catch (Exception ex)
			{
				AddLog("Completion metadata failed: " + ex.Message);
			}
		}

		int take = CompletionCandidateRules.GetTakeLimit(completionContext);
		int Rank(CompletionItem entry) => CompletionCandidateRules.GetRank(entry, normalizedPrefix, preferredSchema, completionContext, resolvedObjectName, singleRelationName, _recentCompletionUsage);
		return items
			.GroupBy((CompletionItem entry) => entry.InsertText, StringComparer.OrdinalIgnoreCase)
			.Select(@group => @group.OrderBy(Rank)
				.ThenBy((CompletionItem entry) => string.Equals(entry.Kind, "keyword", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
				.First())
			.OrderBy(Rank)
			.ThenBy((CompletionItem entry) => entry.InsertText.Length)
			.ThenBy((CompletionItem entry) => entry.InsertText, StringComparer.OrdinalIgnoreCase)
			.Take(take)
			.ToArray();
	}

	public bool ApplyCompletionItems(IReadOnlyList<CompletionItem> items)
	{
		bool shouldOpen = items.Count > 0;
		CompletionItem? preferredSelection = ResolveCompletionSelection(items, SelectedCompletionItem);
		if (CompletionItemsMatch(items))
		{
			CompletionItem? currentSelection = ResolveCompletionSelection(CompletionItems, preferredSelection);
			bool selectionChanged = !ReferenceEquals(SelectedCompletionItem, currentSelection);
			if (selectionChanged)
			{
				SelectedCompletionItem = currentSelection;
			}

			bool visibilityChanged = IsCompletionOpen != shouldOpen;
			if (visibilityChanged)
			{
				IsCompletionOpen = shouldOpen;
			}

			return selectionChanged || visibilityChanged;
		}

		SynchronizeCompletionItems(items);
		CompletionItem? updatedSelection = ResolveCompletionSelection(CompletionItems, preferredSelection);
		bool changed = true;
		if (!ReferenceEquals(SelectedCompletionItem, updatedSelection))
		{
			SelectedCompletionItem = updatedSelection;
		}

		if (IsCompletionOpen != shouldOpen)
		{
			IsCompletionOpen = shouldOpen;
		}

		return changed;
	}

	private bool CompletionItemsMatch(IReadOnlyList<CompletionItem> items)
	{
		if (CompletionItems.Count != items.Count)
		{
			return false;
		}

		for (int i = 0; i < items.Count; i++)
		{
			if (!AreEquivalentCompletionItems(CompletionItems[i], items[i]))
			{
				return false;
			}
		}

		return true;
	}

	private void SynchronizeCompletionItems(IReadOnlyList<CompletionItem> items)
	{
		int commonCount = Math.Min(CompletionItems.Count, items.Count);
		for (int i = 0; i < commonCount; i++)
		{
			if (!AreEquivalentCompletionItems(CompletionItems[i], items[i]))
			{
				CompletionItems[i] = items[i];
			}
		}

		while (CompletionItems.Count > items.Count)
		{
			CompletionItems.RemoveAt(CompletionItems.Count - 1);
		}

		for (int j = commonCount; j < items.Count; j++)
		{
			CompletionItems.Add(items[j]);
		}
	}

	private static CompletionItem? ResolveCompletionSelection(IEnumerable<CompletionItem> items, CompletionItem? preferredSelection)
	{
		if (preferredSelection != null)
		{
			foreach (CompletionItem item in items)
			{
				if (AreEquivalentCompletionItems(item, preferredSelection))
				{
					return item;
				}
			}
		}

		return items.FirstOrDefault();
	}

	private static bool AreEquivalentCompletionItems(CompletionItem? left, CompletionItem? right)
	{
		if (ReferenceEquals(left, right))
		{
			return true;
		}

		if (left == null || right == null)
		{
			return false;
		}

		return string.Equals(left.InsertText, right.InsertText, StringComparison.OrdinalIgnoreCase) &&
			string.Equals(left.DisplayText, right.DisplayText, StringComparison.Ordinal) &&
			string.Equals(left.Kind, right.Kind, StringComparison.OrdinalIgnoreCase) &&
			string.Equals(left.Description, right.Description, StringComparison.Ordinal) &&
			string.Equals(left.SourceObject, right.SourceObject, StringComparison.OrdinalIgnoreCase) &&
			left.SortWeight == right.SortWeight;
	}

	public async Task UpdateCompletionItemsAsync(string prefix, CancellationToken cancellationToken = default(CancellationToken))
	{
		int requestVersion = Interlocked.Increment(ref _completionRequestVersion);
		IReadOnlyList<CompletionItem> items = await BuildCompletionItemsAsync(prefix, SelectedDocumentConnectionProfile, SelectedDocumentSchema, "generic", cancellationToken: cancellationToken);
		if (requestVersion == _completionRequestVersion)
		{
			ApplyCompletionItems(items);
		}
	}

	public void RegisterCompletionUsage(CompletionItem item)
	{
		if (!string.IsNullOrWhiteSpace(item.SourceObject) || !string.IsNullOrWhiteSpace(item.InsertText))
		{
			string key = (string.IsNullOrWhiteSpace(item.SourceObject) ? item.InsertText : item.SourceObject);
			_recentCompletionUsage.TryGetValue(key, out var value);
			_recentCompletionUsage[key] = value + 1;
		}
	}

	public string FormatResultColumnHeader(ResultColumnViewItem column)
	{
		return ResultColumnHeaderFormatter.Format(column, SelectedResultHeaderMode);
	}

	public string FormatResultColumnHeaderBody(ResultColumnViewItem column)
	{
		return ResultColumnHeaderFormatter.FormatBody(column);
	}

	public string FormatResultColumnHeaderTop(ResultColumnViewItem column)
	{
		return ResultColumnHeaderFormatter.FormatTop(column);
	}

	public string FormatResultColumnHeaderBottom(ResultColumnViewItem column)
	{
		return ResultColumnHeaderFormatter.FormatBottom(column);
	}

	public string FormatResultColumnHeaderDisplay(ResultColumnViewItem column)
	{
		return ResultColumnHeaderFormatter.FormatDisplay(column, SelectedResultHeaderMode);
	}

	public string BuildResultColumnHeaderTooltip(ResultColumnViewItem column)
	{
		return ResultColumnHeaderFormatter.BuildTooltip(column);
	}

	public void ShowSearch(bool includeReplace = false)
	{
		IsSearchVisible = true;
		if (includeReplace)
		{
			IsReplaceVisible = true;
		}
	}

	public void HideSearch()
	{
		IsSearchVisible = false;
		IsReplaceVisible = false;
	}

	public void ToggleReplace()
	{
		IsSearchVisible = true;
		IsReplaceVisible = !IsReplaceVisible;
	}

	public void ToggleWordWrap(bool? enabled = null)
	{
		IsWordWrapEnabled = enabled ?? (!IsWordWrapEnabled);
	}

	public void RefreshOracleUiState()
	{
		OnPropertiesChanged(MainWindowViewModelPropertyGroups.OracleUiState);
	}

	private void RestoreProfiles(IReadOnlyList<ConnectionProfile> profiles)
	{
		ConnectionProfiles.Clear();
		foreach (ConnectionProfile profile in profiles)
		{
			NormalizeOracleSettings(profile);
			ConnectionProfileUtilities.ApplyVisuals(profile);
			ConnectionProfiles.Add(CloneConnectionProfile(profile));
		}
		SelectedConnectionProfile = ConnectionProfiles.FirstOrDefault();
		RefreshOracleUiState();
		NotifyConnectionListsChanged();
	}

	private void RestoreRecentFiles(EditorSessionState session)
	{
		RecentFiles.Clear();
		foreach (RecentFileEntry item in (from item in session.RecentFiles
			where !string.IsNullOrWhiteSpace(item.FilePath)
			orderby item.LastOpenedAt descending
			select item).Take(15))
		{
			RecentFiles.Add(item);
		}
	}

	private void RestoreQueryHistory(EditorSessionState session)
	{
		QueryHistoryEntries.Clear();
		foreach (QueryHistoryEntry item in (from item in session.QueryHistory
			where !string.IsNullOrWhiteSpace(item.Sql)
			orderby item.ExecutedAt descending
			select item).Take(100))
		{
			QueryHistoryEntries.Add(item);
		}
		OnPropertyChanged("QueryHistoryEntries");
	}

	private void RestoreCompletionUsage(EditorSessionState session)
	{
		_recentCompletionUsage.Clear();
		foreach (var (completionText, usageCount) in session.RecentCompletionUsage)
		{
			if (!string.IsNullOrWhiteSpace(completionText) && usageCount > 0)
			{
				_recentCompletionUsage[completionText] = usageCount;
			}
		}
	}

	private void RestoreRecentSchemas(EditorSessionState session)
	{
		HashSet<string> validConnectionIds = ConnectionProfiles
			.Select(item => item.Id)
			.Where(item => !string.IsNullOrWhiteSpace(item))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		_schemaStates.RestoreRememberedSchemas(session.RecentSchemasByConnectionId, validConnectionIds, NormalizeSchemaSelection);
	}

	private void RestoreDocuments(EditorSessionState session)
	{
		Documents.Clear();
		List<EditorDocument> persistedDocuments = session.Documents
			.Where(ShouldPersistDocument)
			.Select(EditorDocumentUtilities.Clone)
			.ToList();
		if (persistedDocuments.Count == 0)
		{
			Documents.Add(new EditorDocument
			{
				Title = "Query 1",
				Content = string.Empty
			});
		}
		else
		{
			foreach (EditorDocument document in persistedDocuments)
			{
				Documents.Add(document);
			}
		}
		SelectedDocument = ((session.SelectedIndex >= 0 && session.SelectedIndex < Documents.Count) ? Documents[session.SelectedIndex] : Documents.FirstOrDefault());
	}
	private static bool ShouldPersistDocument(EditorDocument? document)
	{
		if (document == null)
		{
			return false;
		}

		return string.IsNullOrWhiteSpace(document.DocumentKind) ||
		       string.Equals(document.DocumentKind, "Query", StringComparison.OrdinalIgnoreCase);
	}

	private void BuildDisconnectedExplorerTree()
	{
		PruneConnectedConnectionIds();
		Dictionary<string, ObjectNode> existingConnectionRoots = ExplorerNodes
			.Where(node => node.IsConnectionNode && ExplorerNodeUtilities.TryGetConnectionIdFromNodeKey(node.Key, out _))
			.ToDictionary(node => ExplorerNodeUtilities.ExtractConnectionIdFromNodeKey(node.Key), node => node, StringComparer.OrdinalIgnoreCase);
		ExplorerNodes.Clear();
		if (ConnectionProfiles.Count == 0)
		{
			ExplorerNodes.Add(new ObjectNode
			{
				Name = UiText.NoSavedConnections,
				Type = "empty",
				Key = "no-connections",
				IsLoaded = true
			});
			SetExplorerFocus(null);
			return;
		}
		foreach (ConnectionProfile connectionProfile in ConnectionProfiles)
		{
			existingConnectionRoots.TryGetValue(connectionProfile.Id, out ObjectNode? existingNode);
			ObjectNode rootNode = existingNode ?? new ObjectNode();
			ConfigureExplorerConnectionRoot(rootNode, connectionProfile);
			ExplorerNodes.Add(rootNode);
		}
	}

	private void PruneConnectedConnectionIds()
	{
		HashSet<string> validIds = ConnectionProfiles
			.Select(item => item.Id)
			.Where(item => !string.IsNullOrWhiteSpace(item))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		_connectedConnectionIds.RemoveWhere(id => !validIds.Contains(id));
		_schemaStates.Prune(validIds);
		if (ActiveConnectionProfile != null && !validIds.Contains(ActiveConnectionProfile.Id))
		{
			ActiveConnectionProfile = ResolveFallbackActiveConnectionProfile(null, ActiveConnectionProfile.Id);
		}
	}

	private void ConfigureExplorerConnectionRoot(ObjectNode node, ConnectionProfile profile)
	{
		DatabaseProviderDefinition? provider = _providerCatalog.Find(profile.ProviderName);
		bool isConnected = _connectedConnectionIds.Contains(profile.Id);
		bool isActive = ActiveConnectionProfile != null && string.Equals(ActiveConnectionProfile.Id, profile.Id, StringComparison.OrdinalIgnoreCase);
		ExplorerNodeUtilities.ConfigureConnectionRoot(node, profile, provider, isConnected, isActive, IsSchemaOpen);
	}

	private bool IsSchemaOpen(string connectionProfileId, string? schemaName)
	{
		string normalizedSchema = NormalizeSchemaSelection(schemaName);
		if (string.IsNullOrWhiteSpace(connectionProfileId) || string.IsNullOrWhiteSpace(normalizedSchema))
		{
			return false;
		}

		if (_schemaStates.ContainsOpenedSchema(connectionProfileId, normalizedSchema))
		{
			return true;
		}

		if (HasOpenedSchemaNode(connectionProfileId, normalizedSchema))
		{
			_schemaStates.MarkSchemaOpen(connectionProfileId, normalizedSchema);
			return true;
		}

		return false;
	}
	private bool HasOpenedSchemaNode(string connectionProfileId, string schemaName)
	{
		return ExplorerNodeUtilities.FindOpenedSchemaNode(ExplorerNodes, connectionProfileId, schemaName)?.IsSchemaOpened == true;
	}

	private void RemoveOpenedSchemasForConnection(string connectionProfileId)
	{
		if (string.IsNullOrWhiteSpace(connectionProfileId))
		{
			return;
		}

		_schemaStates.RemoveOpenedSchemasForConnection(connectionProfileId);
	}

	private void ApplyExplorerNodes(IReadOnlyList<ObjectNode> nodes)
	{
		ExplorerNodes.Clear();
		foreach (ObjectNode node in nodes)
		{
			ExplorerNodes.Add(node);
		}
	}

	private void ApplyExecutionResult(QueryExecutionResult result, bool includePlan)
	{
		string providerName = SelectedDocumentConnectionProfile?.ProviderName ?? ActiveConnectionProfile?.ProviderName ?? string.Empty;
		ExecutionPlanViewItem? executionPlanViewItem = ExecutionResultStateApplier.ApplyToResultSets(ResultSets, result, includePlan, providerName, UiText, AppendExecutionPipelineLog);
		SelectedResultSet = ResultSets.FirstOrDefault();
		OnPropertyChanged("HasResults");
		LastResultRowCount = result.ResultSets.Sum((QueryResultSet set) => set.Rows.Count);
		LastExecutionTimeText = $"{result.Duration.TotalMilliseconds:0} ms";
		OnPropertyChanged("LastResultRowCountText");
		OnPropertyChanged("SelectedDocumentRowCountDisplay");
		OnPropertyChanged("SelectedDocumentDurationDisplay");
		string text = executionPlanViewItem?.Summary ?? result.Summary;
		UpdateResultPreview(text + Environment.NewLine + string.Format(UiText.ExecutionDurationLineFormat, result.Duration.TotalMilliseconds));
		DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
		selectedDocumentState.ResetResultScrollOnNextRender = ResultSets.Any(ResultSetViewItemFactory.IsTabular);
	}

	private void ApplyExecutionResultToState(DocumentExecutionState state, QueryExecutionResult result, IReadOnlyList<ResultSetViewItem> builtResultSets, bool includePlan)
	{
		string providerName = SelectedDocumentConnectionProfile?.ProviderName ?? ActiveConnectionProfile?.ProviderName ?? string.Empty;
		ExecutionResultStateApplier.ApplyToState(state, result, builtResultSets, includePlan, providerName, UiText);
	}

	private static void AppendExecutionPipelineLog(string message)
	{
		if (!DiagnosticLoggingEnabled)
		{
			return;
		}

		try
		{
			string? directoryName = Path.GetDirectoryName(ExecutionPipelineLogPath);
			if (!string.IsNullOrWhiteSpace(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.AppendAllLines(ExecutionPipelineLogPath, new[] { $"{DateTime.Now:O} {message}" });
		}
		catch
		{
		}
	}

	private async Task EnrichResultColumnCommentsAsync(IEnumerable<ResultSetViewItem> resultSets, ConnectionProfile? profile, string schemaName, CancellationToken cancellationToken)
	{
		await ResultColumnMetadataEnricher.EnrichAsync(_databaseExplorerService, resultSets, profile, schemaName, cancellationToken);
	}

	private async Task ExportSelectedCommentWorkspaceCsvCoreAsync(CommentMaintenanceWorkspaceState state, string filePath, CancellationToken cancellationToken)
	{
		await _commentMaintenanceService.ExportCsvAsync(state.ToWorkspace(), filePath, cancellationToken);
		state.SetExportSummary(filePath);
		NotifySelectedCommentWorkspaceChanged();
	}
	private async Task RefreshOpenResultColumnCommentsForSchemaAsync(ConnectionProfile profile, string schemaName, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(schemaName))
		{
			return;
		}

		string effectiveSchema = schemaName.Trim();
		foreach (EditorDocument document in Documents)
		{
			if (!string.Equals(document.DocumentKind, "Query", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (!string.Equals(document.ConnectionProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (!string.Equals(ResolveDocumentEffectiveSchema(document, profile), effectiveSchema, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			DocumentExecutionState state = EnsureDocumentState(document);
			if (state.ResultSets.Count == 0)
			{
				continue;
			}

			await EnrichResultColumnCommentsAsync(state.ResultSets, profile, effectiveSchema, cancellationToken);
			if (document == SelectedDocument)
			{
				NotifySelectedDocumentStateChanged(rebuildWorkspaceTabs: false);
			}
		}
	}

	private void ClearExecutionResults()
	{
		ResultSets.Clear();
		SelectedResultSet = null;
		OnPropertyChanged("HasResults");
		LastResultRowCount = 0;
		LastExecutionTimeText = "--";
		OnPropertyChanged("LastResultRowCountText");
		UpdateResultPreview(UiText.ResultPlaceholder);
	}

	private void UpdateConnectionStatus()
	{
		int connectedCount = _connectedConnectionIds.Count;
		if (connectedCount <= 0)
		{
			ConnectionStatus = string.Format(UiText.NoActiveConnectionFormat, ConnectionProfiles.Count);
			return;
		}

		if (connectedCount == 1 && ActiveConnectionProfile != null)
		{
			ConnectionStatus = string.Format(UiText.ActiveConnectionFormat, ActiveConnectionProfile.Name, ActiveConnectionProfile.ProviderName);
			return;
		}

		string activeName = ActiveConnectionProfile?.Name ?? ConnectionProfiles.FirstOrDefault(item => _connectedConnectionIds.Contains(item.Id))?.Name ?? "connection";
		ConnectionStatus = string.Format(CultureInfo.CurrentCulture, UiText.MultipleConnectionsStatusFormat, connectedCount, activeName);
	}

	private void UpdateResultPreview(string message)
	{
		ResultPreview = message;
	}

	private void SeedLogs(EditorSessionState session)
	{
		Logs.Clear();
		AddLog("Created Avalonia MVVM shell.");
		AddLog($"Loaded {ConnectionProfiles.Count} saved connection profile(s).");
		AddLog($"Restored {Documents.Count} editor tab(s).");
		AddLog($"Provider catalog contains {Providers.Count} provider definition(s).");
		if (session.Documents.Count == 0)
		{
			AddLog("Started with a fresh query tab.");
		}
	}

	private void AddLog(string message)
	{
		Logs.Add(message);
		LogPreview = message;
	}

	private EditorDocument CreateQueryDocument(string? title, string content, string connectionProfileId, string schemaName)
	{
		EditorDocument editorDocument = new EditorDocument
		{
			Title = (string.IsNullOrWhiteSpace(title) ? $"Query {Documents.Count + 1}" : title),
			DocumentKind = "Query",
			ConnectionProfileId = connectionProfileId ?? string.Empty,
			DefaultSchema = schemaName ?? string.Empty,
			Content = content ?? string.Empty
		};
		Documents.Add(editorDocument);
		SelectedDocument = editorDocument;
		return editorDocument;
	}

	private ConnectionProfile CreateDefaultProfile()
	{
		string providerName = Providers.FirstOrDefault()?.Name ?? "SqlServer";
		return new ConnectionProfile
		{
			Name = $"Connection {ConnectionProfiles.Count + 1}",
			GroupName = "Default",
			EnvironmentTag = "DEV",
			ProviderName = providerName,
			CapabilityLevel = (Providers.FirstOrDefault((DatabaseProviderDefinition item) => string.Equals(item.Name, providerName, StringComparison.OrdinalIgnoreCase))?.SupportLevel ?? "Experimental"),
			Port = ConnectionProfileUtilities.GetDefaultPort(providerName).GetValueOrDefault(),
			OracleConnectionMode = "HostService",
			OraclePort = 1521,
			SavePassword = true
		};
	}

	private static void NormalizeOracleSettings(ConnectionProfile profile)
	{
		ConnectionProfileUtilities.NormalizeOracleSettings(profile);
	}
	private static void CopyConnectionProfileValues(ConnectionProfile target, ConnectionProfile source, bool preserveId)
	{
		ConnectionProfileUtilities.CopyValues(target, source, preserveId);
	}
	private bool MatchesConnectionFilter(ConnectionProfile profile)
	{
		return ConnectionProfileFilter.Matches(
			profile,
			UiText,
			SelectedEnvironmentFilter,
			SelectedGroupFilter,
			SelectedCapabilityFilter,
			ConnectionSearchText,
			FavoritesOnly);
	}

	private static ConnectionProfile CloneConnectionProfile(ConnectionProfile source)
	{
		return ConnectionProfileUtilities.Clone(source);
	}

	private DocumentExecutionState GetSelectedDocumentState()
	{
		return EnsureDocumentState(SelectedDocument);
	}

	private DocumentExecutionState EnsureDocumentState(EditorDocument? document)
	{
		return _workspaceStates.EnsureDocument(document, UiText.ExecutionReady, ResolveInitialSchemaSelection);
	}

	private CommentMaintenanceWorkspaceState EnsureCommentWorkspaceState(EditorDocument? document)
	{
		return _workspaceStates.EnsureComment(document);
	}

	private CommentMaintenanceWorkspaceState? GetSelectedCommentWorkspaceState()
	{
		return _workspaceStates.GetSelectedComment(SelectedDocumentIsCommentMaintenance, SelectedDocument);
	}

	private ModelDiagramWorkspaceState EnsureModelDiagramState(EditorDocument? document)
	{
		return _workspaceStates.EnsureModelDiagram(document);
	}

	private ModelDiagramWorkspaceState? GetSelectedModelDiagramState()
	{
		return _workspaceStates.GetSelectedModelDiagram(SelectedDocumentIsModelDiagram, SelectedDocument);
	}

	private ObjectEditorState EnsureObjectEditorState(EditorDocument? document)
	{
		return _workspaceStates.EnsureObjectEditor(document);
	}

	private ObjectEditorState? GetSelectedObjectEditorState()
	{
		return _workspaceStates.GetSelectedObjectEditor(SelectedDocumentIsObjectEditor, SelectedDocument);
	}

	private void CommentWorkspaceState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (!ReferenceEquals(sender, GetSelectedCommentWorkspaceState()))
		{
			return;
		}

		NotifySelectedCommentWorkspaceChanged();
	}

	private void NotifySelectedCommentWorkspaceChanged()
	{
		OnPropertiesChanged(MainWindowViewModelPropertyGroups.SelectedCommentWorkspace);
	}

	private void ModelDiagramState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (!ReferenceEquals(sender, GetSelectedModelDiagramState()))
		{
			return;
		}

		OnPropertiesChanged(MainWindowViewModelPropertyGroups.ResolveSelectedModelDiagramChanges(e.PropertyName));
	}

	private void NotifySelectedModelDiagramChanged()
	{
		OnPropertiesChanged(MainWindowViewModelPropertyGroups.SelectedModelDiagram);
	}

	private void ObjectEditorState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (!ReferenceEquals(sender, GetSelectedObjectEditorState()))
		{
			return;
		}

		NotifySelectedObjectEditorChanged();
	}

	private void NotifySelectedObjectEditorChanged()
	{
		OnPropertiesChanged(MainWindowViewModelPropertyGroups.SelectedObjectEditor);
	}

	private ObjectEditorSaveRequest BuildSelectedObjectEditorSaveRequest()
	{
		if (SelectedDocument == null)
		{
			throw new InvalidOperationException("当前没有可保存的对象编辑页。");
		}

		return new ObjectEditorSaveRequest
		{
			SchemaName = SelectedDocument.ObjectSchemaName,
			ObjectName = SelectedDocument.ObjectRawName,
			ObjectType = SelectedDocument.ObjectType,
			Definition = SelectedDocument.Content ?? string.Empty
		};
	}

	private void OnPropertiesChanged(IEnumerable<string> propertyNames)
	{
		foreach (string propertyName in propertyNames)
		{
			OnPropertyChanged(propertyName);
		}
	}

	private void NotifySelectedDocumentStateChanged(bool rebuildWorkspaceTabs = true)
	{
		if (rebuildWorkspaceTabs && !SelectedDocumentIsObjectEditor && !SelectedDocumentIsModelDiagram)
		{
			RebuildWorkspaceTabs(GetSelectedDocumentState());
		}
		OnPropertiesChanged(MainWindowViewModelPropertyGroups.SelectedDocumentState);
		NotifyWorkbenchContextChanged();
		if (rebuildWorkspaceTabs && !SelectedDocumentIsObjectEditor && !SelectedDocumentIsModelDiagram)
		{
			OnPropertiesChanged(MainWindowViewModelPropertyGroups.SelectedDocumentWorkspace);
		}
		if (SelectedDocumentIsCommentMaintenance)
		{
			NotifySelectedCommentWorkspaceChanged();
		}
		else if (SelectedDocumentIsModelDiagram)
		{
			NotifySelectedModelDiagramChanged();
		}
		else if (SelectedDocumentIsObjectEditor)
		{
			NotifySelectedObjectEditorChanged();
		}
	}

	private void NotifySelectedDocumentValueDetailChanged()
	{
		OnPropertiesChanged(MainWindowViewModelPropertyGroups.SelectedDocumentValueDetail);
	}

	private void RebuildWorkspaceTabs(DocumentExecutionState state)
	{
		ResultWorkspaceTabBuilder.Rebuild(state, UiText);
	}

	private void UpdateSelectedDocumentConnectionLabel()
	{
		DocumentExecutionState selectedDocumentState = GetSelectedDocumentState();
		ConnectionProfile? selectedDocumentConnectionProfile = SelectedDocumentConnectionProfile;
		if (selectedDocumentConnectionProfile == null)
		{
			selectedDocumentState.ConnectionLabel = UiText.NotConnectedDatabase;
			selectedDocumentState.ConnectionForeground = "#9CA3AF";
		}
		else
		{
			bool isLinked = IsConnectionProfileConnected(selectedDocumentConnectionProfile);
			string label = selectedDocumentConnectionProfile.Name + " (" + selectedDocumentConnectionProfile.ProviderName + ")";
			if (!isLinked)
			{
				selectedDocumentState.ConnectionLabel = label + " - " + UiText.NotConnectedDatabase;
				selectedDocumentState.ConnectionForeground = "#B45309";
			}
			else
			{
				string schemaName = NormalizeSchemaSelection(GetSelectedDocumentState().SelectedSchema);
				selectedDocumentState.ConnectionLabel = string.IsNullOrWhiteSpace(schemaName)
					? label
					: label + " / " + schemaName;
				selectedDocumentState.ConnectionForeground = "#2F6FDB";
			}
		}
		OnPropertyChanged("SelectedDocumentConnectionLabel");
		OnPropertyChanged("SelectedDocumentConnectionForeground");
	}

	private static string ResolvePreferredSchema(ConnectionProfile? profile, IEnumerable<string> schemas)
	{
		return SchemaSelection.ResolvePreferred(profile, schemas);
	}

	private static string NormalizeSchemaSelection(string? schema)
	{
		return SchemaSelection.Normalize(schema);
	}

	private static string NormalizeSchemaDisplaySelection(string? schema)
	{
		return SchemaSelection.NormalizeDisplay(schema);
	}

	private static string ResolveInitialSchemaSelection(EditorDocument? document)
	{
		return SchemaSelection.ResolveInitial(document);
	}

	private static string ResolveDocumentEffectiveSchema(EditorDocument document, ConnectionProfile profile)
	{
		return SchemaSelection.ResolveDocumentEffectiveSchema(document, profile);
	}

	private static bool ContainsSchemaSelection(IEnumerable<string> schemas, string schema)
	{
		return SchemaSelection.Contains(schemas, schema);
	}

	private void UpdateRecentFileDocumentBinding(EditorDocument? document)
	{
		if (document != null && !string.IsNullOrWhiteSpace(document.FilePath))
		{
			RecentFileEntry? recentFileEntry = RecentFiles.FirstOrDefault((RecentFileEntry item) => string.Equals(item.FilePath, document.FilePath, StringComparison.OrdinalIgnoreCase));
			if (recentFileEntry != null)
			{
				recentFileEntry.ConnectionProfileId = document.ConnectionProfileId ?? string.Empty;
				recentFileEntry.DefaultSchema = document.DefaultSchema ?? string.Empty;
			}
		}
	}

	private void OnSelectedDocumentChanged(EditorDocument? value)
	{
		EnsureDocumentState(value);
		if (value != null && string.Equals(value.DocumentKind, "CommentMaintenance", StringComparison.OrdinalIgnoreCase))
		{
			EnsureCommentWorkspaceState(value);
		}
		if (value != null && string.Equals(value.DocumentKind, "ModelDiagram", StringComparison.OrdinalIgnoreCase))
		{
			EnsureModelDiagramState(value);
		}
		if (value != null && string.Equals(value.DocumentKind, "ObjectEditor", StringComparison.OrdinalIgnoreCase))
		{
			EnsureObjectEditorState(value);
		}
		UpdateSelectedDocumentConnectionLabel();
		OnPropertyChanged("SelectedDocumentConnectionProfile");
		NotifySelectedDocumentStateChanged();
	}

	private void OnCurrentLanguageCodeChanged(string value)
	{
		UiTextSet previousText = UiText;
		UiText = UiTextResourceStore.Get(value);
		NormalizeLocalizedFilterSelections(previousText);
		ApplicationTitle = UiText.ApplicationTitle;
		if (!HasResults)
		{
			ResultPreview = UiText.ResultPlaceholder;
		}
		// 切语言只替换系统默认文案，不覆盖执行结果和用户正在看的日志。
		if (string.IsNullOrWhiteSpace(ExecutionStatus) || IsKnownLocalizedValue(ExecutionStatus, text => text.ExecutionReady))
		{
			ExecutionStatus = UiText.ExecutionReady;
		}
		if (string.IsNullOrWhiteSpace(LogPreview) || IsKnownLocalizedValue(LogPreview, text => text.WorkspaceBootstrapped))
		{
			LogPreview = UiText.WorkspaceBootstrapped;
		}
		UpdateConnectionStatus();
		ResultSetViewItemFactory.AssignNavigationTitles(ResultSets, UiText.Results);
		foreach (DocumentExecutionState state in _workspaceStates.DocumentStates)
		{
			ResultSetViewItemFactory.AssignNavigationTitles(state.ResultSets, UiText.Results);
		}
		OnPropertiesChanged(MainWindowViewModelPropertyGroups.LocalizationRefresh);
		NotifyConnectionEditorStateChanged();
		NotifySelectedDocumentStateChanged();
	}

	private void NormalizeLocalizedFilterSelections(UiTextSet previousText)
	{
		SelectedEnvironmentFilter = NormalizeLocalizedFilterValue(
			SelectedEnvironmentFilter,
			UiText.AllEnvironments,
			previousText.AllEnvironments,
			KnownLocalizedValues(text => text.AllEnvironments));
		SelectedGroupFilter = NormalizeLocalizedFilterValue(
			SelectedGroupFilter,
			UiText.AllGroups,
			previousText.AllGroups,
			KnownLocalizedValues(text => text.AllGroups));
		SelectedCapabilityFilter = NormalizeLocalizedFilterValue(
			SelectedCapabilityFilter,
			UiText.AllCapabilities,
			previousText.AllCapabilities,
			KnownLocalizedValues(text => text.AllCapabilities));
		SelectedCapabilityFilter = NormalizeLocalizedFilterValue(
			SelectedCapabilityFilter,
			UiText.Verified,
			previousText.Verified,
			KnownLocalizedValues(text => text.Verified));
		SelectedCapabilityFilter = NormalizeLocalizedFilterValue(
			SelectedCapabilityFilter,
			UiText.Experimental,
			previousText.Experimental,
			KnownLocalizedValues(text => text.Experimental));
		SelectedCapabilityFilter = NormalizeLocalizedFilterValue(
			SelectedCapabilityFilter,
			UiText.Planned,
			previousText.Planned,
			KnownLocalizedValues(text => text.Planned));
	}

	private static string NormalizeLocalizedFilterValue(string currentValue, string targetValue, string previousValue, IEnumerable<string> aliases)
	{
		return aliases.Append(previousValue).Any(alias => string.Equals(currentValue, alias, StringComparison.OrdinalIgnoreCase))
			? targetValue
			: currentValue;
	}

	private static bool IsKnownLocalizedValue(string currentValue, Func<UiTextSet, string> selector)
	{
		return KnownLocalizedValues(selector).Any(value => string.Equals(currentValue, value, StringComparison.OrdinalIgnoreCase));
	}

	private static IEnumerable<string> KnownLocalizedValues(Func<UiTextSet, string> selector)
	{
		return UiTextResourceStore.GetAllTextSets()
			.Select(selector)
			.Where(value => !string.IsNullOrWhiteSpace(value));
	}

	private void OnConnectionSearchTextChanged(string value)
	{
		OnPropertyChanged("FilteredConnectionProfiles");
	}

	private void OnSelectedEnvironmentFilterChanged(string value)
	{
		OnPropertyChanged("FilteredConnectionProfiles");
	}

	private void OnSelectedGroupFilterChanged(string value)
	{
		OnPropertyChanged("FilteredConnectionProfiles");
	}

	private void OnSelectedCapabilityFilterChanged(string value)
	{
		OnPropertyChanged("FilteredConnectionProfiles");
	}

	private void OnFavoritesOnlyChanged(bool value)
	{
		OnPropertyChanged("FilteredConnectionProfiles");
	}

	private void OnSelectedResultHeaderModeChanged(string value)
	{
		OnPropertyChanged("SelectedResultHeaderMode");
	}
}
