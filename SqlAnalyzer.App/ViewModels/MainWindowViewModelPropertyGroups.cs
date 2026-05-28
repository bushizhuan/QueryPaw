using System;
using System.Collections.Generic;
using SqlAnalyzer.App.Models;

namespace SqlAnalyzer.App.ViewModels;

internal static class MainWindowViewModelPropertyGroups
{
    public static readonly string[] SelectedDocumentState =
    {
        "SelectedDocumentHasResults",
        "SelectedDocumentIsExecuting",
        "SelectedDocumentIsRenderingResults",
        "SelectedDocumentCanStartExecution",
        "SelectedDocumentIsBusy",
        "SelectedDocumentShouldShowResultWorkspace",
        "SelectedDocumentCanToggleResultWorkspace",
        "SelectedDocumentResultWorkspaceToggleText",
        "SelectedDocumentBusyText",
        "SelectedDocumentExecutionStatus",
        "SelectedDocumentConnectionLabel",
        "SelectedDocumentConnectionForeground",
        "SelectedDocumentConnectionProfile",
        "SelectedDocumentDurationText",
        "SelectedDocumentDurationDisplay",
        "SelectedDocumentMessageText",
        "SelectedDocumentHasMessage",
        "SelectedDocumentLastExecutionError",
        "SelectedDocumentValueDetail",
        "SelectedDocumentValueDetailPanelVisible",
        "SelectedDocumentValueDetailButtonVisible",
        "SelectedDocumentExecutionPlan",
        "SelectedDocumentHasExecutionPlan",
        "SelectedDocumentExecutionPlanSummary",
        "SelectedDocumentExecutionPlanText",
        "SelectedDocumentExecutionPlanFindingsText",
        "SelectedDocumentHasExecutionPlanFindings",
        "SelectedDocumentHasTabularResult",
        "SelectedDocumentShouldShowResultNavigationBar",
        "SelectedDocumentShouldShowResultSetNavigator",
        "SelectedDocumentCanEnterEditMode",
        "SelectedDocumentIsEditingResult",
        "SelectedDocumentShowEditButton",
        "SelectedDocumentShowEditActionButtons",
        "SelectedDocumentCanSaveEditedResult",
        "SelectedDocumentCanCancelEditedResult",
        "SelectedDocumentEditDisabledReason",
        "SelectedDocumentRowCountText",
        "SelectedDocumentRowCountDisplay",
        "SelectedDocumentHasPreviewTruncatedResult",
        "SelectedDocumentPreviewTruncatedNotice",
        "SelectedDocumentCanLoadMoreRows",
        "SelectedDocumentLoadMoreRowsText",
        "SelectedDocumentLastExecutedSql",
        "SelectedDocumentLastExecutedSqlBaseOffset",
        "SelectedDocumentLastExecutionIncludedPlan",
        "SelectedDocumentNextPreviewLimit",
        "SelectedDocumentSchemas",
        "SelectedDocumentSchema",
        "SelectedDocumentIsQuery",
        "SelectedDocumentIsCommentMaintenance",
        "SelectedDocumentIsModelDiagram",
        "SelectedDocumentIsObjectEditor",
        "SelectedDocumentIsQueryHistory",
        "SelectedDocumentUsesTextEditor"
    };

    public static readonly string[] SelectedDocumentWorkspace =
    {
        "SelectedDocumentHasMultipleResultSets",
        "SelectedDocumentResultSets",
        "SelectedDocumentTabularResultSets",
        "SelectedDocumentShouldShowResultNavigationBar",
        "SelectedDocumentShouldShowResultSetNavigator",
        "SelectedDocumentWorkspaceTabs",
        "SelectedDocumentSelectedResultSet",
        "SelectedDocumentSelectedWorkspaceTab",
        "SelectedDocumentSelectedWorkspaceTabIsResult",
        "SelectedDocumentSelectedWorkspaceTabIsMessage",
        "SelectedDocumentSelectedWorkspaceTabIsPlan"
    };

    public static readonly string[] SelectedDocumentValueDetail =
    {
        "SelectedDocumentValueDetail",
        "SelectedDocumentValueDetailPanelVisible",
        "SelectedDocumentValueDetailButtonVisible"
    };

    public static readonly string[] ConnectionEditor =
    {
        "ConnectionEditorMode",
        "ConnectionEditorDraft",
        "ConnectionEditorHasProfile",
        "ConnectionEditorHasNoProfile",
        "ConnectionEditorCanEditFields",
        "ConnectionEditorIsReadOnly",
        "ConnectionEditorCanChangeSelection",
        "ConnectionEditorCanStartNew",
        "ConnectionEditorShowViewActions",
        "ConnectionEditorShowEditActions",
        "ConnectionEditorCanEditSelected",
        "ConnectionEditorCanDuplicateSelected",
        "ConnectionEditorCanDeleteSelected",
        "ConnectionEditorCanUseSelected",
        "ConnectionEditorCanTest",
        "ConnectionEditorCanSave",
        "ConnectionEditorModeText",
        "ConnectionEditorTitleText",
        "ConnectionEditorHintText",
        "ConnectionEditorEmptyText",
        "ConnectionEditorBasicSectionText",
        "ConnectionEditorProviderSectionText",
        "ConnectionEditorEndpointSectionText",
        "ConnectionEditorAuthSectionText",
        "ConnectionEditorAdvancedSectionText",
        "ConnectionEditorNotesSectionText",
        "EnvironmentTagOptions"
    };

    public static readonly string[] ConnectionLists =
    {
        "GroupFilters",
        "FilteredConnectionProfiles",
        "RecentConnectionProfiles",
        "HasRecentConnections"
    };

    public static readonly string[] OracleUiState =
    {
        "IsOracleProfileSelected",
        "IsMongoProfileSelected",
        "IsServerDatabaseProfileSelected",
        "IsGenericConnectionFormVisible",
        "IsGenericPortVisible",
        "IsOracleHostMode",
        "IsOracleTnsMode",
        "SelectedProviderDisplayName",
        "ServerFieldLabel",
        "DatabaseFieldLabel",
        "AuthenticationModeOptions",
        "SelectedProviderPortHint"
    };

    public static readonly string[] SelectedCommentWorkspace =
    {
        "SelectedCommentWorkspace",
        "SelectedCommentTables",
        "SelectedCommentColumns",
        "SelectedCommentTablesCountText",
        "SelectedCommentColumnsCountText",
        "SelectedCommentWorkspaceSelectedTable",
        "SelectedCommentTableFilterOptions",
        "SelectedCommentWorkspaceIsLoaded",
        "SelectedCommentWorkspaceIsBusy",
        "SelectedCommentWorkspaceHasChanges",
        "SelectedCommentWorkspaceChangedCount",
        "SelectedCommentWorkspaceChangedCountText",
        "SelectedCommentWorkspaceConnectionName",
        "SelectedCommentWorkspaceSchemaName",
        "SelectedCommentWorkspaceLoadedAtText",
        "SelectedCommentWorkspaceHasSummary",
        "SelectedCommentWorkspaceSummaryText",
        "SelectedCommentWorkspaceSummaryForeground",
        "SelectedCommentWorkspaceOnlyEmpty",
        "SelectedCommentWorkspaceOnlyChanged",
        "SelectedCommentWorkspaceTableKeyword",
        "SelectedCommentWorkspaceColumnKeyword",
        "SelectedCommentWorkspaceTableFilter"
    };

    public static readonly string[] SelectedModelDiagram =
    {
        "SelectedModelDiagram",
        "SelectedModelDiagramIsLoaded",
        "SelectedModelDiagramConnectionName",
        "SelectedModelDiagramSchemaName",
        "SelectedModelDiagramLoadedAtText",
        "SelectedModelDiagramTables",
        "SelectedModelDiagramVisibleNodes",
        "SelectedModelDiagramVisibleRelations",
        "SelectedModelDiagramSelectedTable",
        "SelectedModelDiagramSelectedTableName",
        "SelectedModelDiagramSelectedTableCommentText",
        "SelectedModelDiagramSelectedTablePrimaryKeysText",
        "SelectedModelDiagramSelectedTablePrimaryKeysDisplay",
        "SelectedModelDiagramSelectedTableColumns",
        "SelectedModelDiagramSelectedTableColumnCountDisplay",
        "SelectedModelDiagramSelectedTableReferences",
        "SelectedModelDiagramSelectedTableReferencedBy",
        "SelectedModelDiagramSelectedRelation",
        "SelectedModelDiagramSelectedRelationConstraintName",
        "SelectedModelDiagramSelectedRelationSummaryText",
        "SelectedModelDiagramSelectedRelationParentColumnsText",
        "SelectedModelDiagramSelectedRelationChildColumnsText",
        "SelectedModelDiagramSelectedRelationConstraintDisplay",
        "SelectedModelDiagramSelectedRelationSummaryDisplay",
        "SelectedModelDiagramSelectedRelationParentColumnsDisplay",
        "SelectedModelDiagramSelectedRelationChildColumnsDisplay",
        "SelectedModelDiagramMessageText",
        "SelectedModelDiagramOnlyRelatedTables",
        "SelectedModelDiagramOnlyNeighborhood",
        "SelectedModelDiagramShowColumns",
        "SelectedModelDiagramSearchKeyword",
        "SelectedModelDiagramNeighborhoodDepth",
        "SelectedModelDiagramNeighborhoodDepthText",
        "SelectedModelDiagramZoom",
        "SelectedModelDiagramZoomText",
        "SelectedModelDiagramRenderVersion",
        "SelectedModelDiagramCanvasWidth",
        "SelectedModelDiagramCanvasHeight",
        "SelectedModelDiagramCanReload",
        "SelectedModelDiagramCanExport",
        "SelectedModelDiagramCanExportImage",
        "SelectedModelDiagramCanExpandNeighborhood",
        "SelectedModelDiagramHasSelectedRelation"
    };

    // 模型图刷新比较重，能按字段通知时就别整页重刷。
    private static readonly IReadOnlyDictionary<string, string[]> SelectedModelDiagramChangeMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [nameof(ModelDiagramWorkspaceState.FilteredTables)] = new[]
        {
            "SelectedModelDiagramTables"
        },
        [nameof(ModelDiagramWorkspaceState.VisibleNodes)] = new[]
        {
            "SelectedModelDiagramVisibleNodes",
            "SelectedModelDiagramCanExportImage"
        },
        [nameof(ModelDiagramWorkspaceState.VisibleRelations)] = new[]
        {
            "SelectedModelDiagramVisibleRelations"
        },
        [nameof(ModelDiagramWorkspaceState.SelectedTable)] = new[]
        {
            "SelectedModelDiagramSelectedTable",
            "SelectedModelDiagramSelectedTableName",
            "SelectedModelDiagramSelectedTableCommentText",
            "SelectedModelDiagramSelectedTablePrimaryKeysText",
            "SelectedModelDiagramSelectedTablePrimaryKeysDisplay",
            "SelectedModelDiagramCanExpandNeighborhood"
        },
        [nameof(ModelDiagramWorkspaceState.SelectedTableColumns)] = new[]
        {
            "SelectedModelDiagramSelectedTableColumns",
            "SelectedModelDiagramSelectedTableColumnCountDisplay"
        },
        [nameof(ModelDiagramWorkspaceState.SelectedTableReferenceTexts)] = new[]
        {
            "SelectedModelDiagramSelectedTableReferences"
        },
        [nameof(ModelDiagramWorkspaceState.SelectedTableReferencedByTexts)] = new[]
        {
            "SelectedModelDiagramSelectedTableReferencedBy"
        },
        [nameof(ModelDiagramWorkspaceState.SelectedRelation)] = new[]
        {
            "SelectedModelDiagramSelectedRelation",
            "SelectedModelDiagramSelectedRelationConstraintName",
            "SelectedModelDiagramSelectedRelationSummaryText",
            "SelectedModelDiagramSelectedRelationParentColumnsText",
            "SelectedModelDiagramSelectedRelationChildColumnsText",
            "SelectedModelDiagramSelectedRelationConstraintDisplay",
            "SelectedModelDiagramSelectedRelationSummaryDisplay",
            "SelectedModelDiagramSelectedRelationParentColumnsDisplay",
            "SelectedModelDiagramSelectedRelationChildColumnsDisplay",
            "SelectedModelDiagramHasSelectedRelation"
        },
        [nameof(ModelDiagramWorkspaceState.MessageText)] = new[]
        {
            "SelectedModelDiagramMessageText"
        },
        [nameof(ModelDiagramWorkspaceState.OnlyRelatedTables)] = new[]
        {
            "SelectedModelDiagramOnlyRelatedTables"
        },
        [nameof(ModelDiagramWorkspaceState.OnlyNeighborhood)] = new[]
        {
            "SelectedModelDiagramOnlyNeighborhood"
        },
        [nameof(ModelDiagramWorkspaceState.ShowColumns)] = new[]
        {
            "SelectedModelDiagramShowColumns"
        },
        [nameof(ModelDiagramWorkspaceState.SearchKeyword)] = new[]
        {
            "SelectedModelDiagramSearchKeyword"
        },
        [nameof(ModelDiagramWorkspaceState.NeighborhoodDepth)] = new[]
        {
            "SelectedModelDiagramNeighborhoodDepth",
            "SelectedModelDiagramNeighborhoodDepthText"
        },
        [nameof(ModelDiagramWorkspaceState.Zoom)] = new[]
        {
            "SelectedModelDiagramZoom"
        },
        [nameof(ModelDiagramWorkspaceState.ZoomText)] = new[]
        {
            "SelectedModelDiagramZoomText"
        },
        [nameof(ModelDiagramWorkspaceState.RenderVersion)] = new[]
        {
            "SelectedModelDiagramRenderVersion"
        },
        [nameof(ModelDiagramWorkspaceState.CanvasWidth)] = new[]
        {
            "SelectedModelDiagramCanvasWidth"
        },
        [nameof(ModelDiagramWorkspaceState.CanvasHeight)] = new[]
        {
            "SelectedModelDiagramCanvasHeight"
        },
        [nameof(ModelDiagramWorkspaceState.IsLoaded)] = new[]
        {
            "SelectedModelDiagramIsLoaded"
        }
    };

    public static IEnumerable<string> ResolveSelectedModelDiagramChanges(string? propertyName)
    {
        return propertyName != null && SelectedModelDiagramChangeMap.TryGetValue(propertyName, out string[]? propertyNames)
            ? propertyNames
            : SelectedModelDiagram;
    }

    public static readonly string[] SelectedObjectEditor =
    {
        "SelectedObjectEditor",
        "SelectedObjectEditorConnectionName",
        "SelectedObjectEditorProviderName",
        "SelectedObjectEditorSchemaName",
        "SelectedObjectEditorObjectName",
        "SelectedObjectEditorObjectType",
        "SelectedObjectEditorCapabilityText",
        "SelectedObjectEditorCommentText",
        "SelectedObjectEditorReturnType",
        "SelectedObjectEditorMessageText",
        "SelectedObjectEditorPreviewSql",
        "SelectedObjectEditorCompileResultText",
        "SelectedObjectEditorIsLoaded",
        "SelectedObjectEditorCanRefresh",
        "SelectedObjectEditorCanPreview",
        "SelectedObjectEditorCanValidate",
        "SelectedObjectEditorCanSave"
    };

    public static readonly string[] LocalizationRefresh =
    {
        "UiText",
        "LanguageOptions",
        "ServerFieldLabel",
        "DatabaseFieldLabel",
        "FocusedExplorerContextText",
        "SelectedDocumentRowCountDisplay",
        "SelectedDocumentDurationDisplay",
        "SelectedDocumentBusyText",
        "SelectedDocumentPreviewTruncatedNotice",
        "SelectedDocumentLoadMoreRowsText",
        "SelectedDocumentTabularResultSets",
        "SelectedObjectEditorCapabilityText",
        "SelectedCommentTablesCountText",
        "SelectedCommentColumnsCountText",
        "SelectedCommentWorkspaceChangedCountText",
        "SelectedModelDiagramNeighborhoodDepthText",
        "SelectedModelDiagramSelectedTablePrimaryKeysDisplay",
        "SelectedModelDiagramSelectedTableColumnCountDisplay",
        "SelectedModelDiagramSelectedRelationConstraintDisplay",
        "SelectedModelDiagramSelectedRelationSummaryDisplay",
        "SelectedModelDiagramSelectedRelationParentColumnsDisplay",
        "SelectedModelDiagramSelectedRelationChildColumnsDisplay",
        "EnvironmentFilters",
        "EnvironmentTagOptions",
        "CapabilityFilters",
        "GroupFilters",
        "FilteredConnectionProfiles",
        "RecentConnectionProfiles",
        "HasRecentConnections"
    };
}
