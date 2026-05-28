using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Avalonia.Visuals;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.TextMate;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.App.Services;
using SqlAnalyzer.App.ViewModels;
using SqlAnalyzer.Core.Models;
using TextMateSharp.Grammars;
using BlueIcon = SqlAnalyzer.App.Controls.BlueIcon;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace SqlAnalyzer.App.Views;

public partial class MainWindow : Window
{
	private static readonly string StartupLogPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlAnalyzer.Next", "startup-ui.log");

	private static readonly bool DiagnosticLoggingEnabled = string.Equals(Environment.GetEnvironmentVariable("SQLANALYZER_DIAGNOSTIC_LOGS"), "1", StringComparison.OrdinalIgnoreCase);

	private readonly RegistryOptions _registryOptions = new RegistryOptions(ThemeName.LightPlus);

	private object? _textMateInstallation;

	private string _currentHighlightExtension = ".sql";

	private int _suppressCompletionDepth;

	private DateTime _localizedCompletionKeepAliveUntilUtc;

	private CancellationTokenSource? _completionRefreshDelayCancellationTokenSource;

	private int _deferredCompletionRefreshVersion;

	private string? _pendingCompletionRefreshText;

	private int _pendingCompletionRefreshCaretOffset;

	private CancellationTokenSource? _resultRenderCancellationTokenSource;

	private int _resultRenderRequestVersion;

	private int _scheduledResultRenderVersion;

	private string _lastHighlightProbeSignature = string.Empty;

	private static readonly TimeSpan FormattingTimeout = TimeSpan.FromSeconds(4L);

	private readonly ResultWorkspaceController _resultWorkspaceController = new ResultWorkspaceController();

	private readonly CompletionController _completionController = new CompletionController();

	private readonly ConnectionCenterController _connectionCenterController = new ConnectionCenterController();

	private readonly ExecutionController _executionController = new ExecutionController();

	private readonly SearchController _searchController = new SearchController();

	private readonly SearchPanelController _searchPanelController = new SearchPanelController();

	private readonly ExplorerActionController _explorerActionController = new ExplorerActionController();

	private readonly ExplorerOpenController _explorerOpenController = new ExplorerOpenController();

	private readonly DocumentLifecycleController _documentLifecycleController = new DocumentLifecycleController();

	private readonly FindTableController _findTableController = new FindTableController();

	private TableDesignCoordinator? _tableDesignCoordinator;

	private ObjectDetailsCoordinator? _objectDetailsCoordinator;

	private ContextMenu? _openedTopMenu;

	private double _lastResultWorkspaceHeight = 260.0;

	private bool _isSyncingConnectionEditor;

	private MainWindowViewModel? _subscribedViewModel;

	private bool _resultWorkspaceStateSyncPending;

	private bool _resultWorkspaceRenderRequested;

	private bool _lastResultWorkspaceVisible;

	private bool _synchronizingResultVerticalScroll;

	private bool _synchronizingResultVerticalScrollBar;

	private Border? _activeEditableCellBorder;

	private ResultCellContext? _activeEditableCellContext;

	private int _lastCompletionPopupCaretOffset = -1;

	private Vector _lastCompletionPopupScrollOffset;

	private bool _isPanningModelDiagram;

	private Point _lastModelDiagramPanPoint;

	private Vector _modelDiagramPanStartOffset;

	private bool _isDraggingModelDiagramNode;

	private string _draggingModelDiagramNodeTableName = string.Empty;

	private Point _lastModelDiagramNodeDragPoint;

	private bool _suppressNextModelDiagramNodeClick;

	private bool _didMoveModelDiagramNode;

	private bool _modelDiagramRenderPending;

	private string _lastModelDiagramRenderedDocumentId = string.Empty;

	private int _lastModelDiagramRenderedVersion = -1;

	private double _lastModelDiagramRenderedZoom = -1.0;

	private Rect _lastModelDiagramRenderedViewport;

	private bool _hasLastModelDiagramRenderedViewport;

	private string _lastModelDiagramAutoCenteredTableName = string.Empty;

	private string _lastTextEditorDocumentKind = string.Empty;

	private CancellationTokenSource? _sessionAutosaveCancellationTokenSource;

	private int _sessionAutosaveVersion;

	private Task _sessionAutosaveTask = Task.CompletedTask;
































	private MainWindowViewModel ViewModel => (MainWindowViewModel?)base.DataContext ?? throw new InvalidOperationException("Main window data context is not ready.");

	private TableDesignCoordinator TableDesignCoordinator => _tableDesignCoordinator ?? (_tableDesignCoordinator = new TableDesignCoordinator(ViewModel, AppendUiLog, MessageBoxAsync));

	private ObjectDetailsCoordinator ObjectDetailsCoordinator => _objectDetailsCoordinator ?? (_objectDetailsCoordinator = new ObjectDetailsCoordinator(ViewModel, AppendUiLog, MessageBoxAsync));
	private static void AppendUiLog(string message)
	{
		if (!DiagnosticLoggingEnabled)
		{
			return;
		}

		string? directoryName = System.IO.Path.GetDirectoryName(StartupLogPath);
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		File.AppendAllText(StartupLogPath, message + Environment.NewLine);
	}

	private static bool ContainsLocalizedText(string? value)
	{
		return !string.IsNullOrWhiteSpace(value) && value.Any(static ch => ch > 127);
	}

	private static string ToCompletionLogValue(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}

		return value.Replace("\r", "\\r").Replace("\n", "\\n");
	}
	public MainWindow()
	{
		InitializeComponent();
		if (ResultBodyScrollViewer != null)
		{
			ResultBodyScrollViewer.PropertyChanged += ResultBodyScrollViewer_PropertyChanged;
		}
		if (ResultFixedBodyScrollViewer != null)
		{
			ResultFixedBodyScrollViewer.PropertyChanged += ResultBodyScrollViewer_PropertyChanged;
		}
		if (ModelDiagramScrollViewer != null)
		{
			ModelDiagramScrollViewer.PropertyChanged += ModelDiagramScrollViewer_PropertyChanged;
		}
		base.DataContextChanged += MainWindow_DataContextChanged;
		AttachViewModel(TryGetViewModel());
		InitializeEditor();
		EditorTextBox.AddHandler(InputElement.KeyDownEvent, EditorTextBox_PreviewKeyDown, RoutingStrategies.Tunnel);
		EditorTextBox.AddHandler(InputElement.TextInputEvent, EditorTextBox_TextInput, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
		base.Opened += async delegate
		{
			try
			{
				AppendUiLog($"Opened: {DateTime.Now:O}");
				EnsureVisibleOnStartup();
				SyncConnectionEditor();
				ApplyEditorWrapMode();
				PopulateRecentFilesMenu();
				UpdateResultWorkspaceVisibility();
				if (ViewModel.SelectedDocumentIsCommentMaintenance)
				{
					_lastTextEditorDocumentKind = string.Empty;
				}
				else if (ViewModel.SelectedDocumentIsModelDiagram && !ViewModel.SelectedModelDiagramIsLoaded)
				{
					await ViewModel.LoadSelectedModelDiagramAsync();
					RequestModelDiagramRender();
				}
				else if (ViewModel.SelectedDocumentIsObjectEditor && !ViewModel.SelectedObjectEditorIsLoaded)
				{
					await ViewModel.LoadSelectedObjectEditorAsync();
					SyncEditorFromDocument();
					ApplySyntaxHighlightingForCurrentDocument(force: true);
				}
				else
				{
					SyncEditorFromDocument();
					ApplySyntaxHighlightingForCurrentDocument(force: true);
				}
				if (ViewModel.SelectedDocumentIsModelDiagram)
				{
					RequestModelDiagramRender();
				}
				if (ViewModel.SelectedDocumentSelectedWorkspaceTabIsResult)
				{
					RequestResultGridRender();
				}
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				AppendUiLog($"OpenedError: {ex2}");
				throw;
			}
		};
		base.Activated += MainWindow_Activated;
	}
	private void MainWindow_DataContextChanged(object? sender, EventArgs e)
	{
		AttachViewModel(TryGetViewModel());
	}
	private void AttachViewModel(MainWindowViewModel? viewModel)
	{
		if (_subscribedViewModel != viewModel)
		{
			if (_subscribedViewModel != null)
			{
				_subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
			}
			_subscribedViewModel = viewModel;
			if (_subscribedViewModel != null)
			{
				_subscribedViewModel.PropertyChanged += ViewModel_PropertyChanged;
			}
		}
	}
	private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "SelectedDocumentShouldShowResultWorkspace")
		{
			UpdateResultWorkspaceVisibility();
		}
		if (e.PropertyName == "SelectedDocumentIsModelDiagram")
		{
			_hasLastModelDiagramRenderedViewport = false;
			_lastModelDiagramRenderedDocumentId = string.Empty;
			_lastModelDiagramRenderedVersion = -1;
			_lastModelDiagramRenderedZoom = -1.0;
		}
		bool shouldRenderDiagram;
		switch (e.PropertyName)
		{
		case "SelectedDocumentIsModelDiagram":
		case "SelectedModelDiagramRenderVersion":
		case "SelectedModelDiagramCanvasWidth":
		case "SelectedModelDiagramCanvasHeight":
		case "SelectedModelDiagramZoom":
			shouldRenderDiagram = true;
			break;
		default:
			shouldRenderDiagram = false;
			break;
		}
		if (shouldRenderDiagram)
		{
			RequestModelDiagramRender();
		}
	}
	private void RequestModelDiagramRender()
	{
		if (!_modelDiagramRenderPending)
		{
			_modelDiagramRenderPending = true;
			Dispatcher.UIThread.Post(delegate
			{
				_modelDiagramRenderPending = false;
				RenderModelDiagramCanvas();
			}, DispatcherPriority.Background);
		}
	}
	private void ModelDiagramScrollViewer_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
	{
		MainWindowViewModel? mainWindowViewModel = TryGetViewModel();
		if (mainWindowViewModel != null && mainWindowViewModel.SelectedDocumentIsModelDiagram && (e.Property == ScrollViewer.OffsetProperty || e.Property == Visual.BoundsProperty))
		{
			RequestModelDiagramRender();
		}
	}
	private void ScheduleResultWorkspaceStateSync(bool renderIfNeeded = false)
	{
		if (renderIfNeeded)
		{
			_resultWorkspaceRenderRequested = true;
		}
		if (_resultWorkspaceStateSyncPending)
		{
			return;
		}
		_resultWorkspaceStateSyncPending = true;
		Dispatcher.UIThread.Post(delegate
		{
			_resultWorkspaceStateSyncPending = false;
			MainWindowViewModel? mainWindowViewModel = TryGetViewModel();
			if (mainWindowViewModel == null)
			{
				_resultWorkspaceRenderRequested = false;
				return;
			}
			bool lastResultWorkspaceVisible = _lastResultWorkspaceVisible;
			bool selectedDocumentShouldShowResultWorkspace = mainWindowViewModel.SelectedDocumentShouldShowResultWorkspace;
			UpdateResultWorkspaceVisibility();
			if (selectedDocumentShouldShowResultWorkspace && mainWindowViewModel.SelectedDocumentSelectedWorkspaceTab == null)
			{
				SelectResultWorkspaceTab();
			}
			bool shouldRenderResultWorkspace = _resultWorkspaceRenderRequested || (!lastResultWorkspaceVisible && selectedDocumentShouldShowResultWorkspace);
			if (!shouldRenderResultWorkspace && selectedDocumentShouldShowResultWorkspace && mainWindowViewModel.SelectedDocumentSelectedWorkspaceTabIsResult)
			{
				Grid resultHeaderHost = ResultHeaderHost;
				var headerChildren = resultHeaderHost?.Children;
				if (headerChildren != null && headerChildren.Count == 0)
				{
					StackPanel resultRowsHost = ResultRowsHost;
					var rowChildren = resultRowsHost?.Children;
					shouldRenderResultWorkspace = rowChildren != null && rowChildren.Count == 0;
				}
			}
			_lastResultWorkspaceVisible = selectedDocumentShouldShowResultWorkspace;
			_resultWorkspaceRenderRequested = false;
			if (shouldRenderResultWorkspace && selectedDocumentShouldShowResultWorkspace && CanRenderSelectedResultGrid(mainWindowViewModel))
			{
				RequestResultGridRender();
			}
		}, DispatcherPriority.Background);
	}
	private void RequestResultGridRender(int delayMilliseconds = 0)
	{
		int requestVersion = ++_scheduledResultRenderVersion;
		Task.Run(async delegate
		{
			if (delayMilliseconds > 0)
			{
				await Task.Delay(delayMilliseconds);
			}
			await Dispatcher.UIThread.InvokeAsync(delegate
			{
				if (requestVersion == _scheduledResultRenderVersion)
				{
					MainWindowViewModel? mainWindowViewModel = TryGetViewModel();
					if (mainWindowViewModel != null && CanRenderSelectedResultGrid(mainWindowViewModel))
					{
						_ = RenderSelectedResultGridAsync();
					}
				}
			}, DispatcherPriority.Background);
		});
	}
	private static bool CanRenderSelectedResultGrid(MainWindowViewModel viewModel)
	{
		return viewModel.SelectedDocumentSelectedWorkspaceTabIsResult &&
		       !viewModel.SelectedDocumentIsExecuting &&
		       !viewModel.SelectedDocumentIsRenderingResults;
	}
	private void InitializeEditor()
	{
		_textMateInstallation = EditorTextBox.InstallTextMate(_registryOptions);
		EditorTextBox.TextArea.AddHandler(InputElement.PointerPressedEvent, EditorSurface_PointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
		EditorTextBox.TextArea.TextView.AddHandler(InputElement.PointerPressedEvent, EditorSurface_PointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
	}
	private void EditorSurface_PointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt))
		{
			HideCompletionPopup();
		}
	}
	public void EnsureVisibleOnStartup()
	{
		base.ShowInTaskbar = true;
		base.WindowState = WindowState.Normal;
		base.Topmost = true;
		Activate();
		Dispatcher.UIThread.Post(delegate
		{
			base.Topmost = false;
			Activate();
			AppendUiLog($"EnsureVisible: {DateTime.Now:O}; Visible={base.IsVisible}; WindowState={base.WindowState}");
		}, DispatcherPriority.Background);
	}
	private MainWindowViewModel? TryGetViewModel()
	{
		return base.DataContext as MainWindowViewModel;
	}
	private async void NewQueryButton_Click(object? sender, RoutedEventArgs e)
	{
		await ViewModel.CreateDocumentAsync();
		UpdateResultWorkspaceVisibility();
		FocusEditor();
	}
	private void OpenQueryHistoryButton_Click(object? sender, RoutedEventArgs e)
	{
		ViewModel.OpenQueryHistoryDocument();
		UpdateResultWorkspaceVisibility();
	}
	private async void OpenQueryFromHistoryButton_Click(object? sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: QueryHistoryEntry entry })
		{
			return;
		}

		ViewModel.OpenQueryFromHistory(entry);
		await ViewModel.EnsureSelectedDocumentContextReadyAsync();
		UpdateResultWorkspaceVisibility();
		FocusEditor();
	}
	private async void OpenCommentMaintenanceButton_Click(object? sender, RoutedEventArgs e)
	{
		if (ViewModel.OpenCommentMaintenanceDocument() == null)
		{
			await MessageBoxAsync(ViewModel.UiText.CommentMaintenance, ViewModel.UiText.OpenWorkbenchRequiresOpenedSchema);
			return;
		}
		try
		{
			UpdateResultWorkspaceVisibility();
			if (!ViewModel.SelectedCommentWorkspaceIsLoaded)
			{
				await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
				await ViewModel.LoadSelectedCommentWorkspaceAsync();
			}
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.CommentMaintenance, ex.Message);
		}
	}
	private async void OpenModelDiagramButton_Click(object? sender, RoutedEventArgs e)
	{
		if (ViewModel.OpenModelDiagramDocument() == null)
		{
			await MessageBoxAsync(ViewModel.UiText.DataModel, ViewModel.UiText.OpenWorkbenchRequiresOpenedSchema);
			return;
		}
		try
		{
			UpdateResultWorkspaceVisibility();
			if (!ViewModel.SelectedModelDiagramIsLoaded)
			{
				await ViewModel.LoadSelectedModelDiagramAsync();
			}
			RequestModelDiagramRender();
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.DataModel, ex.Message);
		}
	}
	private async void OpenSchemaModelDiagramMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		if (node == null)
		{
			return;
		}
		try
		{
			ViewModel.SetExplorerFocus(node);
			if (_explorerActionController.OpenSchemaModelDiagram(ViewModel, node) == null)
			{
				return;
			}
			UpdateResultWorkspaceVisibility();
			await ViewModel.LoadSelectedModelDiagramAsync();
			RequestModelDiagramRender();
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.DataModel, ex.Message);
		}
	}

	private async void OpenTableModelDiagramMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		if (node == null)
		{
			return;
		}
		try
		{
			ViewModel.SetExplorerFocus(node);
			if (_explorerActionController.OpenTableModelDiagram(ViewModel, node) == null)
			{
				return;
			}
			UpdateResultWorkspaceVisibility();
			await ViewModel.LoadSelectedModelDiagramAsync();
			RequestModelDiagramRender();
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.DataModel, ex.Message);
		}
	}

	private async void LoadModelDiagramButton_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			await ViewModel.LoadSelectedModelDiagramAsync();
			RequestModelDiagramRender();
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.DataModel, ex.Message);
		}
	}

	private async void ExportModelDiagramRelationsButton_Click(object? sender, RoutedEventArgs e)
	{
		TopLevel? topLevel = TopLevel.GetTopLevel(this);
		if (topLevel?.StorageProvider == null)
		{
			return;
		}
		string? path = (await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = ViewModel.UiText.ExportRelationsTitle,
			SuggestedFileName = $"model-relations-{DateTime.Now:yyyyMMddHHmmss}.csv",
			FileTypeChoices =
			[
				new FilePickerFileType(ViewModel.UiText.CsvFileType)
				{
					Patterns = ["*.csv"]
				}
			],
			DefaultExtension = "csv"
		}))?.TryGetLocalPath();
		if (!string.IsNullOrWhiteSpace(path))
		{
			try
			{
				await ViewModel.ExportSelectedModelDiagramRelationsAsync(path);
				await MessageBoxAsync(ViewModel.UiText.ExportRelationsTitle, ViewModel.UiText.ExportRelationsSuccess);
			}
			catch (Exception ex)
			{
				await MessageBoxAsync(ViewModel.UiText.ExportRelationsTitle, ex.Message);
			}
		}
	}

	private void ReloadModelDiagramLayoutButton_Click(object? sender, RoutedEventArgs e)
	{
		ViewModel.ReloadSelectedModelDiagramLayout();
		RequestModelDiagramRender();
	}

	private void ExpandModelDiagramNeighborhoodButton_Click(object? sender, RoutedEventArgs e)
	{
		ViewModel.ExpandSelectedModelDiagramNeighborhood();
		RequestModelDiagramRender();
	}

	private void ZoomInModelDiagramButton_Click(object? sender, RoutedEventArgs e)
	{
		ViewModel.ZoomInSelectedModelDiagram();
		RequestModelDiagramRender();
	}

	private void ZoomOutModelDiagramButton_Click(object? sender, RoutedEventArgs e)
	{
		ViewModel.ZoomOutSelectedModelDiagram();
		RequestModelDiagramRender();
	}

	private void ResetModelDiagramZoomButton_Click(object? sender, RoutedEventArgs e)
	{
		ViewModel.ResetSelectedModelDiagramZoom();
		RequestModelDiagramRender();
	}

	private void ModelDiagramScrollViewer_PointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (!(sender is ScrollViewer scrollViewer))
		{
			return;
		}
		if (!e.GetCurrentPoint(scrollViewer).Properties.IsMiddleButtonPressed)
		{
			if (e.Source == scrollViewer || e.Source == ModelDiagramCanvas)
			{
				MainWindowViewModel? mainWindowViewModel = TryGetViewModel();
				if (mainWindowViewModel != null)
				{
					mainWindowViewModel.ClearSelectedModelDiagramRelation();
					RequestModelDiagramRender();
				}
			}
		}
		else
		{
			_isPanningModelDiagram = true;
			_lastModelDiagramPanPoint = e.GetPosition(scrollViewer);
			_modelDiagramPanStartOffset = scrollViewer.Offset;
			e.Pointer.Capture(scrollViewer);
			e.Handled = true;
		}
	}

	private void ModelDiagramScrollViewer_PointerMoved(object? sender, PointerEventArgs e)
	{
		if (!(sender is ScrollViewer scrollViewer))
		{
			return;
		}
		if (_isDraggingModelDiagramNode)
		{
			Point position = e.GetPosition(scrollViewer);
			Vector vector = position - _lastModelDiagramNodeDragPoint;
			if (Math.Abs(vector.X) > 0.1 || Math.Abs(vector.Y) > 0.1)
			{
				double zoom = Math.Max(0.1, TryGetViewModel()?.SelectedModelDiagramZoom ?? 1.0);
				TryGetViewModel()?.MoveSelectedModelDiagramNode(_draggingModelDiagramNodeTableName, vector.X / zoom, vector.Y / zoom);
				_lastModelDiagramNodeDragPoint = position;
				_didMoveModelDiagramNode = true;
				e.Handled = true;
			}
		}
		else if (_isPanningModelDiagram)
		{
			Point position2 = e.GetPosition(scrollViewer);
			Vector vector2 = position2 - _lastModelDiagramPanPoint;
			Vector offset = new Vector(Math.Max(0.0, _modelDiagramPanStartOffset.X - vector2.X), Math.Max(0.0, _modelDiagramPanStartOffset.Y - vector2.Y));
			scrollViewer.Offset = offset;
			e.Handled = true;
		}
	}

	private void ModelDiagramScrollViewer_PointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		if (sender is ScrollViewer scrollViewer)
		{
			if (_isDraggingModelDiagramNode)
			{
				_isDraggingModelDiagramNode = false;
				_draggingModelDiagramNodeTableName = string.Empty;
				_suppressNextModelDiagramNodeClick = _didMoveModelDiagramNode;
				_didMoveModelDiagramNode = false;
				e.Pointer.Capture(null);
				e.Handled = true;
			}
			else if (_isPanningModelDiagram)
			{
				_isPanningModelDiagram = false;
				e.Pointer.Capture(null);
				_modelDiagramPanStartOffset = scrollViewer.Offset;
				e.Handled = true;
			}
		}
	}

	private async void ExportModelDiagramImageButton_Click(object? sender, RoutedEventArgs e)
	{
		TopLevel? topLevel = TopLevel.GetTopLevel(this);
		if (topLevel?.StorageProvider == null)
		{
			return;
		}
		string? path = (await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = ViewModel.UiText.ExportModelImageTitle,
			SuggestedFileName = $"model-diagram-{DateTime.Now:yyyyMMddHHmmss}.png",
			DefaultExtension = "png",
			FileTypeChoices =
			[
				new FilePickerFileType(ViewModel.UiText.PngFileType)
				{
					Patterns = ["*.png"]
				}
			],
			ShowOverwritePrompt = true
		}))?.TryGetLocalPath();
		if (string.IsNullOrWhiteSpace(path) || ModelDiagramCanvas == null)
		{
			return;
		}
		try
		{
			RenderModelDiagramCanvas(forceFull: true);
			int width = Math.Max(1, (int)Math.Ceiling(ViewModel.SelectedModelDiagramCanvasWidth));
			int height = Math.Max(1, (int)Math.Ceiling(ViewModel.SelectedModelDiagramCanvasHeight));
			RenderTargetBitmap bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96.0, 96.0));
			bitmap.Render(ModelDiagramCanvas);
			await using FileStream stream = File.Create(path);
			bitmap.Save(stream);
			await MessageBoxAsync(ViewModel.UiText.ExportModelImageTitle, ViewModel.UiText.ExportModelImageSuccess);
			RequestModelDiagramRender();
		}
		catch (Exception ex)
		{
			RequestModelDiagramRender();
			await MessageBoxAsync(ViewModel.UiText.ExportModelImageTitle, ex.Message);
		}
	}

	private async void LoadCommentMaintenanceButton_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
			await ViewModel.LoadSelectedCommentWorkspaceAsync();
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.CommentMaintenance, ex.Message);
		}
	}

	private async void ImportCommentMaintenanceCsvButton_Click(object? sender, RoutedEventArgs e)
	{
		TopLevel? topLevel = TopLevel.GetTopLevel(this);
		if (topLevel?.StorageProvider == null)
		{
			return;
		}
		string? path = (await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = ViewModel.UiText.ImportCommentCsvTitle,
			AllowMultiple = false,
			FileTypeFilter =
			[
				new FilePickerFileType(ViewModel.UiText.CsvFileType)
				{
					Patterns = ["*.csv"]
				}
			]
		})).FirstOrDefault()?.TryGetLocalPath();
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		try
		{
			CommentImportResult result = await ViewModel.ImportSelectedCommentWorkspaceCsvAsync(path);
			string summary = string.Format(CultureInfo.CurrentCulture, ViewModel.UiText.ImportCommentCsvSummaryFormat, result.ImportedCount, result.UpdatedCount, result.SkippedCount);
			if (result.Errors.Count > 0)
			{
				summary = summary + Environment.NewLine + Environment.NewLine + ViewModel.UiText.ImportCommentCsvTopErrorsHeader + Environment.NewLine + string.Join(Environment.NewLine, from item in result.Errors.Take(10)
					select string.Format(CultureInfo.CurrentCulture, ViewModel.UiText.ImportErrorLineFormat, item.RowNumber, item.Message));
			}
			await MessageBoxAsync(ViewModel.UiText.ImportCommentCsvTitle, summary);
			if (result.Errors.Count > 0)
			{
				string details = string.Join(Environment.NewLine, result.Errors.Select((CommentImportErrorItem item) => string.Format(CultureInfo.CurrentCulture, ViewModel.UiText.ImportErrorLineFormat, item.RowNumber, item.Message)));
				CommentSqlPreviewWindow window = new CommentSqlPreviewWindow(ViewModel.UiText.ImportFailureDetailsTitle, details, ViewModel.UiText.CopyFailureDetails, ViewModel.UiText.Close);
				await window.ShowDialog(this);
			}
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.ImportCommentCsvTitle, ex.Message);
		}
	}

	private async void ExportCommentMaintenanceCsvButton_Click(object? sender, RoutedEventArgs e)
	{
		TopLevel? topLevel = TopLevel.GetTopLevel(this);
		if (topLevel?.StorageProvider == null)
		{
			return;
		}
		string? path = (await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = ViewModel.UiText.ExportCommentCsvTitle,
			SuggestedFileName = $"comment-maintenance-{DateTime.Now:yyyyMMddHHmmss}.csv",
			FileTypeChoices =
			[
				new FilePickerFileType(ViewModel.UiText.CsvFileType)
				{
					Patterns = ["*.csv"]
				}
			],
			DefaultExtension = "csv"
		}))?.TryGetLocalPath();
		if (!string.IsNullOrWhiteSpace(path))
		{
			try
			{
				await ViewModel.ExportSelectedCommentWorkspaceCsvAsync(path);
				await MessageBoxAsync(ViewModel.UiText.ExportCommentCsvTitle, ViewModel.UiText.CommentCsvExported);
			}
			catch (Exception ex)
			{
				await MessageBoxAsync(ViewModel.UiText.ExportCommentCsvTitle, ex.Message);
			}
		}
	}

	private async void PreviewCommentMaintenanceSqlButton_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			string sql = ViewModel.BuildSelectedCommentWorkspaceSqlPreview();
			if (string.IsNullOrWhiteSpace(sql))
			{
				await MessageBoxAsync(ViewModel.UiText.PreviewCommentSqlTitle, ViewModel.UiText.NoCommentSqlPreview);
				return;
			}
			CommentSqlPreviewWindow window = new CommentSqlPreviewWindow(ViewModel.UiText.CommentUpdateSqlPreviewTitle, sql, ViewModel.UiText.CopySql, ViewModel.UiText.Close);
			await window.ShowDialog(this);
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.PreviewCommentSqlTitle, ex.Message);
		}
	}

	private async void ApplyCommentMaintenanceButton_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			int affected = await ViewModel.ApplySelectedCommentWorkspaceAsync();
			await MessageBoxAsync(ViewModel.UiText.ApplyUpdates, string.Format(CultureInfo.CurrentCulture, ViewModel.UiText.CommentUpdateCompletedFormat, affected));
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.ApplyUpdates, ex.Message);
		}
	}

	private void CommentTableNameCell_PointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (TryGetViewModel() != null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && (sender as Control)?.DataContext is CommentMaintenanceTableItem selectedCommentWorkspaceSelectedTable)
		{
			ViewModel.SelectedCommentWorkspaceSelectedTable = selectedCommentWorkspaceSelectedTable;
		}
	}

	private void ClearCommentMaintenanceFiltersButton_Click(object? sender, RoutedEventArgs e)
	{
		ViewModel.ClearSelectedCommentWorkspaceFilters();
	}

	private async void OpenObjectEditorMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		if (node == null)
		{
			return;
		}
		try
		{
			if (_explorerActionController.OpenObjectEditor(ViewModel, node) == null)
			{
				return;
			}
			UpdateResultWorkspaceVisibility();
			await ViewModel.LoadSelectedObjectEditorAsync();
			SyncEditorFromDocument();
			ApplySyntaxHighlightingForCurrentDocument(force: true);
			FocusEditor();
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.EditObject, ex.Message);
		}
	}

	private void ModelDiagramNode_Click(object? sender, RoutedEventArgs e)
	{
		if (_suppressNextModelDiagramNodeClick)
		{
			_suppressNextModelDiagramNodeClick = false;
			return;
		}
		if ((sender as Control)?.Tag is ModelDiagramNodeState nodeState)
		{
			ViewModel.SelectedModelDiagramSelectedTable = ViewModel.SelectedModelDiagramTables.FirstOrDefault((ModelTableNode item) => string.Equals(item.TableName, nodeState.TableName, StringComparison.OrdinalIgnoreCase));
			RequestModelDiagramRender();
		}
	}

	private void ModelDiagramLightNode_Tapped(object? sender, TappedEventArgs e)
	{
		if (_suppressNextModelDiagramNodeClick)
		{
			_suppressNextModelDiagramNodeClick = false;
			return;
		}
		if ((sender as Control)?.Tag is ModelDiagramNodeState nodeState)
		{
			ViewModel.SelectedModelDiagramSelectedTable = ViewModel.SelectedModelDiagramTables.FirstOrDefault((ModelTableNode item) => string.Equals(item.TableName, nodeState.TableName, StringComparison.OrdinalIgnoreCase));
			RequestModelDiagramRender();
		}
	}

	private void ModelDiagramNode_PointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if ((sender as Control)?.Tag is ModelDiagramNodeState modelDiagramNodeState && ModelDiagramScrollViewer != null && e.GetCurrentPoint(ModelDiagramScrollViewer).Properties.IsLeftButtonPressed)
		{
			_isDraggingModelDiagramNode = true;
			_draggingModelDiagramNodeTableName = modelDiagramNodeState.TableName;
			_lastModelDiagramNodeDragPoint = e.GetPosition(ModelDiagramScrollViewer);
			_didMoveModelDiagramNode = false;
			e.Pointer.Capture(ModelDiagramScrollViewer);
		}
	}

	private async void ModelDiagramNode_DoubleTapped(object? sender, RoutedEventArgs e)
	{
		if ((sender as Control)?.Tag is ModelDiagramNodeState nodeState && ViewModel.SelectedModelDiagram != null)
		{
			await TableDesignCoordinator.OpenAsync(this, BuildModelDiagramObjectNode(nodeState));
		}
	}

	private void RenderModelDiagramCanvas(bool forceFull = false)
	{
		if (ModelDiagramCanvas == null)
		{
			return;
		}
		MainWindowViewModel? viewModel = TryGetViewModel();
		if (viewModel == null || !viewModel.SelectedDocumentIsModelDiagram)
		{
			ModelDiagramCanvas.Children.Clear();
			return;
		}
		double zoom = viewModel.SelectedModelDiagramZoom;
		Rect actualViewport = BuildModelDiagramActualViewport(viewModel, forceFull);
		if (CanReusePreviousModelDiagramRender(viewModel, actualViewport, zoom, forceFull))
		{
			return;
		}
		Rect viewport = BuildModelDiagramRenderViewport(actualViewport, forceFull);
		IReadOnlyList<ModelDiagramNodeState> visibleNodes = (forceFull ? viewModel.SelectedModelDiagramVisibleNodes.ToArray() : viewModel.SelectedModelDiagramVisibleNodes.Where((ModelDiagramNodeState node) => IntersectsViewport(node, viewport, zoom)).ToArray());
		HashSet<string> renderedNodeNames = visibleNodes.Select((ModelDiagramNodeState node) => node.TableName).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		IReadOnlyList<ModelDiagramRelationState> visibleRelations = (forceFull ? viewModel.SelectedModelDiagramVisibleRelations.ToArray() : viewModel.SelectedModelDiagramVisibleRelations.Where((ModelDiagramRelationState relation) => renderedNodeNames.Contains(relation.FromTable) || renderedNodeNames.Contains(relation.ToTable) || IntersectsViewport(relation, viewport, zoom)).ToArray());
		ModelDiagramCanvas.Children.Clear();
		bool batchPlainRelations = !forceFull && (visibleRelations.Count > 60 || zoom <= 0.9 || (zoom <= 1.0 && visibleNodes.Count > 120));
		bool suppressRelationHitTargets = !forceFull && (zoom <= 0.72 || visibleRelations.Count > 180 || visibleNodes.Count > 180);
		StringBuilder? batchedRelationPathBuilder = batchPlainRelations ? new StringBuilder(visibleRelations.Count * 24) : null;
		foreach (ModelDiagramRelationState relation in visibleRelations)
		{
			if (string.IsNullOrWhiteSpace(relation.PathData))
			{
				continue;
			}
			if (batchPlainRelations && !relation.IsSelected && (!relation.IsHighlighted || suppressRelationHitTargets))
			{
				batchedRelationPathBuilder!.Append("M ").Append(relation.StartX * zoom).Append(',')
					.Append(relation.StartY * zoom)
					.Append(" L ")
					.Append(relation.EndX * zoom)
					.Append(',')
					.Append(relation.EndY * zoom)
					.Append(' ');
				continue;
			}
			string relationPath = $"M {relation.StartX * zoom},{relation.StartY * zoom} L {relation.EndX * zoom},{relation.EndY * zoom}";
			Geometry relationGeometry = Geometry.Parse(relationPath);
			double visibleThickness = ResolveModelDiagramRelationVisibleThickness(relation, zoom);
			bool canHitTestRelation = !suppressRelationHitTargets || relation.IsSelected;
			bool needsTransparentHitPath = canHitTestRelation && (zoom <= 1.05 || relation.IsSelected || relation.IsHighlighted);
			Avalonia.Controls.Shapes.Path relationShape = new Avalonia.Controls.Shapes.Path
			{
				Data = relationGeometry,
				Stroke = new SolidColorBrush(Color.Parse(relation.Stroke)),
				StrokeThickness = visibleThickness,
				StrokeLineCap = PenLineCap.Round,
				IsHitTestVisible = (canHitTestRelation && !needsTransparentHitPath),
				Tag = relation,
				ContextMenu = ((canHitTestRelation && !needsTransparentHitPath) ? BuildModelDiagramRelationContextMenu(relation) : null)
			};
			if (relationShape.IsHitTestVisible)
			{
				relationShape.PointerPressed += ModelDiagramRelation_PointerPressed;
			}
			ModelDiagramCanvas.Children.Add(relationShape);
			if (needsTransparentHitPath)
			{
				Avalonia.Controls.Shapes.Path relationHitShape = new Avalonia.Controls.Shapes.Path
				{
					Data = relationGeometry,
					Stroke = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
					StrokeThickness = ResolveModelDiagramRelationHitThickness(visibleThickness, zoom, relation),
					StrokeLineCap = PenLineCap.Round,
					IsHitTestVisible = true,
					Tag = relation,
					ContextMenu = BuildModelDiagramRelationContextMenu(relation)
				};
				relationHitShape.PointerPressed += ModelDiagramRelation_PointerPressed;
				ModelDiagramCanvas.Children.Add(relationHitShape);
			}
		}
		if (batchedRelationPathBuilder != null && batchedRelationPathBuilder.Length > 0)
		{
			ModelDiagramCanvas.Children.Add(new Avalonia.Controls.Shapes.Path
			{
				Data = Geometry.Parse(batchedRelationPathBuilder.ToString()),
				Stroke = new SolidColorBrush(Color.Parse("#94A3B8")),
				StrokeThickness = Math.Max(1.0, 1.2 * Math.Min(zoom, 1.2)),
				IsHitTestVisible = false
			});
		}
		bool useCompactNodeRendering = !forceFull && (visibleNodes.Count > 140 || (zoom <= 0.9 && visibleNodes.Count > 90));
		bool suppressNodePointerActions = !forceFull && zoom <= 0.72 && visibleNodes.Count > 150;
		foreach (ModelDiagramNodeState node in visibleNodes)
		{
			bool isImportantNode = node.IsSelected || node.IsHighlighted || node.IsSearchMatched;
			bool renderAsLightweightBorder = useCompactNodeRendering && zoom <= 0.86 && !isImportantNode;
			bool allowNodeContextMenu = !useCompactNodeRendering || isImportantNode;
			bool allowNodePointerActions = !suppressNodePointerActions || isImportantNode;
			Border border = BuildModelDiagramNodeBody(viewModel, node, allowNodeContextMenu);
			Control control;
			if (renderAsLightweightBorder)
			{
				Border border2 = new Border
				{
					Tag = node,
					Background = Brushes.Transparent,
					Child = border,
					RenderTransform = new ScaleTransform(zoom, zoom),
					RenderTransformOrigin = new RelativePoint(0.0, 0.0, RelativeUnit.Relative),
					ContextMenu = (allowNodeContextMenu ? BuildModelDiagramNodeContextMenu(node) : null)
				};
				border2.Tapped += ModelDiagramLightNode_Tapped;
				if (allowNodePointerActions)
				{
					border2.DoubleTapped += ModelDiagramNode_DoubleTapped;
				}
				if (allowNodePointerActions)
				{
					border2.PointerPressed += ModelDiagramNode_PointerPressed;
				}
				control = border2;
			}
			else
			{
				Button button = new Button
				{
					Tag = node,
					Content = border,
					Background = Brushes.Transparent,
					BorderThickness = new Thickness(0.0),
					Padding = new Thickness(0.0),
					HorizontalContentAlignment = HorizontalAlignment.Stretch,
					VerticalContentAlignment = VerticalAlignment.Stretch,
					RenderTransform = new ScaleTransform(zoom, zoom),
					RenderTransformOrigin = new RelativePoint(0.0, 0.0, RelativeUnit.Relative),
					ContextMenu = (allowNodeContextMenu ? BuildModelDiagramNodeContextMenu(node) : null)
				};
				button.Click += ModelDiagramNode_Click;
				if (allowNodePointerActions)
				{
					button.DoubleTapped += ModelDiagramNode_DoubleTapped;
				}
				if (allowNodePointerActions)
				{
					button.PointerPressed += ModelDiagramNode_PointerPressed;
				}
				control = button;
			}
			Canvas.SetLeft(control, node.X * zoom);
			Canvas.SetTop(control, node.Y * zoom);
			ModelDiagramCanvas.Children.Add(control);
		}
		if (!forceFull && TryCenterSelectedModelDiagramTable(viewModel, zoom))
		{
			ResetModelDiagramRenderCache();
			RequestModelDiagramRender();
		}
		else
		{
			RememberModelDiagramRender(viewModel, viewport, zoom, forceFull);
		}
	}

	private Rect BuildModelDiagramActualViewport(MainWindowViewModel viewModel, bool forceFull)
	{
		if (forceFull || ModelDiagramScrollViewer == null || ModelDiagramScrollViewer.Bounds.Width <= 1.0 || ModelDiagramScrollViewer.Bounds.Height <= 1.0)
		{
			return new Rect(0.0, 0.0, viewModel.SelectedModelDiagramCanvasWidth, viewModel.SelectedModelDiagramCanvasHeight);
		}
		Vector offset = ModelDiagramScrollViewer.Offset;
		return new Rect(offset.X, offset.Y, Math.Max(1.0, ModelDiagramScrollViewer.Bounds.Width), Math.Max(1.0, ModelDiagramScrollViewer.Bounds.Height));
	}

	private static Rect BuildModelDiagramRenderViewport(Rect actualViewport, bool forceFull)
	{
		return forceFull ? actualViewport : InflateRect(actualViewport, 180.0);
	}

	private static double ResolveModelDiagramRelationVisibleThickness(ModelDiagramRelationState relation, double zoom)
	{
		return Math.Max(1.0, relation.Thickness * Math.Min(zoom, 1.4));
	}

	private static double ResolveModelDiagramRelationHitThickness(double visibleThickness, double zoom, ModelDiagramRelationState relation)
	{
		double val = ((!relation.IsSelected) ? ((!relation.IsHighlighted) ? ((zoom <= 0.72) ? 14.0 : ((zoom <= 0.9) ? 10.0 : 6.0)) : ((zoom <= 0.72) ? 16.0 : ((zoom <= 0.9) ? 12.0 : 8.0))) : ((zoom <= 0.72) ? 18.0 : ((zoom <= 0.9) ? 14.0 : 10.0)));
		return Math.Max(val, visibleThickness + 4.0);
	}

	private bool CanReusePreviousModelDiagramRender(MainWindowViewModel viewModel, Rect actualViewport, double zoom, bool forceFull)
	{
		if (forceFull || !_hasLastModelDiagramRenderedViewport || viewModel.SelectedDocument == null)
		{
			return false;
		}
		return string.Equals(_lastModelDiagramRenderedDocumentId, viewModel.SelectedDocument.Id, StringComparison.OrdinalIgnoreCase) && _lastModelDiagramRenderedVersion == viewModel.SelectedModelDiagramRenderVersion && Math.Abs(_lastModelDiagramRenderedZoom - zoom) < 0.001 && ContainsRect(_lastModelDiagramRenderedViewport, actualViewport);
	}

	private void RememberModelDiagramRender(MainWindowViewModel viewModel, Rect renderedViewport, double zoom, bool forceFull)
	{
		if (forceFull || viewModel.SelectedDocument == null)
		{
			ResetModelDiagramRenderCache();
			return;
		}
		_hasLastModelDiagramRenderedViewport = true;
		_lastModelDiagramRenderedViewport = renderedViewport;
		_lastModelDiagramRenderedDocumentId = viewModel.SelectedDocument.Id;
		_lastModelDiagramRenderedVersion = viewModel.SelectedModelDiagramRenderVersion;
		_lastModelDiagramRenderedZoom = zoom;
	}

	private bool TryCenterSelectedModelDiagramTable(MainWindowViewModel viewModel, double zoom)
	{
		if (ModelDiagramScrollViewer == null || viewModel.SelectedModelDiagram == null)
		{
			return false;
		}
		string tableName = viewModel.SelectedModelDiagram.ConsumePendingViewportCenterTableName();
		if (string.IsNullOrWhiteSpace(tableName) || string.Equals(_lastModelDiagramAutoCenteredTableName, tableName, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		ModelDiagramNodeState? modelDiagramNodeState = viewModel.SelectedModelDiagramVisibleNodes.FirstOrDefault((ModelDiagramNodeState item) => string.Equals(item.TableName, tableName, StringComparison.OrdinalIgnoreCase));
		if (modelDiagramNodeState == null || ModelDiagramScrollViewer.Bounds.Width <= 1.0 || ModelDiagramScrollViewer.Bounds.Height <= 1.0)
		{
			return false;
		}
		double centerX = (modelDiagramNodeState.X + modelDiagramNodeState.Width / 2.0) * zoom;
		double centerY = (modelDiagramNodeState.Y + modelDiagramNodeState.Height / 2.0) * zoom;
		double viewportWidth = Math.Max(1.0, ModelDiagramScrollViewer.Bounds.Width);
		double viewportHeight = Math.Max(1.0, ModelDiagramScrollViewer.Bounds.Height);
		double targetOffsetX = Math.Max(0.0, centerX - viewportWidth / 2.0);
		double targetOffsetY = Math.Max(0.0, centerY - viewportHeight / 2.0);
		Vector offset = ModelDiagramScrollViewer.Offset;
		if (Math.Abs(offset.X - targetOffsetX) < 2.0 && Math.Abs(offset.Y - targetOffsetY) < 2.0)
		{
			_lastModelDiagramAutoCenteredTableName = tableName;
			return false;
		}
		_lastModelDiagramAutoCenteredTableName = tableName;
		ModelDiagramScrollViewer.Offset = new Vector(targetOffsetX, targetOffsetY);
		return true;
	}

	private void ResetModelDiagramRenderCache()
	{
		_hasLastModelDiagramRenderedViewport = false;
		_lastModelDiagramRenderedDocumentId = string.Empty;
		_lastModelDiagramRenderedVersion = -1;
		_lastModelDiagramRenderedZoom = -1.0;
		_lastModelDiagramAutoCenteredTableName = string.Empty;
	}

	private static bool ContainsRect(Rect outer, Rect inner)
	{
		return inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
	}

	private static bool IntersectsViewport(ModelDiagramNodeState node, Rect viewport, double zoom)
	{
		Rect rect = new Rect(node.X * zoom, node.Y * zoom, Math.Max(1.0, node.Width * zoom), Math.Max(1.0, node.Height * zoom));
		return viewport.Intersects(rect);
	}

	private static bool IntersectsViewport(ModelDiagramRelationState relation, Rect viewport, double zoom)
	{
		double left = Math.Min(relation.StartX, relation.EndX) * zoom;
		double top = Math.Min(relation.StartY, relation.EndY) * zoom;
		double right = Math.Max(relation.StartX, relation.EndX) * zoom;
		double bottom = Math.Max(relation.StartY, relation.EndY) * zoom;
		double margin = ((zoom <= 0.72) ? 12.0 : ((zoom <= 0.9) ? 20.0 : 32.0));
		Rect rect = InflateRect(new Rect(left, top, Math.Max(1.0, right - left), Math.Max(1.0, bottom - top)), margin);
		return viewport.Intersects(rect);
	}

	private static Rect InflateRect(Rect rect, double margin)
	{
		return new Rect(rect.X - margin, rect.Y - margin, rect.Width + margin * 2.0, rect.Height + margin * 2.0);
	}

	private void ModelDiagramRelation_PointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if ((sender as Control)?.Tag is not ModelDiagramRelationState relationState)
		{
			return;
		}
		MainWindowViewModel? mainWindowViewModel = TryGetViewModel();
		if (mainWindowViewModel != null)
		{
			mainWindowViewModel.SelectedModelDiagramSelectedTable = mainWindowViewModel.SelectedModelDiagramTables.FirstOrDefault((ModelTableNode item) => string.Equals(item.TableName, relationState.ToTable, StringComparison.OrdinalIgnoreCase)) ?? mainWindowViewModel.SelectedModelDiagramTables.FirstOrDefault((ModelTableNode item) => string.Equals(item.TableName, relationState.FromTable, StringComparison.OrdinalIgnoreCase));
			mainWindowViewModel.SelectModelDiagramRelation(relationState);
			RequestModelDiagramRender();
			e.Handled = true;
		}
	}

	private ContextMenu BuildModelDiagramRelationContextMenu(ModelDiagramRelationState relation)
	{
		MenuItem menuItem = new MenuItem
		{
			Header = ViewModel.UiText.OpenParentTableDesign,
			Tag = relation.FromTable
		};
		menuItem.Click += OpenModelDiagramRelationTableDesignMenuItem_Click;
		MenuItem menuItem2 = new MenuItem
		{
			Header = ViewModel.UiText.OpenChildTableDesign,
			Tag = relation.ToTable
		};
		menuItem2.Click += OpenModelDiagramRelationTableDesignMenuItem_Click;
		MenuItem menuItem3 = new MenuItem
		{
			Header = ViewModel.UiText.LocateParentTableInExplorer,
			Tag = relation.FromTable
		};
		menuItem3.Click += LocateModelDiagramRelationTableInExplorerMenuItem_Click;
		MenuItem menuItem4 = new MenuItem
		{
			Header = ViewModel.UiText.LocateChildTableInExplorer,
			Tag = relation.ToTable
		};
		menuItem4.Click += LocateModelDiagramRelationTableInExplorerMenuItem_Click;
		MenuItem menuItem5 = new MenuItem
		{
			Header = ViewModel.UiText.GenerateJoinQuery,
			Tag = relation
		};
		menuItem5.Click += GenerateModelDiagramRelationQueryMenuItem_Click;
		ContextMenu contextMenu = new ContextMenu();
		contextMenu.Items.Add(menuItem);
		contextMenu.Items.Add(menuItem2);
		contextMenu.Items.Add(new Separator());
		contextMenu.Items.Add(menuItem3);
		contextMenu.Items.Add(menuItem4);
		contextMenu.Items.Add(new Separator());
		contextMenu.Items.Add(menuItem5);
		return contextMenu;
	}

	private ContextMenu BuildModelDiagramNodeContextMenu(ModelDiagramNodeState node)
	{
		MenuItem menuItem = new MenuItem
		{
			Header = ViewModel.UiText.OpenTableDesign,
			Tag = node
		};
		menuItem.Click += OpenModelDiagramNodeTableDesignMenuItem_Click;
		MenuItem menuItem2 = new MenuItem
		{
			Header = ViewModel.UiText.LocateInExplorer,
			Tag = node
		};
		menuItem2.Click += LocateModelDiagramNodeInExplorerMenuItem_Click;
		MenuItem menuItem3 = new MenuItem
		{
			Header = ViewModel.UiText.GenerateQuery,
			Tag = node
		};
		menuItem3.Click += GenerateModelDiagramNodeQueryMenuItem_Click;
		ContextMenu contextMenu = new ContextMenu();
		contextMenu.Items.Add(menuItem);
		contextMenu.Items.Add(menuItem2);
		contextMenu.Items.Add(menuItem3);
		return contextMenu;
	}

	private async void OpenModelDiagramNodeTableDesignMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		if ((sender as MenuItem)?.Tag is ModelDiagramNodeState nodeState)
		{
			await TableDesignCoordinator.OpenAsync(this, BuildModelDiagramObjectNode(nodeState));
		}
	}

	private void GenerateModelDiagramNodeQueryMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		if ((sender as MenuItem)?.Tag is ModelDiagramNodeState nodeState)
		{
			ViewModel.OpenQueryForObject(BuildModelDiagramObjectNode(nodeState));
			FocusEditor();
		}
	}

	private async void OpenModelDiagramRelationTableDesignMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		string? tableName = (sender as MenuItem)?.Tag as string;
		if (!string.IsNullOrWhiteSpace(tableName))
		{
			await TableDesignCoordinator.OpenAsync(this, BuildModelDiagramObjectNode(tableName));
		}
	}

	private void GenerateModelDiagramRelationQueryMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		if ((sender as MenuItem)?.Tag is ModelDiagramRelationState relation)
		{
			ViewModel.OpenQueryForModelDiagramRelation(relation);
			FocusEditor();
		}
	}

	private async void LocateModelDiagramRelationTableInExplorerMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		string? tableName = (sender as MenuItem)?.Tag as string;
		if (!string.IsNullOrWhiteSpace(tableName))
		{
			FindTableResult result = await _findTableController.FindAsync(ViewModel, tableName);
			if (!result.Success || result.Match == null)
			{
				await MessageBoxAsync(result.Title, result.Message);
			}
			else
			{
				await RevealExplorerNodeAsync(result.Match);
			}
		}
	}

	private async void LocateModelDiagramNodeInExplorerMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		if ((sender as MenuItem)?.Tag is ModelDiagramNodeState nodeState)
		{
			FindTableResult result = await _findTableController.FindAsync(ViewModel, nodeState.TableName);
			if (!result.Success || result.Match == null)
			{
				await MessageBoxAsync(result.Title, result.Message);
			}
			else
			{
				await RevealExplorerNodeAsync(result.Match);
			}
		}
	}

	private ObjectNode BuildModelDiagramObjectNode(ModelDiagramNodeState nodeState)
	{
		return BuildModelDiagramObjectNode(nodeState.TableName, nodeState.DisplayName);
	}

	private ObjectNode BuildModelDiagramObjectNode(string tableName, string? displayName = null)
	{
		ModelTableNode? modelTableNode = ViewModel.SelectedModelDiagram?.Tables.FirstOrDefault((ModelTableNode item) => string.Equals(item.TableName, tableName, StringComparison.OrdinalIgnoreCase));
		return new ObjectNode
		{
			Name = tableName,
			DisplayName = ((!string.IsNullOrWhiteSpace(displayName)) ? displayName : (modelTableNode?.DisplayName ?? tableName)),
			SchemaName = (ViewModel.SelectedModelDiagram?.SchemaName ?? string.Empty),
			Type = "table"
		};
	}

	private static Border BuildModelDiagramNodeBody(MainWindowViewModel viewModel, ModelDiagramNodeState node, bool includeToolTip)
	{
		bool showTitleOnly = viewModel.SelectedModelDiagramZoom < 0.72 || (viewModel.SelectedModelDiagramVisibleNodes.Count > 220 && !node.IsSelected && !node.IsHighlighted);
		bool hideDetails = viewModel.SelectedModelDiagramZoom < 0.9 || (viewModel.SelectedModelDiagramVisibleNodes.Count > 100 && !node.IsSelected && !node.IsHighlighted);
		StackPanel stackPanel = new StackPanel
		{
			Spacing = (showTitleOnly ? 1 : (hideDetails ? 2 : 4))
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = node.TableName,
			FontWeight = (node.IsSearchMatched ? FontWeight.Bold : FontWeight.DemiBold),
			FontSize = (showTitleOnly ? 11 : 12),
			Foreground = new SolidColorBrush(Color.Parse(node.TitleForeground)),
			TextTrimming = TextTrimming.CharacterEllipsis
		});
		if (!showTitleOnly && !hideDetails && !string.IsNullOrWhiteSpace(node.CommentText))
		{
			stackPanel.Children.Add(new TextBlock
			{
				Text = node.CommentText,
				FontSize = 11.0,
				Foreground = new SolidColorBrush(Color.Parse("#64748B")),
				TextWrapping = TextWrapping.Wrap,
				MaxHeight = 34.0
			});
		}
		if (!showTitleOnly && !hideDetails && viewModel.SelectedModelDiagramShowColumns && node.PreviewColumnLines.Count > 0)
		{
			foreach (string previewColumnLine in node.PreviewColumnLines)
			{
				stackPanel.Children.Add(new TextBlock
				{
					Text = previewColumnLine,
					FontSize = 11.0,
					Foreground = new SolidColorBrush(Color.Parse("#334155")),
					TextTrimming = TextTrimming.CharacterEllipsis
				});
			}
		}
		Border border = new Border
		{
			Width = node.Width,
			MinHeight = node.Height,
			Background = new SolidColorBrush(Color.Parse(node.Background)),
			BorderBrush = new SolidColorBrush(Color.Parse(node.BorderBrush)),
			BorderThickness = new Thickness(1.4),
			CornerRadius = new CornerRadius(8.0),
			Padding = (showTitleOnly ? new Thickness(8.0, 6.0) : new Thickness(10.0, 8.0)),
			Child = stackPanel
		};
		if (includeToolTip)
		{
			ToolTip.SetTip(border, BuildModelDiagramNodeToolTip(node));
		}
		return border;
	}

	private static object BuildModelDiagramNodeToolTip(ModelDiagramNodeState node)
	{
		List<string> lines = new() { node.TableName };
		if (!string.IsNullOrWhiteSpace(node.CommentText))
		{
			lines.Add(node.CommentText);
		}
		foreach (string item in node.PreviewColumnLines.Take(6))
		{
			lines.Add(item);
		}
		return string.Join(Environment.NewLine, lines);
	}

	private async void RefreshObjectEditorButton_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			await ViewModel.LoadSelectedObjectEditorAsync();
			SyncEditorFromDocument();
			ApplySyntaxHighlightingForCurrentDocument(force: true);
			FocusEditor();
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.RefreshObjectDefinition, ex.Message);
		}
	}

	private async void PreviewObjectEditorSqlButton_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			await ViewModel.PreviewSelectedObjectEditorSqlAsync();
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.PreviewSql, ex.Message);
		}
	}

	private async void ValidateObjectEditorButton_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			await ViewModel.ValidateSelectedObjectEditorAsync();
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.CompileValidate, ex.Message);
		}
	}

	private async void SaveObjectEditorButton_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			await ViewModel.SaveSelectedObjectEditorAsync();
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.SaveObjectDefinition, ex.Message);
		}
	}

	private void DiscardObjectEditorChangesButton_Click(object? sender, RoutedEventArgs e)
	{
		ViewModel.DiscardSelectedObjectEditorChanges();
		SyncEditorFromDocument();
		ApplySyntaxHighlightingForCurrentDocument(force: true);
		FocusEditor();
	}

	private async void OpenFileMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		await OpenDocumentFileAsync();
	}

	private void TopMenuButton_Click(object? sender, RoutedEventArgs e)
	{
		if (sender is Button { ContextMenu: not null } button)
		{
			ContextMenu menu = button.ContextMenu;
			if (_openedTopMenu != null && !ReferenceEquals(_openedTopMenu, menu))
			{
				_openedTopMenu.Close();
			}

			menu.PlacementTarget = button;
			menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
			menu.HorizontalOffset = 0;
			menu.VerticalOffset = 3;
			menu.Open(button);
			_openedTopMenu = menu;
		}
	}

	private async void SaveFileMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		await SaveSelectedDocumentAsync(saveAs: false);
	}

	private async void SaveFileAsMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		await SaveSelectedDocumentAsync(saveAs: true);
	}

	private async void RunButton_Click(object? sender, RoutedEventArgs e)
	{
		if (ViewModel.SelectedDocumentIsQuery)
		{
			await FlushSessionAutosaveAsync();
			await ExecuteEditorTextAsync(includePlan: false);
		}
	}

	private async void PlanButton_Click(object? sender, RoutedEventArgs e)
	{
		if (ViewModel.SelectedDocumentIsQuery)
		{
			await FlushSessionAutosaveAsync();
			await ExecuteEditorTextAsync(includePlan: true);
		}
	}

	private async void LoadMoreResultRowsButton_Click(object? sender, RoutedEventArgs e)
	{
		if (!ViewModel.SelectedDocumentCanLoadMoreRows)
		{
			return;
		}

		await ExecuteEditorTextAsync(
			ViewModel.SelectedDocumentLastExecutionIncludedPlan,
			ViewModel.SelectedDocumentNextPreviewLimit,
			ViewModel.SelectedDocumentLastExecutedSql,
			ViewModel.SelectedDocumentLastExecutedSqlBaseOffset);
	}

	private void CancelExecutionButton_Click(object? sender, RoutedEventArgs e)
	{
		_executionController.CancelExecution(ViewModel, AppendUiLog);
	}

	private async void FormatButton_Click(object? sender, RoutedEventArgs e)
	{
		await FormatEditorSelectionOrAllAsync();
	}

	private void FindButton_Click(object? sender, RoutedEventArgs e)
	{
		ApplySearchPanelState(_searchPanelController.OpenFind(ViewModel));
	}

	private void ReplaceButton_Click(object? sender, RoutedEventArgs e)
	{
		ApplySearchPanelState(_searchPanelController.ToggleReplace(ViewModel));
	}

	private void ConnectionManagerButton_Click(object? sender, RoutedEventArgs e)
	{
		_connectionCenterController.OpenDialog(ViewModel);
		SyncConnectionEditor();
	}

	private void CloseConnectionDialogButton_Click(object? sender, RoutedEventArgs e)
	{
		_connectionCenterController.CloseDialog(ViewModel);
	}

	private void NewConnectionButton_Click(object? sender, RoutedEventArgs e)
	{
		_connectionCenterController.CreateConnection(ViewModel);
		SyncConnectionEditor();
	}

	private void DuplicateConnectionButton_Click(object? sender, RoutedEventArgs e)
	{
		_connectionCenterController.DuplicateSelectedConnection(ViewModel);
		SyncConnectionEditor();
	}

	private void EditConnectionButton_Click(object? sender, RoutedEventArgs e)
	{
		ViewModel.BeginEditConnectionDraft();
		SyncConnectionEditor();
	}

	private void CancelConnectionEditButton_Click(object? sender, RoutedEventArgs e)
	{
		ViewModel.CancelConnectionEditorDraft();
		SyncConnectionEditor();
	}

	private async void SaveConnectionButton_Click(object? sender, RoutedEventArgs e)
	{
		ConnectionCenterActionResult result = await _connectionCenterController.SaveSelectedConnectionWithFeedbackAsync(ViewModel, PasswordTextBox.Text);
		SyncConnectionEditor();
		await MessageBoxAsync(result.Title, result.Message);
	}

	private void DeleteConnectionButton_Click(object? sender, RoutedEventArgs e)
	{
		_connectionCenterController.DeleteSelectedConnection(ViewModel);
		SyncConnectionEditor();
	}

	private async void ImportConnectionsButton_Click(object? sender, RoutedEventArgs e)
	{
		TopLevel? topLevel = TopLevel.GetTopLevel(this);
		if (topLevel?.StorageProvider == null)
		{
			return;
		}
		try
		{
			IStorageFile? file = (await topLevel.StorageProvider.OpenFilePickerAsync(_connectionCenterController.BuildImportPickerOptions(ViewModel))).FirstOrDefault();
			if (file != null)
			{
				string? path = file.TryGetLocalPath();
				if (!string.IsNullOrWhiteSpace(path))
				{
					IReadOnlyList<ConnectionProfile> profiles = await ViewModel.ImportConnectionsAsync(path);
					if (profiles.Count == 0)
					{
						await MessageBoxAsync(ViewModel.UiText.ImportConnectionsDialogTitle, ViewModel.UiText.NoImportableConnections);
						return;
					}

					ConnectionImportPreviewWindow previewWindow = new ConnectionImportPreviewWindow(profiles, ViewModel.SnapshotConnections(), path);
					if (await previewWindow.ShowDialog<bool?>(this) == true)
					{
						IReadOnlyList<ConnectionImportPreviewItem> selectedItems = previewWindow.SelectedItems;
						if (selectedItems.Count == 0)
						{
							return;
						}

						ConnectionImportResult result = await ViewModel.ImportSelectedConnectionsAsync(selectedItems);
						SyncConnectionEditor();
						await MessageBoxAsync(ViewModel.UiText.ImportConnectionsDialogTitle, result.BuildSummary());
					}
				}
			}
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.ImportConnectionsDialogTitle, ex.Message);
		}
	}

	private async void ExportConnectionsButton_Click(object? sender, RoutedEventArgs e)
	{
		TopLevel? topLevel = TopLevel.GetTopLevel(this);
		if (topLevel?.StorageProvider != null)
		{
			try
			{
				IReadOnlyList<ConnectionProfile> profiles = ViewModel.SnapshotConnections();
				if (profiles.Count == 0)
				{
					await MessageBoxAsync(ViewModel.UiText.ExportConnectionsDialogTitle, ViewModel.UiText.NoExportableConnections);
					return;
				}

				ConnectionExportSelectionWindow selectionWindow = new ConnectionExportSelectionWindow(profiles, ViewModel.SelectedConnectionProfile?.Id ?? string.Empty);
				if (await selectionWindow.ShowDialog<bool?>(this) != true)
				{
					return;
				}

				IReadOnlyList<ConnectionProfile> selectedProfiles = selectionWindow.SelectedProfiles;
				if (selectedProfiles.Count == 0)
				{
					return;
				}

				string? path = (await topLevel.StorageProvider.SaveFilePickerAsync(_connectionCenterController.BuildExportPickerOptions(ViewModel)))?.TryGetLocalPath();
				if (!string.IsNullOrWhiteSpace(path))
				{
					await _connectionCenterController.ExportConnectionsAsync(ViewModel, path, PasswordTextBox.Text, selectedProfiles);
					await MessageBoxAsync(ViewModel.UiText.ExportConnectionsDialogTitle, string.Format(CultureInfo.CurrentCulture, ViewModel.UiText.ExportConnectionsCompletedFormat, selectedProfiles.Count));
				}
			}
			catch (Exception ex)
			{
				await MessageBoxAsync(ViewModel.UiText.ExportConnectionsDialogTitle, ex.Message);
			}
		}
	}

	private async void TestConnectionButton_Click(object? sender, RoutedEventArgs e)
	{
		if (ViewModel.SelectedConnectionProfile != null)
		{
			try
			{
				await MessageBoxAsync(message: await _connectionCenterController.TestSelectedConnectionAsync(ViewModel, PasswordTextBox.Text), title: ViewModel.UiText.ConnectionTestTitle);
			}
			catch (Exception ex)
			{
				await MessageBoxAsync(ViewModel.UiText.ConnectionTestFailedTitle, ex.Message);
			}
		}
	}

	private async void UseConnectionButton_Click(object? sender, RoutedEventArgs e)
	{
		if (ViewModel.SelectedConnectionProfile != null)
		{
			try
			{
				await _connectionCenterController.UseSelectedConnectionAsync(ViewModel, PasswordTextBox.Text);
			}
			catch (Exception ex)
			{
				await MessageBoxAsync(ViewModel.UiText.MetadataLoadFailedTitle, ex.Message);
			}
		}
	}

	private void ConnectionListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (!ViewModel.ConnectionEditorCanChangeSelection)
		{
			SyncConnectionEditor();
			return;
		}

		ConnectionEditorState state = _connectionCenterController.HandleSelectionChanged(ViewModel);
		ApplyConnectionEditorState(state);
	}

	private void ProviderComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (_isSyncingConnectionEditor)
		{
			return;
		}

		ConnectionEditorState? connectionEditorState = _connectionCenterController.HandleProviderSelectionChanged(ViewModel, ProviderComboBox.SelectedItem as DatabaseProviderDefinition);
		if (connectionEditorState != null)
		{
			ApplyConnectionEditorState(connectionEditorState);
		}
	}

	private void OracleModeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (_isSyncingConnectionEditor)
		{
			return;
		}

		ViewModel.RefreshOracleUiState();
	}

	private void LanguageMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		if (sender is MenuItem { Tag: string tag })
		{
			ViewModel.CurrentLanguageCode = tag;
		}
	}

	private void PasswordTextBox_TextChanged(object? sender, TextChangedEventArgs e)
	{
		if (_isSyncingConnectionEditor)
		{
			return;
		}

		_connectionCenterController.CommitEditorPassword(ViewModel, PasswordTextBox.Text);
	}

	private void SearchTextBox_KeyDown(object? sender, KeyEventArgs e)
	{
		HandleSearchPanelKeyDown(e, replaceBox: false);
	}

	private void ReplaceTextBox_KeyDown(object? sender, KeyEventArgs e)
	{
		HandleSearchPanelKeyDown(e, replaceBox: true);
	}

	private void CloseSearchPanelButton_Click(object? sender, RoutedEventArgs e)
	{
		CloseSearchPanel();
	}

	private void FindNextButton_Click(object? sender, RoutedEventArgs e)
	{
		FindNext();
	}

	private void ReplaceCurrentButton_Click(object? sender, RoutedEventArgs e)
	{
		ReplaceCurrent();
	}

	private async void FindTableButton_Click(object? sender, RoutedEventArgs e)
	{
		await FindTableFromEditorAsync();
	}

	private void ReplaceAllButton_Click(object? sender, RoutedEventArgs e)
	{
		if (!string.IsNullOrEmpty(ViewModel.SearchText))
		{
			TextEditor editorTextBox = EditorTextBox;
			if (editorTextBox.Text == null)
			{
				editorTextBox.Text = string.Empty;
			}
			string replacedText = _searchController.ReplaceAll(EditorTextBox.Text, ViewModel.SearchText, ViewModel.ReplaceText ?? string.Empty);
			EditorTextBox.Text = replacedText;
		}
	}

	private void EditorTextBox_KeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.F5 || (e.Key == Key.Return && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
		{
			if (ViewModel.SelectedDocumentIsQuery)
			{
				_ = ExecuteEditorTextAsync(includePlan: false);
			}
			e.Handled = true;
		}
		else if (e.Key == Key.T && e.KeyModifiers.HasFlag(KeyModifiers.Control))
		{
			_ = FormatEditorSelectionOrAllAsync();
			e.Handled = true;
		}
		else if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
		{
			ViewModel.ShowSearch();
			SearchTextBox.Focus();
			SearchTextBox.SelectAll();
			e.Handled = true;
		}
		else if (e.Key == Key.H && e.KeyModifiers.HasFlag(KeyModifiers.Control))
		{
			ViewModel.ShowSearch(includeReplace: true);
			ReplaceTextBox.Focus();
			ReplaceTextBox.SelectAll();
			e.Handled = true;
		}
		else if (e.Key == Key.R && e.KeyModifiers.HasFlag(KeyModifiers.Control))
		{
			_ = FindTableFromEditorAsync();
			e.Handled = true;
		}
		else if (ViewModel.IsCompletionOpen)
		{
			switch (_completionController.GetPopupKeyAction(e.Key))
			{
			case CompletionController.PopupKeyAction.MoveDown:
				MoveCompletionSelection(1);
				e.Handled = true;
				break;
			case CompletionController.PopupKeyAction.MoveUp:
				MoveCompletionSelection(-1);
				e.Handled = true;
				break;
			case CompletionController.PopupKeyAction.Commit:
				ApplyCompletion();
				e.Handled = true;
				break;
			case CompletionController.PopupKeyAction.Close:
				ViewModel.IsCompletionOpen = false;
				e.Handled = true;
				break;
			}
		}
	}

	private void EditorTextBox_PreviewKeyDown(object? sender, KeyEventArgs e)
	{
		if (ViewModel.IsCompletionOpen)
		{
			switch (_completionController.GetPopupKeyAction(e.Key))
			{
			case CompletionController.PopupKeyAction.MoveDown:
				MoveCompletionSelection(1);
				e.Handled = true;
				break;
			case CompletionController.PopupKeyAction.MoveUp:
				MoveCompletionSelection(-1);
				e.Handled = true;
				break;
			case CompletionController.PopupKeyAction.Commit:
				ApplyCompletion();
				e.Handled = true;
				break;
			case CompletionController.PopupKeyAction.Close:
				ViewModel.IsCompletionOpen = false;
				e.Handled = true;
				break;
			}
		}
	}

	private void EditorTextBox_KeyUp(object? sender, KeyEventArgs e)
	{
		if (_suppressCompletionDepth <= 0)
		{
			// TextInput schedules completion work; KeyUp only closes the popup for keys that leave no useful prefix.
			if (_completionController.ShouldHideOnKeyUp(e.Key) && !ShouldKeepLocalizedCompletionVisibleForKey(e.Key))
			{
				HideCompletionPopup();
			}
		}
	}

	private void EditorTextBox_TextInput(object? sender, TextInputEventArgs e)
	{
		if (_suppressCompletionDepth > 0 || string.IsNullOrWhiteSpace(e.Text))
		{
			return;
		}

		if (ShouldTriggerCompletionFromTextInput(e.Text))
		{
			AppendUiLog($"CompletionTextInput: text={ToCompletionLogValue(e.Text)}; caret={EditorTextBox.CaretOffset}");
			if (e.Text.Any(static ch => ch > 127))
			{
				_localizedCompletionKeepAliveUntilUtc = DateTime.UtcNow.AddMilliseconds(800.0);
			}
			ScheduleDeferredCompletionRefresh();
		}
	}

	private static bool ShouldTriggerCompletionFromTextInput(string text)
	{
		return text.Any(static ch => ch == '.' || ch == '_' || char.IsLetterOrDigit(ch) || ch > 127);
	}

	private void EditorTextBox_TextChanged(object? sender, EventArgs e)
	{
		// This handler runs for every keystroke, so keep parsing and metadata work out of it.
		string editorText = EditorTextBox.Text ?? string.Empty;
		int caretOffset = EditorTextBox.CaretOffset;
		if (ViewModel.SelectedDocument != null)
		{
			ViewModel.SelectedDocument.Content = editorText;
			ViewModel.SelectedDocument.CaretOffset = caretOffset;
			ViewModel.SelectedDocument.IsDirty = true;
			ViewModel.NotifySelectedDocumentContentEdited();
			ScheduleSessionAutosave();
		}
		if (_suppressCompletionDepth > 0)
		{
			CancelCompletionRefresh();
			HideCompletionPopup();
			ApplySyntaxHighlightingForCurrentDocument(editorText: editorText);
		}
		else
		{
			ApplySyntaxHighlightingForCurrentDocument(editorText: editorText);
		}
	}

	private void ScheduleSessionAutosave()
	{
		_sessionAutosaveCancellationTokenSource?.Cancel();
		_sessionAutosaveCancellationTokenSource?.Dispose();
		_sessionAutosaveCancellationTokenSource = new CancellationTokenSource();
		CancellationToken token = _sessionAutosaveCancellationTokenSource.Token;
		int version = Interlocked.Increment(ref _sessionAutosaveVersion);
		_sessionAutosaveTask = RunSessionAutosaveAsync(version, token);
	}

	private async Task RunSessionAutosaveAsync(int version, CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);
			if (cancellationToken.IsCancellationRequested || version != _sessionAutosaveVersion)
			{
				return;
			}

			await ViewModel.PersistAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			AppendUiLog("Session autosave failed: " + ex.Message);
		}
	}

	private async Task FlushSessionAutosaveAsync()
	{
		_sessionAutosaveCancellationTokenSource?.Cancel();
		_sessionAutosaveCancellationTokenSource?.Dispose();
		_sessionAutosaveCancellationTokenSource = null;
		Interlocked.Increment(ref _sessionAutosaveVersion);
		try
		{
			await ViewModel.PersistAsync();
		}
		catch (Exception ex)
		{
			AppendUiLog("Session autosave flush failed: " + ex.Message);
		}
	}

	private async void ExplorerTreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		ViewModel.SetExplorerFocus(ExplorerTreeView.SelectedItem as ObjectNode);
		object? selectedItem = ExplorerTreeView.SelectedItem;
		ObjectNode? node = selectedItem as ObjectNode;
		if (node != null && !string.Equals(node.Type, "status", StringComparison.OrdinalIgnoreCase) && !string.Equals(node.Type, "error", StringComparison.OrdinalIgnoreCase))
		{
			try
			{
				await ViewModel.EnsureNodeChildrenLoadedAsync(node);
			}
			catch (Exception ex)
			{
				await MessageBoxAsync(ViewModel.UiText.ExplorerLoadFailedTitle, ex.Message);
			}
		}
	}

	private async void ExplorerTreeView_DoubleTapped(object? sender, TappedEventArgs e)
	{
		HideCompletionPopup();
		ObjectNode? selectedNode = ExplorerTreeView.SelectedItem as ObjectNode;
		HashSet<string> expandedKeys = CaptureExpandedExplorerNodeKeys();
		ExplorerOpenResult result = await _explorerOpenController.OpenAsync(ViewModel, selectedNode);
		if (!result.Success)
		{
			await MessageBoxAsync(result.Title, result.Message);
			return;
		}
		if (selectedNode != null && (selectedNode.IsConnectionNode || selectedNode.IsSchemaNode))
		{
			RestoreExplorerExpansion(expandedKeys, selectedNode.Key, selectedNode.IsSchemaNode ? selectedNode.ParentKey : null);
		}
		if ((selectedNode?.CanOpenDetails ?? false) && !string.Equals(selectedNode.Type, "connection", StringComparison.OrdinalIgnoreCase))
		{
			await ObjectDetailsCoordinator.OpenAsync(this, selectedNode);
		}
		else if (selectedNode != null)
		{
			ExpandExplorerNode(selectedNode.Key);
		}
		else
		{
			ExpandConnectedRootNode();
		}
	}

	private void ResultWorkspaceTabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (TryGetViewModel() != null)
		{
			HideCompletionPopup();
			if (ViewModel.SelectedDocumentSelectedWorkspaceTabIsResult)
			{
				RequestResultGridRender();
			}
		}
	}

	private void ResultSetTabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (TryGetViewModel() != null)
		{
			HideCompletionPopup();
			if (ViewModel.SelectedDocumentSelectedWorkspaceTabIsResult)
			{
				RequestResultGridRender();
			}
		}
	}

	private void ResultNavigationChip_Click(object? sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: ResultSetViewItem resultSet } && TryGetViewModel() != null)
		{
			HideCompletionPopup();
			ViewModel.SelectedDocumentSelectedResultSet = resultSet;
			SelectResultWorkspaceTabByKind((ResultWorkspaceTabItem item) => item.IsResultTab);
			if (ViewModel.SelectedDocumentSelectedWorkspaceTabIsResult)
			{
				RequestResultGridRender();
			}
		}
	}

	private void ResultMessageNavigationButton_Click(object? sender, RoutedEventArgs e)
	{
		SelectResultWorkspaceTabByKind((ResultWorkspaceTabItem item) => item.IsMessageTab);
	}

	private void ResultPlanNavigationButton_Click(object? sender, RoutedEventArgs e)
	{
		SelectResultWorkspaceTabByKind((ResultWorkspaceTabItem item) => item.IsPlanTab);
	}

	private void SelectResultWorkspaceTabByKind(Func<ResultWorkspaceTabItem, bool> predicate)
	{
		MainWindowViewModel? viewModel = TryGetViewModel();
		if (viewModel == null)
		{
			return;
		}
		HideCompletionPopup();
		ResultWorkspaceTabItem? targetTab = viewModel.SelectedDocumentWorkspaceTabs.FirstOrDefault(predicate);
		if (targetTab != null)
		{
			viewModel.SelectedDocumentSelectedWorkspaceTab = targetTab;
		}
	}

	private void ResultHeaderModeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (TryGetViewModel() != null)
		{
			RequestResultGridRender();
		}
	}

	private async void DocumentTabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		MainWindowViewModel? viewModel = TryGetViewModel();
		if (viewModel == null)
		{
			return;
		}
		HideCompletionPopup();
		UpdateResultWorkspaceVisibility();
		if (viewModel.SelectedDocumentIsCommentMaintenance)
		{
			_lastTextEditorDocumentKind = string.Empty;
			return;
		}
		if (viewModel.SelectedDocumentIsModelDiagram)
		{
			_lastTextEditorDocumentKind = string.Empty;
			if (!viewModel.SelectedModelDiagramIsLoaded)
			{
				await viewModel.LoadSelectedModelDiagramAsync();
			}
			RequestModelDiagramRender();
			return;
		}
		if (viewModel.SelectedDocumentIsObjectEditor)
		{
			if (!viewModel.SelectedObjectEditorIsLoaded)
			{
				await viewModel.LoadSelectedObjectEditorAsync();
			}
			SyncEditorFromDocument();
			ApplySyntaxHighlightingForCurrentDocument(ShouldForceTextEditorHighlight(viewModel));
			return;
		}
		if (viewModel.SelectedDocumentIsQuery)
		{
			await viewModel.EnsureSelectedDocumentContextReadyAsync();
		}
		SyncEditorFromDocument();
		ApplySyntaxHighlightingForCurrentDocument(ShouldForceTextEditorHighlight(viewModel));
		if (viewModel.SelectedDocumentHasResults || viewModel.SelectedDocumentHasMessage || viewModel.SelectedDocumentHasExecutionPlan)
		{
			SelectResultWorkspaceTab();
			if (viewModel.SelectedDocumentSelectedWorkspaceTabIsResult)
			{
				RequestResultGridRender(40);
			}
		}
	}

	private async void RetryNodeButton_Click(object? sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: ObjectNode node })
		{
			return;
		}

		try
		{
			await ViewModel.RetryNodeLoadAsync(node);
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.ExplorerLoadFailedTitle, ex.Message);
		}
	}

	private async void DocumentConnectionComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		MainWindowViewModel? viewModel = TryGetViewModel();
		if (viewModel != null && viewModel.SelectedDocumentIsQuery)
		{
			HideCompletionPopup();
			await viewModel.ApplySelectedDocumentConnectionAsync();
		}
	}

	private void DocumentSchemaComboBox_DropDownOpened(object? sender, EventArgs e)
	{
		MainWindowViewModel? viewModel = TryGetViewModel();
		if (viewModel == null || !viewModel.SelectedDocumentIsQuery)
		{
			return;
		}

		viewModel.PrioritizeSelectedDocumentSchemas();
	}

	private async void RefreshNodeMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		if (sender is not MenuItem { Tag: ObjectNode node })
		{
			return;
		}

		try
		{
			await _explorerActionController.RefreshNodeAsync(ViewModel, node);
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.ExplorerRefreshFailedTitle, ex.Message);
		}
	}

	private async void OpenConnectionNodeMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		ConnectionProfile? profile = ResolveConnectionProfileFromNode(node);
		if (profile == null)
		{
			await MessageBoxAsync(ViewModel.UiText.DatabaseConnection, ViewModel.UiText.NoConnectionSelected);
			return;
		}
		try
		{
			HashSet<string> expandedKeys = CaptureExpandedExplorerNodeKeys();
			ViewModel.SelectedConnectionProfile = profile;
			SyncConnectionEditor();
			await ViewModel.SetActiveConnectionAsync(profile);
			ViewModel.SetExplorerFocus((ExplorerTreeView.SelectedItem as ObjectNode) ?? node);
			RestoreExplorerExpansion(expandedKeys, node?.Key);
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.MetadataLoadFailedTitle, ex.Message);
		}
	}

	private async void CloseConnectionNodeMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		ConnectionProfile? profile = ResolveConnectionProfileFromNode(node);
		if (profile == null)
		{
			await MessageBoxAsync(ViewModel.UiText.DatabaseConnection, ViewModel.UiText.NoConnectionSelected);
			return;
		}
		HashSet<string> expandedKeys = CaptureExpandedExplorerNodeKeys();
		await ViewModel.DisconnectConnectionAsync(profile);
		ViewModel.SelectedConnectionProfile = profile;
		SyncConnectionEditor();
		ViewModel.SetExplorerFocus((ExplorerTreeView.SelectedItem as ObjectNode) ?? node);
		RestoreExplorerExpansion(expandedKeys);
	}

	private async void OpenSchemaNodeMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		if (node != null)
		{
			try
			{
				HashSet<string> expandedKeys = CaptureExpandedExplorerNodeKeys();
				await ViewModel.OpenSchemaAsync(node);
				RestoreExplorerExpansion(expandedKeys, node.ParentKey, node.Key);
			}
			catch (Exception ex)
			{
				await MessageBoxAsync(ViewModel.UiText.OpenSchema, ex.Message);
			}
		}
	}

	private async void CloseSchemaNodeMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		if (node != null)
		{
			try
			{
				HashSet<string> expandedKeys = CaptureExpandedExplorerNodeKeys();
				ViewModel.CloseSchema(node);
				RestoreExplorerExpansion(expandedKeys, node.ParentKey);
			}
			catch (Exception ex)
			{
				await MessageBoxAsync(ViewModel.UiText.CloseSchema, ex.Message);
			}
		}
	}

	private async void OpenSchemaCommentMaintenanceMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		if (node == null)
		{
			return;
		}
		ViewModel.SetExplorerFocus(node);
		if (ViewModel.OpenCommentMaintenanceDocument(node) == null)
		{
			await MessageBoxAsync(ViewModel.UiText.CommentMaintenance, ViewModel.UiText.OpenWorkbenchRequiresOpenedSchema);
			return;
		}
		try
		{
			UpdateResultWorkspaceVisibility();
			if (!ViewModel.SelectedCommentWorkspaceIsLoaded)
			{
				await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
				await ViewModel.LoadSelectedCommentWorkspaceAsync();
			}
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.CommentMaintenance, ex.Message);
		}
	}

	private async void ConnectionDetailsNodeMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		ConnectionProfile? profile = ResolveConnectionProfileFromNode(node);
		if (profile == null)
		{
			await MessageBoxAsync(ViewModel.UiText.ConnectionDetails, ViewModel.UiText.NoConnectionSelected);
			return;
		}
		string details = string.Join(Environment.NewLine, new[]
		{
			ViewModel.UiText.ConnectionName + ": " + profile.Name,
			ViewModel.UiText.Database + ": " + profile.Database,
			ViewModel.UiText.Schema + ": " + (string.IsNullOrWhiteSpace(profile.Schema) ? "(Default)" : profile.Schema),
			ViewModel.UiText.Server + ": " + profile.Server,
			"Provider: " + profile.ProviderName,
			ViewModel.UiText.EnvironmentTag + ": " + profile.EnvironmentTag,
			string.Format(CultureInfo.CurrentCulture, ViewModel.UiText.ConnectionDetailsStatusFormat, (node?.IsConnected ?? false) ? ViewModel.UiText.Connected : ViewModel.UiText.Disconnected)
		});
		ViewModel.SelectedConnectionProfile = profile;
		SyncConnectionEditor();
		await MessageBoxAsync(ViewModel.UiText.ConnectionDetails, details);
	}

	private void DuplicateConnectionNodeMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		ConnectionProfile? connectionProfile = ResolveConnectionProfileFromNode(node);
		if (connectionProfile != null)
		{
			ViewModel.SelectedConnectionProfile = connectionProfile;
			_connectionCenterController.DuplicateSelectedConnection(ViewModel);
			_connectionCenterController.OpenDialog(ViewModel);
			SyncConnectionEditor();
		}
	}

	private void EditConnectionNodeMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		ConnectionProfile? connectionProfile = ResolveConnectionProfileFromNode(node);
		if (connectionProfile != null)
		{
			ViewModel.SelectedConnectionProfile = connectionProfile;
			_connectionCenterController.OpenDialog(ViewModel);
			SyncConnectionEditor();
		}
	}

	private async void CopyNodeNameMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		if (node != null)
		{
			TopLevel? topLevel = TopLevel.GetTopLevel(this);
			if (topLevel?.Clipboard != null)
			{
				await topLevel.Clipboard.SetTextAsync(_explorerActionController.GetNodeNameToCopy(node));
			}
		}
	}

	private async void DesignTableMenuItem_Legacy(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		if (node != null)
		{
			await _explorerActionController.OpenTableDesignAsync(TableDesignCoordinator, this, node);
		}
	}

	private async void ExportTableStructureMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		if (node != null)
		{
			try
			{
				ExplorerActionResult result = await _explorerActionController.ExportTableStructureAsync(ViewModel, node);
				await SaveTextToFileAsync(result.SuggestedFileName, result.Content);
			}
			catch (Exception ex)
			{
				await MessageBoxAsync(ViewModel.UiText.ExportTableStructureTitle, ex.Message);
			}
		}
	}

	private ConnectionProfile? ResolveConnectionProfileFromNode(ObjectNode? node)
	{
		ConnectionProfile? profile = ViewModel.ResolveConnectionProfileForNode(node);
		if (profile == null)
		{
			return null;
		}
		return ViewModel.ConnectionProfiles.FirstOrDefault((ConnectionProfile item) => string.Equals(item.Id, profile.Id, StringComparison.OrdinalIgnoreCase)) ?? profile;
	}

	private async void ExportTableDataMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		if (node != null)
		{
			try
			{
				ExplorerActionResult result = await _explorerActionController.ExportTableDataAsync(ViewModel, node);
				await SaveTextToFileAsync(result.SuggestedFileName, result.Content);
			}
			catch (Exception ex)
			{
				await MessageBoxAsync(ViewModel.UiText.ExportTableDataTitle, ex.Message);
			}
		}
	}

	private void GenerateSelectMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		GenerateObjectSqlTemplate(sender, "Select");
	}

	private void GenerateCountMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		GenerateObjectSqlTemplate(sender, "Count");
	}

	private void GenerateInsertTemplateMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		GenerateObjectSqlTemplate(sender, "Insert");
	}

	private void GenerateUpdateTemplateMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		GenerateObjectSqlTemplate(sender, "Update");
	}

	private void GenerateObjectSqlTemplate(object? sender, string templateKind)
	{
		ObjectNode? objectNode = _explorerActionController.TryGetNode(sender);
		if (objectNode != null)
		{
			_explorerActionController.OpenQueryForObject(ViewModel, objectNode, templateKind);
			FocusEditor();
		}
	}

	private void SelectAllEditorMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		EditorTextBox.SelectAll();
		FocusEditor();
	}

	private async void CopyEditorMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		TopLevel? topLevel = TopLevel.GetTopLevel(this);
		if (topLevel?.Clipboard != null)
		{
			await topLevel.Clipboard.SetTextAsync(EditorTextBox.SelectedText ?? string.Empty);
		}
	}

	private async void CutEditorMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		TopLevel? topLevel = TopLevel.GetTopLevel(this);
		if (topLevel?.Clipboard != null)
		{
			string selected = EditorTextBox.SelectedText ?? string.Empty;
			if (selected.Length != 0)
			{
				await topLevel.Clipboard.SetTextAsync(selected);
				ReplaceSelectionOrAll(string.Empty, replaceAllWhenNoSelection: false);
			}
		}
	}

	private async void PasteEditorMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		TopLevel? topLevel = TopLevel.GetTopLevel(this);
		if (topLevel?.Clipboard != null)
		{
			string? text = await topLevel.Clipboard.TryGetTextAsync();
			if (!string.IsNullOrEmpty(text))
			{
				InsertTextAtCaret(text, suppressCompletionPopup: true);
			}
		}
	}

	private async void CloseDocumentButton_Click(object? sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: EditorDocument tag })
		{
			await FlushSessionAutosaveAsync();
			ViewModel.CloseDocument(tag);
			await FlushSessionAutosaveAsync();
			UpdateResultWorkspaceVisibility();
			FocusEditor();
		}
	}

	private void ToggleResultWorkspaceButton_Click(object? sender, RoutedEventArgs e)
	{
		ViewModel.ToggleSelectedDocumentResultWorkspace();
		UpdateResultWorkspaceVisibility();
		if (ViewModel.SelectedDocumentShouldShowResultWorkspace && ViewModel.SelectedDocumentSelectedWorkspaceTabIsResult)
		{
			RequestResultGridRender();
		}
		else if (ViewModel.SelectedDocumentShouldShowResultWorkspace)
		{
			SelectResultWorkspaceTab();
		}
	}

	private void CloseResultWorkspaceButton_Click(object? sender, RoutedEventArgs e)
	{
		ViewModel.SetSelectedDocumentResultWorkspaceOpen(isOpen: false);
		UpdateResultWorkspaceVisibility();
	}

	private async void AboutMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		await ShowAboutDialogAsync();
	}

	private void CompletionListBox_DoubleTapped(object? sender, TappedEventArgs e)
	{
		ApplyCompletion();
	}

	private void EditorTextBox_PointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt))
		{
			HideCompletionPopup();
		}
	}

	private void DocumentTabControl_PointerPressed(object? sender, PointerPressedEventArgs e)
	{
		HideCompletionPopup();
	}
	private void FocusResultInteractionHost()
	{
		ResultInteractionHost?.Focus();
	}

	private void UpdateValueDetailForCell(ResultSetViewItem resultSet, ResultCellContext context)
	{
		ViewModel.SetSelectedDocumentValueDetail(resultSet, context, _resultWorkspaceController.SelectedCellSelectionCount);
	}

	private void BeginInlineCellEdit(ResultSetViewItem resultSet, ResultCellContext context)
	{
		if (!resultSet.IsEditMode ||
		    context.ColumnIndex < 0 ||
		    context.ColumnIndex >= resultSet.Columns.Count ||
		    !resultSet.Columns[context.ColumnIndex].IsEditable ||
		    !_resultWorkspaceController.TryGetCellBorder(context, out Border? cellBorder) ||
		    cellBorder == null)
		{
			return;
		}

		EndInlineCellEdit();
		string cellText = context.ColumnIndex < context.Row.Values.Count ? context.Row.Values[context.ColumnIndex] : string.Empty;
		TextBox editor = _resultWorkspaceController.BuildEditableCellEditor(context, cellText, EditableResultCellTextBox_TextChanged);
		editor.LostFocus += InlineEditableCell_LostFocus;
		editor.KeyDown += InlineEditableCell_KeyDown;
		cellBorder.Child = editor;
		_activeEditableCellBorder = cellBorder;
		_activeEditableCellContext = context;
		Dispatcher.UIThread.Post(() =>
		{
			editor.Focus();
			editor.SelectAll();
		}, DispatcherPriority.Background);
	}

	private void EndInlineCellEdit()
	{
		if (_activeEditableCellBorder == null || _activeEditableCellContext == null)
		{
			_activeEditableCellBorder = null;
			_activeEditableCellContext = null;
			return;
		}

		string cellText = _activeEditableCellContext.ColumnIndex < _activeEditableCellContext.Row.Values.Count
			? _activeEditableCellContext.Row.Values[_activeEditableCellContext.ColumnIndex]
			: string.Empty;
		_activeEditableCellBorder.Child = _resultWorkspaceController.BuildDisplayCellContent(cellText);
		_activeEditableCellBorder = null;
		_activeEditableCellContext = null;
	}

	private void InlineEditableCell_LostFocus(object? sender, RoutedEventArgs e)
	{
		EndInlineCellEdit();
	}

	private void InlineEditableCell_KeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter || e.Key == Key.Escape)
		{
			EndInlineCellEdit();
			FocusResultInteractionHost();
			e.Handled = true;
		}
	}

	private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
	{
		Visual relativeTo = ResultInteractionHost;
		IInputElement? resultInteractionHost = ResultInteractionHost;
		IInputElement control = resultInteractionHost ?? (sender as IInputElement) ?? this;
		Point position = e.GetPosition(relativeTo);
		bool isInsideResultGrid = IsPointWithinVisual(ResultFixedHeaderHost, position, relativeTo) || IsPointWithinVisual(ResultFixedBodyScrollViewer, position, relativeTo) || IsPointWithinVisual(ResultHorizontalScrollViewer, position, relativeTo);
		if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) || IsEditorPointerEvent(e) || IsResultEditorPointerEvent(e) || !isInsideResultGrid)
		{
			return;
		}
		FocusResultInteractionHost();
		ResultSetViewItem? selectedDocumentSelectedResultSet = ViewModel.SelectedDocumentSelectedResultSet;
		if (selectedDocumentSelectedResultSet != null)
		{
			PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
			if (properties.IsLeftButtonPressed && _resultWorkspaceController.TryGetHeaderColumnAtPoint(relativeTo, position, out var columnIndex) && columnIndex >= 0)
			{
				EndInlineCellEdit();
				e.Pointer.Capture(control);
				_resultWorkspaceController.BeginHeaderSelection(selectedDocumentSelectedResultSet, columnIndex);
				ViewModel.ClearSelectedDocumentValueDetail();
				e.Handled = true;
				return;
			}
			if (_resultWorkspaceController.TryGetActionRowAtPoint(relativeTo, position, out ResultRowViewItem? row) && row != null)
			{
				if (properties.IsRightButtonPressed)
				{
					_resultWorkspaceController.PrepareRowContextSelection(row);
					return;
				}
				if (properties.IsLeftButtonPressed)
				{
					EndInlineCellEdit();
					e.Pointer.Capture(control);
					_resultWorkspaceController.BeginSelection(row);
					ViewModel.ClearSelectedDocumentValueDetail();
					e.Handled = true;
					return;
				}
			}
			if (properties.IsLeftButtonPressed && _resultWorkspaceController.TryGetCellAtPoint(relativeTo, position, out ResultCellContext? context) && context != null)
			{
				e.Pointer.Capture(control);
				_resultWorkspaceController.BeginCellSelection(selectedDocumentSelectedResultSet, context);
				UpdateValueDetailForCell(selectedDocumentSelectedResultSet, context);
				BeginInlineCellEdit(selectedDocumentSelectedResultSet, context);
				e.Handled = true;
				return;
			}
		}
		Border completionPopup = CompletionPopup;
		if (completionPopup == null || !completionPopup.IsPointerOver)
		{
			HideCompletionPopup();
		}
	}

	private void Window_PointerMoved(object? sender, PointerEventArgs e)
	{
		if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) || (!_resultWorkspaceController.IsDraggingSelection && !_resultWorkspaceController.IsDraggingCellSelection && !_resultWorkspaceController.IsDraggingHeaderSelection))
		{
			return;
		}
		ResultSetViewItem? selectedDocumentSelectedResultSet = ViewModel.SelectedDocumentSelectedResultSet;
		if (selectedDocumentSelectedResultSet == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			return;
		}
		Visual relativeTo = ResultInteractionHost;
		Point position = e.GetPosition(relativeTo);
		if (_resultWorkspaceController.IsDraggingHeaderSelection)
		{
			if (_resultWorkspaceController.TryGetHeaderColumnAtPoint(relativeTo, position, out var columnIndex) && columnIndex >= 0)
			{
				EndInlineCellEdit();
				_resultWorkspaceController.UpdateHeaderSelection(selectedDocumentSelectedResultSet, columnIndex, isLeftButtonPressed: true);
			}
		}
		else if (_resultWorkspaceController.IsDraggingCellSelection)
		{
			if (_resultWorkspaceController.TryGetCellAtPoint(relativeTo, position, out ResultCellContext? context) && context != null)
			{
				_resultWorkspaceController.UpdateCellSelection(selectedDocumentSelectedResultSet, context, isLeftButtonPressed: true);
				UpdateValueDetailForCell(selectedDocumentSelectedResultSet, context);
			}
		}
		else if (_resultWorkspaceController.TryGetActionRowAtPoint(relativeTo, position, out ResultRowViewItem? row) && row != null)
		{
			EndInlineCellEdit();
			_resultWorkspaceController.UpdateDragSelection(selectedDocumentSelectedResultSet, row, isLeftButtonPressed: true);
		}
	}

	private void Window_PointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt) && (_resultWorkspaceController.IsDraggingSelection || _resultWorkspaceController.IsDraggingCellSelection || _resultWorkspaceController.IsDraggingHeaderSelection))
		{
			e.Pointer.Capture(null);
			_resultWorkspaceController.EndSelection();
		}
	}

	private void CompletionListBox_KeyDown(object? sender, KeyEventArgs e)
	{
		if (_completionController.GetPopupKeyAction(e.Key) == CompletionController.PopupKeyAction.Commit)
		{
			ApplyCompletion();
			e.Handled = true;
		}
	}

	private void ResultInteractionHost_KeyDown(object? sender, KeyEventArgs e)
	{
		if (TryHandleResultSelectAllShortcut(e))
		{
			return;
		}

		TryHandleResultCopyShortcut(e);
	}

	private void Window_KeyDown(object? sender, KeyEventArgs e)
	{
		if (TryHandleResultCopyShortcut(e))
		{
			return;
		}
		if (e.Key == Key.S && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
		{
			_ = SaveSelectedDocumentAsync(saveAs: true);
			e.Handled = true;
			return;
		}
		if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
		{
			_ = SaveSelectedDocumentAsync(saveAs: false);
			e.Handled = true;
			return;
		}
		if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Control))
		{
			_ = OpenDocumentFileAsync();
			e.Handled = true;
			return;
		}
		SearchPanelState? searchPanelState = _searchPanelController.HandleShortcut(ViewModel, e.Key, e.KeyModifiers);
		if (searchPanelState != null)
		{
			ApplySearchPanelState(searchPanelState);
			e.Handled = true;
		}
	}

	private void Window_KeyUp(object? sender, KeyEventArgs e)
	{
	}

	private async Task ExecuteEditorTextAsync(bool includePlan, int? previewLimit = null, string? sqlOverride = null, int? sqlBaseOffsetOverride = null)
	{
		if (!ViewModel.SelectedDocumentIsQuery)
		{
			return;
		}
		(string sql, int sqlBaseOffset) = string.IsNullOrWhiteSpace(sqlOverride)
			? GetSqlForExecution()
			: (sqlOverride!, Math.Max(0, sqlBaseOffsetOverride ?? 0));
		if (string.IsNullOrWhiteSpace(sql))
		{
			return;
		}
		if (ViewModel.IsSelectedDocumentConnectionLinked())
		{
			ViewModel.SetSelectedDocumentResultWorkspaceOpen(isOpen: true);
			UpdateResultWorkspaceVisibility();
		}
		ClearRenderedResultGrid();
		try
		{
			int maxPreviewRows = Math.Clamp(previewLimit ?? MainWindowViewModel.DefaultResultPreviewRowLimit, 1, MainWindowViewModel.MaxResultPreviewRowLimit);
			if (await _executionController.ExecuteAsync(ViewModel, sql, includePlan, AppendUiLog, sqlBaseOffset, maxPreviewRows))
			{
				SelectResultWorkspaceTab(includePlan);
				UpdateResultWorkspaceVisibility();
				if (ViewModel.SelectedDocumentSelectedWorkspaceTabIsResult)
				{
					RequestResultGridRender();
				}
				TryLocateExecutionError(ViewModel.SelectedDocumentLastExecutionError);
			}
		}
		finally
		{
			UpdateResultWorkspaceVisibility();
		}
	}

	private async void EditResultButton_Click(object? sender, RoutedEventArgs e)
	{
		ResultSetViewItem? resultSet = ViewModel.SelectedDocumentSelectedResultSet;
		ResultCellContext? selectedCell = _resultWorkspaceController.SelectedCell;
		ViewModel.BeginSelectedResultEdit();
		await RenderSelectedResultGridAsync();
		if (resultSet?.IsEditMode == true && selectedCell != null)
		{
			_resultWorkspaceController.BeginCellSelection(resultSet, selectedCell);
			UpdateValueDetailForCell(resultSet, selectedCell);
			BeginInlineCellEdit(resultSet, selectedCell);
		}
	}

	private async void SaveEditedResultButton_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			EndInlineCellEdit();
			EditableResultMutationResult? result = await ViewModel.SaveSelectedResultEditAsync();
			if (result != null)
			{
				AppendUiLog($"EditableResult:save; summary={result.Summary}; affected={result.AffectedRows}");
			}
			await RenderSelectedResultGridAsync();
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.SaveEditedResultTitle, ex.Message);
		}
	}

	private async void CancelEditedResultButton_Click(object? sender, RoutedEventArgs e)
	{
		EndInlineCellEdit();
		ViewModel.CancelSelectedResultEdit();
		await RenderSelectedResultGridAsync();
	}

	private async Task FormatEditorSelectionOrAllAsync()
	{
		string sql = GetSelectedTextOrAll();
		if (string.IsNullOrWhiteSpace(sql))
		{
			return;
		}
		HideCompletionPopup();
		MainWindowViewModel? viewModel = TryGetViewModel();
		if (viewModel == null)
		{
			return;
		}
		Func<string, string> format = viewModel.FormatText;
		Task<string> formattingTask = Task.Run(() => format(sql));
		if (await Task.WhenAny(formattingTask, Task.Delay(FormattingTimeout)) != formattingTask)
		{
			await ShowInfoMessageAsync(ViewModel.UiText.FormatTimeoutTitle, ViewModel.UiText.FormatTimeoutMessage);
			return;
		}
		string formatted = await formattingTask;
		await Dispatcher.UIThread.InvokeAsync(delegate
		{
			RunWithoutCompletion(delegate
			{
				ReplaceSelectionOrAll(formatted);
			});
		}, DispatcherPriority.Background);
	}

	private async Task FindTableFromEditorAsync()
	{
		FindTableResult result = await _findTableController.FindAsync(ViewModel, GetSelectedText());
		if (!result.Success || result.Match == null)
		{
			await MessageBoxAsync(result.Title, result.Message);
		}
		else
		{
			await RevealExplorerNodeAsync(result.Match);
		}
	}

	private async Task RevealExplorerNodeAsync(ObjectNode targetNode)
	{
		List<ObjectNode> path = BuildExplorerNodePath(targetNode);
		if (path.Count == 0)
		{
			ExplorerTreeView.SelectedItem = targetNode;
			return;
		}
		foreach (ObjectNode node in path.Take(path.Count - 1))
		{
			node.IsExpanded = true;
			await ViewModel.EnsureNodeChildrenLoadedAsync(node);
			await Dispatcher.UIThread.InvokeAsync(delegate
			{
				TreeViewItem? treeViewItem = FindTreeViewItem(node);
				if (treeViewItem != null)
				{
					treeViewItem.IsExpanded = true;
				}
			}, DispatcherPriority.Background);
		}
		ExplorerTreeView.SelectedItem = targetNode;
		await Dispatcher.UIThread.InvokeAsync(delegate
		{
			TreeViewItem? treeViewItem = FindTreeViewItem(targetNode);
			if (treeViewItem != null)
			{
				treeViewItem.IsSelected = true;
				treeViewItem.BringIntoView();
			}
		}, DispatcherPriority.Background);
	}

	private List<ObjectNode> BuildExplorerNodePath(ObjectNode targetNode)
	{
		List<ObjectNode> path = new List<ObjectNode>();
		for (ObjectNode? objectNode = targetNode; objectNode != null; objectNode = (string.IsNullOrWhiteSpace(objectNode.ParentKey) ? null : FindExplorerNodeByKey(ViewModel.ExplorerNodes, objectNode.ParentKey)))
		{
			path.Add(objectNode);
		}
		path.Reverse();
		return path;
	}

	private ObjectNode? FindExplorerNodeByKey(IEnumerable<ObjectNode> nodes, string key)
	{
		foreach (ObjectNode node in nodes)
		{
			if (string.Equals(node.Key, key, StringComparison.OrdinalIgnoreCase))
			{
				return node;
			}
			ObjectNode? objectNode = FindExplorerNodeByKey(node.Children, key);
			if (objectNode != null)
			{
				return objectNode;
			}
		}
		return null;
	}

	private TreeViewItem? FindTreeViewItem(ObjectNode node)
	{
		return ExplorerTreeView.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault((TreeViewItem item) => item.DataContext == node);
	}

	private void FindNext()
	{
		if (!string.IsNullOrEmpty(ViewModel.SearchText) && !string.IsNullOrEmpty(EditorTextBox.Text))
		{
			SearchMatchResult searchMatchResult = _searchController.FindNext(EditorTextBox.Text, ViewModel.SearchText, EditorTextBox.SelectionStart, EditorTextBox.SelectionLength);
			if (searchMatchResult.Found)
			{
				EditorTextBox.Select(searchMatchResult.Start, searchMatchResult.Length);
				FocusEditor();
			}
		}
	}

	private void ReplaceCurrent()
	{
		if (!string.IsNullOrEmpty(ViewModel.SearchText))
		{
			string selectedText = GetSelectedText();
			SearchMatchResult nextMatch;
			string text = _searchController.ReplaceCurrent(EditorTextBox.Text ?? string.Empty, selectedText, ViewModel.SearchText, ViewModel.ReplaceText ?? string.Empty, EditorTextBox.SelectionStart, EditorTextBox.SelectionLength, out nextMatch);
			if (!string.Equals(text, EditorTextBox.Text, StringComparison.Ordinal))
			{
				EditorTextBox.Text = text;
			}
			if (nextMatch.Found)
			{
				EditorTextBox.Select(nextMatch.Start, nextMatch.Length);
				FocusEditor();
			}
			else
			{
				FindNext();
			}
		}
	}

	private void MoveCompletionSelection(int offset)
	{
		CompletionItem? completionItem = _completionController.MoveSelection(ViewModel.CompletionItems, ViewModel.SelectedCompletionItem, offset);
		if (completionItem != null)
		{
			ViewModel.SelectedCompletionItem = completionItem;
			CompletionListBox.SelectedItem = completionItem;
			CompletionListBox.ScrollIntoView(completionItem);
		}
	}

	private bool TryHandleResultCopyShortcut(KeyEventArgs e)
	{
		if (e.Handled || e.Key != Key.C || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
		{
			return false;
		}

		if (_resultWorkspaceController.HasCellSelection)
		{
			_ = CopySelectedCellsToClipboardAsync();
			e.Handled = true;
			return true;
		}

		if (_resultWorkspaceController.HasSelection)
		{
			_ = CopySelectedRowsToClipboardAsync(includeHeader: false);
			e.Handled = true;
			return true;
		}

		return false;
	}

	private bool TryHandleResultSelectAllShortcut(KeyEventArgs e)
	{
		if (e.Handled || e.Source is TextBox || e.Key != Key.A || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
		{
			return false;
		}

		ResultSetViewItem? resultSet = ViewModel.SelectedDocumentSelectedResultSet;
		if (resultSet == null || resultSet.IsMessageOnly || resultSet.Rows.Count == 0)
		{
			return false;
		}

		EndInlineCellEdit();
		_resultWorkspaceController.SelectAllRows(resultSet);
		ViewModel.ClearSelectedDocumentValueDetail();
		e.Handled = true;
		return true;
	}

	private void ScheduleDeferredCompletionRefresh(string? textSnapshot = null, int? caretOffset = null)
	{
		// Completion may inspect SQL context and metadata; a short pause keeps fast typing smooth.
		_pendingCompletionRefreshText = textSnapshot;
		_pendingCompletionRefreshCaretOffset = caretOffset ?? EditorTextBox.CaretOffset;
		_completionController.CancelRefresh();
		_completionRefreshDelayCancellationTokenSource?.Cancel();
		_completionRefreshDelayCancellationTokenSource?.Dispose();
		_completionRefreshDelayCancellationTokenSource = new CancellationTokenSource();
		int version = Interlocked.Increment(ref _deferredCompletionRefreshVersion);
		_ = RunDeferredCompletionRefreshAsync(version, _completionRefreshDelayCancellationTokenSource.Token);
	}

	private async Task RunDeferredCompletionRefreshAsync(int version, CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(80, cancellationToken);
			await Dispatcher.UIThread.InvokeAsync(delegate
			{
				if (cancellationToken.IsCancellationRequested || version != _deferredCompletionRefreshVersion || _suppressCompletionDepth > 0)
				{
					return;
				}

				string text = _pendingCompletionRefreshText ?? EditorTextBox.Text ?? string.Empty;
				int caretOffset = _pendingCompletionRefreshText == null ? EditorTextBox.CaretOffset : _pendingCompletionRefreshCaretOffset;
				_pendingCompletionRefreshText = null;
				ScheduleCompletionRefresh(text, caretOffset);
			}, DispatcherPriority.Background);
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void ScheduleCompletionRefresh(string? textSnapshot = null, int? caretOffsetSnapshot = null)
	{
		string text = textSnapshot ?? EditorTextBox.Text ?? string.Empty;
		int caretOffset = caretOffsetSnapshot ?? EditorTextBox.CaretOffset;
		CompletionController.CompletionRefreshRequest completionRefreshRequest = _completionController.BeginRefresh(text, caretOffset, ViewModel.SelectedDocumentConnectionProfile, ViewModel.SelectedDocumentSchema);
		if (ContainsLocalizedText(completionRefreshRequest.Prefix) || !string.Equals(completionRefreshRequest.Context, "generic", StringComparison.OrdinalIgnoreCase))
		{
			AppendUiLog($"CompletionSchedule: prefix={ToCompletionLogValue(completionRefreshRequest.Prefix)}; context={completionRefreshRequest.Context}; schema={completionRefreshRequest.Schema}; connection={completionRefreshRequest.Connection?.Name ?? string.Empty}; relations={completionRefreshRequest.RelationReferences.Count}; allowEmpty={completionRefreshRequest.AllowEmptyPrefix}; caret={caretOffset}");
		}

		if (!string.IsNullOrWhiteSpace(completionRefreshRequest.Prefix) || completionRefreshRequest.AllowEmptyPrefix)
		{
			// Cached keywords can show right away; database-backed candidates arrive from the async refresh.
			IReadOnlyList<CompletionItem> immediateCompletionItems = ViewModel.BuildImmediateCompletionItems(
				completionRefreshRequest.Prefix,
				completionRefreshRequest.Connection,
				completionRefreshRequest.Schema,
				completionRefreshRequest.Context,
				completionRefreshRequest.ResolvedObjectName,
				completionRefreshRequest.SingleRelationName,
				completionRefreshRequest.AllowEmptyPrefix,
				completionRefreshRequest.RelationReferences,
				completionRefreshRequest.Qualifier);
			if (immediateCompletionItems.Count > 0 && ViewModel.ApplyCompletionItems(immediateCompletionItems) && ViewModel.IsCompletionOpen)
			{
				UpdateCompletionPopupPosition();
			}
		}
		_ = RefreshCompletionAsync(completionRefreshRequest);
	}

	private async Task RefreshCompletionAsync(CompletionController.CompletionRefreshRequest request)
	{
		try
		{
			int debounceDelay = _completionController.GetDebounceDelayMilliseconds(request.Prefix);
			await Task.Delay(debounceDelay, request.Token);
			if (string.IsNullOrWhiteSpace(request.Prefix) && !request.AllowEmptyPrefix)
			{
				if (ShouldKeepLocalizedCompletionVisible())
				{
					return;
				}

				await Dispatcher.UIThread.InvokeAsync((Action)HideCompletionPopup, DispatcherPriority.Background);
				return;
			}
			Stopwatch stopwatch = Stopwatch.StartNew();
			IReadOnlyList<CompletionItem> items = await ViewModel.BuildCompletionItemsAsync(
				request.Prefix,
				request.Connection,
				request.Schema,
				request.Context,
				request.ResolvedObjectName,
				request.SingleRelationName,
				request.AllowEmptyPrefix,
				request.RelationReferences,
				request.Qualifier,
				request.Token);
			stopwatch.Stop();
			await Dispatcher.UIThread.InvokeAsync(delegate
			{
				if (items.Count == 0 && ShouldKeepLocalizedCompletionVisible())
				{
					AppendUiLog($"CompletionRefresh: prefix={ToCompletionLogValue(request.Prefix)}; context={request.Context}; schema={request.Schema}; items=0; keptVisible=True; elapsedMs={stopwatch.ElapsedMilliseconds}");
					return;
				}

				// Old requests are allowed to finish, but they must not repaint the popup.
				if (_completionController.IsCurrentRefresh(request.Token, request.Sequence) &&
				    ViewModel.ApplyCompletionItems(items) &&
				    ViewModel.IsCompletionOpen)
				{
					UpdateCompletionPopupPosition();
				}
			}, DispatcherPriority.Background);
			if (stopwatch.ElapsedMilliseconds >= 120 || ContainsLocalizedText(request.Prefix) || !string.Equals(request.Context, "generic", StringComparison.OrdinalIgnoreCase))
			{
				AppendUiLog($"CompletionRefresh: prefix={ToCompletionLogValue(request.Prefix)}; context={request.Context}; schema={request.Schema}; items={items.Count}; elapsedMs={stopwatch.ElapsedMilliseconds}");
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private bool ShouldKeepLocalizedCompletionVisibleForKey(Key key)
	{
		return (key == Key.Space || key == Key.Back || key == Key.Delete) && ShouldKeepLocalizedCompletionVisible();
	}

	private bool ShouldKeepLocalizedCompletionVisible()
	{
		return DateTime.UtcNow <= _localizedCompletionKeepAliveUntilUtc;
	}

	private void ApplyCompletion()
	{
		CompletionItem? selectedCompletionItem = ViewModel.SelectedCompletionItem;
		if (selectedCompletionItem != null)
		{
			string text = EditorTextBox.Text ?? string.Empty;
			int caretOffset = EditorTextBox.CaretOffset;
			var (updated, nextCaret) = _completionController.ApplyCompletion(text, caretOffset, selectedCompletionItem);
			RunWithoutCompletion(delegate
			{
				EditorTextBox.Text = updated;
				EditorTextBox.CaretOffset = nextCaret;
			});
			ViewModel.RegisterCompletionUsage(selectedCompletionItem);
			ViewModel.IsCompletionOpen = false;
			_lastCompletionPopupCaretOffset = -1;
			FocusEditor();
		}
	}

	private void InsertTextAtCaret(string textToInsert, bool suppressCompletionPopup = false)
	{
		string text = EditorTextBox.Text ?? string.Empty;
		int selectionStart = Math.Max(EditorTextBox.SelectionStart, 0);
		int selectionLength = Math.Max(EditorTextBox.SelectionLength, 0);
		if (selectionStart > text.Length)
		{
			selectionStart = text.Length;
		}
		string updated = ((selectionLength > 0 && selectionStart + selectionLength <= text.Length) ? text.Remove(selectionStart, selectionLength).Insert(selectionStart, textToInsert) : text.Insert(selectionStart, textToInsert));
		if (suppressCompletionPopup)
		{
			RunWithoutCompletion(delegate
			{
				EditorTextBox.Text = updated;
				EditorTextBox.CaretOffset = selectionStart + textToInsert.Length;
			});
		}
		else
		{
			EditorTextBox.Text = updated;
			EditorTextBox.CaretOffset = selectionStart + textToInsert.Length;
		}
		FocusEditor();
	}

	private string GetSelectedTextOrAll()
	{
		string selectedText = GetSelectedText();
		return string.IsNullOrWhiteSpace(selectedText) ? (EditorTextBox.Text ?? string.Empty) : selectedText;
	}

	private (string Sql, int BaseOffset) GetSqlForExecution()
	{
		string selectedText = GetSelectedText();
		if (string.IsNullOrWhiteSpace(selectedText))
		{
			return (EditorTextBox.Text ?? string.Empty, 0);
		}

		string fullText = EditorTextBox.Text ?? string.Empty;
		int baseOffset = Math.Clamp(EditorTextBox.SelectionStart, 0, fullText.Length);
		return (selectedText, baseOffset);
	}

	private string GetSelectedText()
	{
		return EditorTextBox.SelectedText ?? string.Empty;
	}

	private void ReplaceSelectionOrAll(string replacement, bool replaceAllWhenNoSelection = true)
	{
		string text = EditorTextBox.Text ?? string.Empty;
		int selectionStart = Math.Clamp(EditorTextBox.SelectionStart, 0, text.Length);
		int selectedLength = Math.Max(EditorTextBox.SelectionLength, 0);
		if (selectedLength > 0 && selectionStart <= text.Length)
		{
			int selectionLength = Math.Min(selectedLength, text.Length - selectionStart);
			string replacedText = _searchController.ReplaceRange(text, selectionStart, selectionLength, replacement);
			EditorTextBox.Text = replacedText;
			int caretStart = Math.Clamp(selectionStart, 0, replacedText.Length);
			int replacementLength = Math.Min(replacement.Length, Math.Max(replacedText.Length - caretStart, 0));
			EditorTextBox.Select(caretStart, replacementLength);
		}
		else if (replaceAllWhenNoSelection)
		{
			EditorTextBox.Text = replacement;
			EditorTextBox.CaretOffset = replacement.Length;
		}
	}

	private void UpdateCompletionPopupPosition()
	{
		if (CompletionPopup == null || !ViewModel.IsCompletionOpen)
		{
			_lastCompletionPopupCaretOffset = -1;
			return;
		}
		int caretOffset = EditorTextBox.CaretOffset;
		Vector scrollOffset = EditorTextBox.TextArea.TextView.ScrollOffset;
		if (_lastCompletionPopupCaretOffset == caretOffset &&
		    _lastCompletionPopupScrollOffset == scrollOffset &&
		    CompletionPopup.IsVisible)
		{
			return;
		}
		try
		{
			EditorTextBox.TextArea.TextView.EnsureVisualLines();
			Rect rect = EditorTextBox.TextArea.Caret.CalculateCaretRectangle();
			Visual? visual = CompletionOverlay ?? CompletionPopup.Parent as Visual;
			Point caretPoint = new Point(
				rect.X - scrollOffset.X,
				rect.Bottom - scrollOffset.Y);
			Point popupOrigin = ((visual == null) ? caretPoint : (EditorTextBox.TextArea.TextView.TranslatePoint(caretPoint, visual) ?? EditorTextBox.TranslatePoint(new Point(0.0, 0.0), visual) ?? new Point(18.0, 18.0)));
			Rect parentBounds = visual?.Bounds ?? base.Bounds;
			SetCompletionPopupCanvasPosition(_completionController.CalculatePopupMargin(popupOrigin, parentBounds));
			_lastCompletionPopupCaretOffset = caretOffset;
			_lastCompletionPopupScrollOffset = scrollOffset;
		}
		catch
		{
			string text = EditorTextBox.Text ?? string.Empty;
			caretOffset = Math.Clamp(caretOffset, 0, text.Length);
			Visual? visual = CompletionOverlay ?? CompletionPopup.Parent as Visual;
			Point editorOrigin = visual == null
				? new Point()
				: EditorTextBox.TranslatePoint(new Point(0.0, 0.0), visual) ?? new Point();
			Rect parentBounds = visual?.Bounds ?? base.Bounds;
			Thickness fallbackMargin = _completionController.CalculateFallbackPopupMargin(text, caretOffset, EditorTextBox.Bounds);
			SetCompletionPopupCanvasPosition(ClampCompletionPopupMargin(
				new Thickness(editorOrigin.X + fallbackMargin.Left - scrollOffset.X, editorOrigin.Y + fallbackMargin.Top - scrollOffset.Y, 0, 0),
				parentBounds));
			_lastCompletionPopupCaretOffset = caretOffset;
			_lastCompletionPopupScrollOffset = scrollOffset;
		}
	}
	private void SetCompletionPopupCanvasPosition(Thickness margin)
	{
		CompletionPopup.Margin = new Thickness(0);
		CompletionPopup.HorizontalAlignment = HorizontalAlignment.Left;
		CompletionPopup.VerticalAlignment = VerticalAlignment.Top;
		Canvas.SetLeft(CompletionPopup, margin.Left);
		Canvas.SetTop(CompletionPopup, margin.Top);
	}
	private static Thickness ClampCompletionPopupMargin(Thickness margin, Rect bounds)
	{
		double width = bounds.Width <= 0 ? 900 : bounds.Width;
		double height = bounds.Height <= 0 ? 500 : bounds.Height;
		double left = Math.Clamp(margin.Left, 12, Math.Max(width - 380, 12));
		double top = Math.Clamp(margin.Top, 12, Math.Max(height - 280, 12));
		return new Thickness(left, top, 0, 0);
	}

	private void CommitConnectionEditor()
	{
		_connectionCenterController.CommitEditorPassword(ViewModel, PasswordTextBox.Text);
	}

	private void SyncConnectionEditor()
	{
		if (ViewModel.ConnectionEditorDraft == null)
		{
			PasswordTextBox.Text = string.Empty;
			ProviderComboBox.SelectedItem = null;
		}
		else
		{
			ConnectionEditorState state = _connectionCenterController.BuildEditorState(ViewModel.ConnectionEditorDraft, ViewModel.Providers);
			ApplyConnectionEditorState(state);
		}
	}

	private void ApplyConnectionEditorState(ConnectionEditorState state)
	{
		_isSyncingConnectionEditor = true;
		try
		{
			PasswordTextBox.Text = state.Password;
			ProviderComboBox.SelectedItem = state.SelectedProvider;
			if (OracleModeComboBox != null)
			{
				OracleModeComboBox.SelectedItem = state.OracleConnectionMode;
			}
		}
		finally
		{
			_isSyncingConnectionEditor = false;
		}
	}

	private void FocusEditor()
	{
		EditorTextBox.Focus();
	}

	private void TryLocateExecutionError(QueryExecutionErrorInfo? errorInfo)
	{
		if (errorInfo == null || EditorTextBox.Document == null)
		{
			return;
		}

		string text = EditorTextBox.Text ?? string.Empty;
		if (text.Length == 0)
		{
			return;
		}

		int offset = errorInfo.AbsoluteOffset >= 0 ? errorInfo.AbsoluteOffset : errorInfo.StatementStartOffset;
		offset = Math.Clamp(offset, 0, text.Length);
		int selectionLength = ResolveErrorSelectionLength(text, offset);
		EditorTextBox.Select(offset, selectionLength);
		EditorTextBox.CaretOffset = offset;
		ScrollEditorToOffset(offset);
		FocusEditor();
	}

	private void ScrollEditorToOffset(int offset)
	{
		try
		{
			DocumentLine line = EditorTextBox.Document.GetLineByOffset(Math.Clamp(offset, 0, EditorTextBox.Document.TextLength));
			EditorTextBox.ScrollToLine(line.LineNumber);
			EditorTextBox.TextArea.Caret.BringCaretToView();
		}
		catch
		{
		}
	}

	private static int ResolveErrorSelectionLength(string text, int offset)
	{
		if (offset >= text.Length)
		{
			return 0;
		}

		int length = 0;
		for (int index = offset; index < text.Length && length < 64; index++, length++)
		{
			if (char.IsWhiteSpace(text[index]) || text[index] == ';')
			{
				break;
			}
		}

		return Math.Max(1, length);
	}

	private void SyncEditorFromDocument()
	{
		if (!ViewModel.SelectedDocumentUsesTextEditor)
		{
			return;
		}
		string documentText = ViewModel.SelectedDocument?.Content ?? string.Empty;
		if (!string.Equals(EditorTextBox.Text ?? string.Empty, documentText, StringComparison.Ordinal))
		{
			RunWithoutCompletion(delegate
			{
				EditorTextBox.Text = documentText;
			});
		}
		int caretOffset = Math.Clamp(ViewModel.SelectedDocument?.CaretOffset ?? 0, 0, (EditorTextBox.Text ?? string.Empty).Length);
		EditorTextBox.CaretOffset = caretOffset;
	}

	private void CloseSearchPanel()
	{
		ApplySearchPanelState(_searchPanelController.Close(ViewModel));
	}

	private void HandleSearchPanelKeyDown(KeyEventArgs e, bool replaceBox)
	{
		switch (_searchPanelController.GetKeyAction(e.Key, replaceBox))
		{
		case SearchPanelKeyAction.FindNext:
			FindNext();
			e.Handled = true;
			break;
		case SearchPanelKeyAction.ReplaceCurrent:
			ReplaceCurrent();
			e.Handled = true;
			break;
		case SearchPanelKeyAction.Close:
			CloseSearchPanel();
			e.Handled = true;
			break;
		}
	}

	private void ApplySearchPanelState(SearchPanelState state)
	{
		switch (state.FocusTarget)
		{
		case SearchPanelFocusTarget.Search:
			SearchTextBox.Focus();
			SearchTextBox.SelectAll();
			break;
		case SearchPanelFocusTarget.Replace:
			ReplaceTextBox.Focus();
			ReplaceTextBox.SelectAll();
			break;
		case SearchPanelFocusTarget.Editor:
			FocusEditor();
			break;
		}
	}

	private void HideCompletionPopup()
	{
		CancelCompletionRefresh();
		if (ViewModel.IsCompletionOpen)
		{
			ViewModel.IsCompletionOpen = false;
		}
		_lastCompletionPopupCaretOffset = -1;
		_lastCompletionPopupScrollOffset = default;
	}

	private bool ShouldForceTextEditorHighlight(MainWindowViewModel viewModel)
	{
		string text = viewModel.SelectedDocument?.DocumentKind ?? string.Empty;
		bool result = !string.Equals(_lastTextEditorDocumentKind, text, StringComparison.OrdinalIgnoreCase);
		_lastTextEditorDocumentKind = text;
		return result;
	}

	private void ApplySyntaxHighlightingForCurrentDocument(bool force = false, string? editorText = null)
	{
		if (!ViewModel.SelectedDocumentUsesTextEditor || _textMateInstallation == null)
		{
			return;
		}
		// Language detection only needs a small probe; reading a long script here is visible while typing.
		string text = editorText ?? GetEditorLanguageProbeText();
		string probeSignature = BuildLanguageProbeSignature(text);
		if (!force && string.Equals(probeSignature, _lastHighlightProbeSignature, StringComparison.Ordinal))
		{
			return;
		}
		_lastHighlightProbeSignature = probeSignature;
		string detectedExtension = EditorLanguageDetector.DetectExtension(text);
		if (force || !string.Equals(detectedExtension, _currentHighlightExtension, StringComparison.OrdinalIgnoreCase))
		{
			Language languageByExtension = _registryOptions.GetLanguageByExtension(detectedExtension);
			if (languageByExtension != null)
			{
				string scopeByLanguageId = _registryOptions.GetScopeByLanguageId(languageByExtension.Id);
				_textMateInstallation.GetType().GetMethod("SetGrammar")?.Invoke(_textMateInstallation, new object[1] { scopeByLanguageId });
				_currentHighlightExtension = detectedExtension;
			}
		}
	}

	private string GetEditorLanguageProbeText()
	{
		if (EditorTextBox.Document == null || EditorTextBox.Document.TextLength == 0)
		{
			return string.Empty;
		}

		int length = Math.Min(EditorTextBox.Document.TextLength, 4096);
		return EditorTextBox.Document.GetText(0, length);
	}

	private void CancelCompletionRefresh()
	{
		_completionRefreshDelayCancellationTokenSource?.Cancel();
		_completionRefreshDelayCancellationTokenSource?.Dispose();
		_completionRefreshDelayCancellationTokenSource = null;
		_pendingCompletionRefreshText = null;
		_completionController.CancelRefresh();
	}

	private void SelectResultWorkspaceTab(bool preferPlan = false)
	{
		MainWindowViewModel? mainWindowViewModel = TryGetViewModel();
		if (mainWindowViewModel == null)
		{
			return;
		}
		if (preferPlan && mainWindowViewModel.SelectedDocumentHasExecutionPlan)
		{
			mainWindowViewModel.SelectedDocumentSelectedWorkspaceTab = mainWindowViewModel.SelectedDocumentWorkspaceTabs.FirstOrDefault((ResultWorkspaceTabItem item) => item.IsPlanTab);
		}
		else if (mainWindowViewModel.SelectedDocumentHasTabularResult)
		{
			mainWindowViewModel.SelectedDocumentSelectedWorkspaceTab = mainWindowViewModel.SelectedDocumentWorkspaceTabs.FirstOrDefault((ResultWorkspaceTabItem item) => item.IsResultTab);
		}
		else if (mainWindowViewModel.SelectedDocumentHasMessage)
		{
			mainWindowViewModel.SelectedDocumentSelectedWorkspaceTab = mainWindowViewModel.SelectedDocumentWorkspaceTabs.FirstOrDefault((ResultWorkspaceTabItem item) => item.IsMessageTab);
		}
		else if (mainWindowViewModel.SelectedDocumentHasExecutionPlan)
		{
			mainWindowViewModel.SelectedDocumentSelectedWorkspaceTab = mainWindowViewModel.SelectedDocumentWorkspaceTabs.FirstOrDefault((ResultWorkspaceTabItem item) => item.IsPlanTab);
		}
	}

	private void UpdateResultWorkspaceVisibility()
	{
		if (EditorWorkspaceGrid == null || EditorWorkspaceGrid.RowDefinitions.Count < 5)
		{
			return;
		}
		RowDefinition rowDefinition = EditorWorkspaceGrid.RowDefinitions[3];
		RowDefinition rowDefinition2 = EditorWorkspaceGrid.RowDefinitions[4];
		bool wasVisible = _lastResultWorkspaceVisible;
		bool shouldShow = ViewModel.SelectedDocumentShouldShowResultWorkspace;
		if (shouldShow)
		{
			double currentHeight = GetCurrentResultWorkspaceHeight(rowDefinition2);
			rowDefinition.Height = new GridLength(5.0);
			if (!wasVisible || currentHeight <= 0.0)
			{
				double val = ((_lastResultWorkspaceHeight <= 0.0) ? 260.0 : _lastResultWorkspaceHeight);
				rowDefinition2.Height = new GridLength(Math.Max(val, 170.0));
			}
			else
			{
				// 结果区已经显示时只记录用户拖动后的高度，不再覆盖分隔条调整结果。
				_lastResultWorkspaceHeight = Math.Max(currentHeight, 170.0);
			}
			_lastResultWorkspaceVisible = true;
			return;
		}
		double hiddenHeight = GetCurrentResultWorkspaceHeight(rowDefinition2);
		if (hiddenHeight > 0.0)
		{
			_lastResultWorkspaceHeight = Math.Max(hiddenHeight, 170.0);
		}
		_lastResultWorkspaceVisible = false;
		rowDefinition.Height = new GridLength(0.0);
		rowDefinition2.Height = new GridLength(0.0);
	}

	private static double GetCurrentResultWorkspaceHeight(RowDefinition rowDefinition)
	{
		if (!double.IsNaN(rowDefinition.ActualHeight) && !double.IsInfinity(rowDefinition.ActualHeight) && rowDefinition.ActualHeight > 0.0)
		{
			return rowDefinition.ActualHeight;
		}
		return (!double.IsNaN(rowDefinition.Height.Value) && !double.IsInfinity(rowDefinition.Height.Value) && rowDefinition.Height.Value > 0.0) ? rowDefinition.Height.Value : 0.0;
	}
	private void ClearRenderedResultGrid()
	{
		_resultRenderCancellationTokenSource?.Cancel();
		_resultRenderRequestVersion++;
		EndInlineCellEdit();
		ViewModel.SetSelectedDocumentRendering(isRendering: false);
		ResultFixedHeaderHost?.Children.Clear();
		ResultFixedRowsHost?.Children.Clear();
		ResultHeaderHost?.Children.Clear();
		ResultRowsHost?.Children.Clear();
		_resultWorkspaceController.Reset();
		ViewModel.ClearSelectedDocumentValueDetail();
		ResultHorizontalScrollViewer?.ScrollToHome();
		ResultBodyScrollViewer?.ScrollToHome();
		ResultFixedBodyScrollViewer?.ScrollToHome();
		UpdateResultVerticalScrollBar();
	}

	private async Task RenderSelectedResultGridAsync()
	{
		if (ResultHeaderHost == null || ResultRowsHost == null || ResultFixedHeaderHost == null || ResultFixedRowsHost == null)
		{
			return;
		}
		_resultRenderCancellationTokenSource?.Cancel();
		_resultRenderCancellationTokenSource?.Dispose();
		_resultRenderCancellationTokenSource = new CancellationTokenSource();
		CancellationToken cancellationToken = _resultRenderCancellationTokenSource.Token;
		int renderRequestVersion = ++_resultRenderRequestVersion;
		Stopwatch renderStopwatch = Stopwatch.StartNew();
		try
		{
			ResultSetViewItem? resultSet = ViewModel.SelectedDocumentSelectedResultSet;
			if (resultSet == null || resultSet.IsMessageOnly || resultSet.Columns.Count == 0)
			{
				AppendUiLog($"RenderSelectedResultGrid:no-tabular-result; elapsedMs={renderStopwatch.ElapsedMilliseconds}; hasResults={ViewModel.SelectedDocumentHasResults}; hasMessage={ViewModel.SelectedDocumentHasMessage}");
				return;
			}
			ResultFixedHeaderHost.Children.Clear();
			ResultFixedRowsHost.Children.Clear();
			ResultHeaderHost.Children.Clear();
			ResultRowsHost.Children.Clear();
			EndInlineCellEdit();
			_resultWorkspaceController.Reset();
			ViewModel.ClearSelectedDocumentValueDetail();
			ViewModel.SetSelectedDocumentRendering(isRendering: true);
			bool resetScrollOnRender = ViewModel.ConsumeSelectedDocumentResultScrollResetPending();
			IReadOnlyList<ResultRowViewItem> visibleRows = resultSet.GetViewRows();
			Stopwatch layoutStopwatch = Stopwatch.StartNew();
			double[] columnWidths = _resultWorkspaceController.BuildColumnWidths(resultSet, visibleRows, ViewModel.FormatResultColumnHeaderBody);
			double actionColumnWidth = ((columnWidths.Length != 0) ? columnWidths[0] : 18.0);
			double[] scrollableColumnWidths = columnWidths.Skip(1).ToArray();
			bool hasHeaderSubtitle = resultSet.Columns.Any((ResultColumnViewItem column) => !string.IsNullOrWhiteSpace(ViewModel.FormatResultColumnHeaderBottom(column)));
			ResultFixedHeaderHost.Children.Add(_resultWorkspaceController.BuildFixedActionHeader(actionColumnWidth, hasHeaderSubtitle));
			ResultHeaderHost.Children.Add(_resultWorkspaceController.BuildScrollableHeaderRow(resultSet, scrollableColumnWidths, ViewModel.FormatResultColumnHeaderTop, ViewModel.FormatResultColumnHeaderBottom, ViewModel.BuildResultColumnHeaderTooltip, ResultHeaderCell_PointerPressed, (int columnIndex) => _resultWorkspaceController.BuildHeaderContextMenu(columnIndex, CopyResultHeaderMenuItem_Click, PinResultHeaderMenuItem_Click)));
			layoutStopwatch.Stop();
			if (visibleRows.Count == 0)
			{
				ResultFixedRowsHost.Children.Add(new Border
				{
					Width = actionColumnWidth,
					Height = 54.0,
					Background = Brush.Parse("#F8FAFC"),
					BorderBrush = Brush.Parse("#E5EAF1"),
					BorderThickness = new Thickness(0.0, 0.0, 1.0, 1.0)
				});
				ResultRowsHost.Children.Add(new Border
				{
					Padding = new Thickness(12.0, 18.0),
					Child = new TextBlock
					{
						Text = ViewModel.UiText.EmptyFilteredData,
						Foreground = Brush.Parse("#64748B")
					}
				});
			}
			Stopwatch rowsStopwatch = Stopwatch.StartNew();
			for (int index = 0; index < visibleRows.Count; index++)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					AppendUiLog($"RenderSelectedResultGrid:cancelled; elapsedMs={renderStopwatch.ElapsedMilliseconds}; renderedRows={index}");
					return;
				}
				ResultRowViewItem row = visibleRows[index];
				Border actionBorder = _resultWorkspaceController.BuildFixedActionRow(row, actionColumnWidth, (ResultRowViewItem targetRow) => _resultWorkspaceController.BuildActionContextMenu(resultSet, targetRow, CopySelectedRowsMenuItem_Click, CopySelectedRowsWithHeaderMenuItem_Click, CopySelectedRowsAsInsertMenuItem_Click, ExportResultCsvButton_Click, ExportResultJsonButton_Click, DeleteSelectedRowsMenuItem_Click));
				Border rowBorder = _resultWorkspaceController.BuildScrollableDataRow(row, resultSet, scrollableColumnWidths, (ResultCellContext cell) => _resultWorkspaceController.BuildCellContextMenu(cell, CopyCellMenuItem_Click), EditableResultCellTextBox_TextChanged);
				_resultWorkspaceController.RegisterRowBorder(row, rowBorder);
				ResultFixedRowsHost.Children.Add(actionBorder);
				ResultRowsHost.Children.Add(rowBorder);
				if ((index + 1) % 40 == 0)
				{
					// Large previews are built in slices so the rest of the window can keep repainting.
					await Task.Yield();
				}
			}
			rowsStopwatch.Stop();
			if (resetScrollOnRender)
			{
				ResultHorizontalScrollViewer?.ScrollToHome();
				ResultBodyScrollViewer?.ScrollToHome();
				ResultFixedBodyScrollViewer?.ScrollToHome();
			}
			UpdateResultVerticalScrollBar();
			AppendUiLog($"RenderSelectedResultGrid:complete; elapsedMs={renderStopwatch.ElapsedMilliseconds}; layoutMs={layoutStopwatch.ElapsedMilliseconds}; rowsMs={rowsStopwatch.ElapsedMilliseconds}; columns={resultSet.Columns.Count}; rows={visibleRows.Count}; totalRows={resultSet.Rows.Count}; hasResults={ViewModel.SelectedDocumentHasResults}");
		}
		finally
		{
			if (renderRequestVersion == _resultRenderRequestVersion)
			{
				ViewModel.SetSelectedDocumentRendering(isRendering: false);
			}
		}
	}

	private static Control BuildDialogHeader(string iconKind, string title)
	{
		Grid header = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,*"),
			ColumnSpacing = 10.0
		};
		header.Children.Add(BuildDialogIcon(iconKind));
		TextBlock titleBlock = new TextBlock
		{
			Text = title,
			FontSize = 16.0,
			FontWeight = FontWeight.SemiBold,
			Foreground = Brush.Parse("#255E91"),
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetColumn(titleBlock, 1);
		header.Children.Add(titleBlock);
		return header;
	}

	private static Control BuildDialogIcon(string iconKind)
	{
		return new Border
		{
			Width = 38.0,
			Height = 38.0,
			CornerRadius = new CornerRadius(12.0),
			Background = Brush.Parse("#EAF5FF"),
			BorderBrush = Brush.Parse("#BBD8F2"),
			BorderThickness = new Thickness(1.0),
			Child = new BlueIcon
			{
				Kind = iconKind,
				Width = 22.0,
				Height = 22.0,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			}
		};
	}

	private static Control BuildIconButtonContent(string iconKind, string text)
	{
		StackPanel panel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		panel.Children.Add(new BlueIcon
		{
			Kind = iconKind,
			Width = 14.0,
			Height = 14.0,
			VerticalAlignment = VerticalAlignment.Center
		});
		panel.Children.Add(new TextBlock
		{
			Text = text,
			VerticalAlignment = VerticalAlignment.Center
		});
		return panel;
	}

	private async Task ShowInfoMessageAsync(string title, string message)
	{
		Window dialog = new Window
		{
			Width = 420.0,
			Height = 180.0,
			Title = title,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			CanResize = false
		};
		StackPanel panel = new StackPanel
		{
			Margin = new Thickness(18.0),
			Spacing = 14.0
		};
		panel.Children.Add(BuildDialogHeader("Info", title));
		panel.Children.Add(new TextBlock
		{
			Text = message,
			TextWrapping = TextWrapping.Wrap
		});
		Button okButton = new Button
		{
			Content = BuildIconButtonContent("Check", ViewModel.UiText.DialogConfirm),
			HorizontalAlignment = HorizontalAlignment.Right,
			MinWidth = 72.0
		};
		okButton.Click += delegate
		{
			dialog.Close();
		};
		panel.Children.Add(okButton);
		dialog.Content = panel;
		TopLevel? topLevel = TopLevel.GetTopLevel(this);
		if (topLevel is Window owner)
		{
			await dialog.ShowDialog(owner);
		}
		else
		{
			dialog.Show();
		}
	}

	private void ResultRowBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (sender is Border { Tag: ResultRowViewItem resultRow })
		{
			FocusResultInteractionHost();
			if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
			{
				_resultWorkspaceController.PrepareRowContextSelection(resultRow);
				return;
			}
			e.Pointer.Capture(this);
			EndInlineCellEdit();
			_resultWorkspaceController.BeginSelection(resultRow);
			ViewModel.ClearSelectedDocumentValueDetail();
			e.Handled = true;
		}
	}

	private void ResultHeaderCell_PointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (sender is Border border && border.Tag is int columnIndex)
		{
			FocusResultInteractionHost();
			ResultSetViewItem? selectedDocumentSelectedResultSet = ViewModel.SelectedDocumentSelectedResultSet;
			if (selectedDocumentSelectedResultSet != null)
			{
				EndInlineCellEdit();
				_resultWorkspaceController.SelectColumn(selectedDocumentSelectedResultSet, columnIndex);
				ViewModel.ClearSelectedDocumentValueDetail();
				e.Handled = true;
			}
		}
	}

	private void ResultRowActionCell_PointerEntered(object? sender, PointerEventArgs e)
	{
		if (sender is Border { Tag: ResultRowViewItem tag })
		{
			ResultSetViewItem? selectedDocumentSelectedResultSet = ViewModel.SelectedDocumentSelectedResultSet;
			if (selectedDocumentSelectedResultSet != null)
			{
				EndInlineCellEdit();
				_resultWorkspaceController.UpdateDragSelection(selectedDocumentSelectedResultSet, tag, e.GetCurrentPoint(this).Properties.IsLeftButtonPressed);
			}
		}
	}

	private void ResultRowActionCell_PointerMoved(object? sender, PointerEventArgs e)
	{
		if (sender is Border { Tag: ResultRowViewItem tag })
		{
			ResultSetViewItem? selectedDocumentSelectedResultSet = ViewModel.SelectedDocumentSelectedResultSet;
			if (selectedDocumentSelectedResultSet != null)
			{
				EndInlineCellEdit();
				_resultWorkspaceController.UpdateDragSelection(selectedDocumentSelectedResultSet, tag, e.GetCurrentPoint(this).Properties.IsLeftButtonPressed);
				e.Handled = true;
			}
		}
	}

	private void ResultRowActionCell_PointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		e.Pointer.Capture(null);
		_resultWorkspaceController.EndSelection();
	}

	private void ResultCellBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (sender is Border { Tag: ResultCellContext tag } border)
		{
			FocusResultInteractionHost();
			e.Pointer.Capture(this);
			ResultSetViewItem? selectedDocumentSelectedResultSet = ViewModel.SelectedDocumentSelectedResultSet;
			if (selectedDocumentSelectedResultSet != null)
			{
				_resultWorkspaceController.BeginCellSelection(selectedDocumentSelectedResultSet, tag);
				UpdateValueDetailForCell(selectedDocumentSelectedResultSet, tag);
				BeginInlineCellEdit(selectedDocumentSelectedResultSet, tag);
			}
			e.Handled = true;
		}
	}

	private void ResultCellBorder_PointerEntered(object? sender, PointerEventArgs e)
	{
		if (sender is Border { Tag: ResultCellContext tag })
		{
			ResultSetViewItem? selectedDocumentSelectedResultSet = ViewModel.SelectedDocumentSelectedResultSet;
			if (selectedDocumentSelectedResultSet != null)
			{
				_resultWorkspaceController.UpdateCellSelection(selectedDocumentSelectedResultSet, tag, e.GetCurrentPoint(this).Properties.IsLeftButtonPressed);
				if (_resultWorkspaceController.IsDraggingCellSelection)
				{
					UpdateValueDetailForCell(selectedDocumentSelectedResultSet, tag);
				}
			}
		}
	}

	private void ResultCellBorder_PointerMoved(object? sender, PointerEventArgs e)
	{
		if (sender is Border { Tag: ResultCellContext tag })
		{
			ResultSetViewItem? selectedDocumentSelectedResultSet = ViewModel.SelectedDocumentSelectedResultSet;
			if (selectedDocumentSelectedResultSet != null)
			{
				_resultWorkspaceController.UpdateCellSelection(selectedDocumentSelectedResultSet, tag, e.GetCurrentPoint(this).Properties.IsLeftButtonPressed);
				if (_resultWorkspaceController.IsDraggingCellSelection)
				{
					UpdateValueDetailForCell(selectedDocumentSelectedResultSet, tag);
				}
				e.Handled = true;
			}
		}
	}

	private void ResultCellBorder_PointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		e.Pointer.Capture(null);
		_resultWorkspaceController.EndSelection();
	}

	private async void CopyCellMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		if (_resultWorkspaceController.HasCellSelection)
		{
			await CopySelectedCellsToClipboardAsync();
			return;
		}

		if (sender is MenuItem { Tag: ResultCellContext context })
		{
			TopLevel? topLevel = TopLevel.GetTopLevel(this);
			if (topLevel?.Clipboard != null)
			{
				await topLevel.Clipboard.SetTextAsync(_resultWorkspaceController.BuildCellClipboardText(context));
			}
		}
	}

	private async void CopyValueDetailButton_Click(object? sender, RoutedEventArgs e)
	{
		string? value = ViewModel.SelectedDocumentValueDetail?.ValueText;
		if (string.IsNullOrEmpty(value))
		{
			return;
		}

		TopLevel? topLevel = TopLevel.GetTopLevel(this);
		if (topLevel?.Clipboard != null)
		{
			await topLevel.Clipboard.SetTextAsync(value);
		}
	}

	private void OpenValueDetailButton_Click(object? sender, RoutedEventArgs e)
	{
		ViewModel.OpenSelectedDocumentValueDetailPanel();
	}

	private void CloseValueDetailButton_Click(object? sender, RoutedEventArgs e)
	{
		ViewModel.CloseSelectedDocumentValueDetailPanel();
	}

	private void EditableResultCellTextBox_TextChanged(object? sender, TextChangedEventArgs e)
	{
		if (sender is TextBox { Tag: ResultCellContext { ColumnIndex: >=0 } tag } textBox && tag.ColumnIndex < tag.Row.Values.Count)
		{
			tag.Row.Values[tag.ColumnIndex] = textBox.Text ?? string.Empty;
			ViewModel.RefreshSelectedResultEditState();
		}
	}

	private async void CopyResultHeaderMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		if (sender is not MenuItem { Tag: int columnIndex })
		{
			return;
		}

		ResultSetViewItem? resultSet = ViewModel.SelectedDocumentSelectedResultSet;
		if (resultSet != null && columnIndex >= 0 && columnIndex < resultSet.Columns.Count)
		{
			TopLevel? topLevel = TopLevel.GetTopLevel(this);
			if (topLevel?.Clipboard != null)
			{
				await topLevel.Clipboard.SetTextAsync(ViewModel.FormatResultColumnHeaderTop(resultSet.Columns[columnIndex]));
			}
		}
	}

	private void PinResultHeaderMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		if (sender is MenuItem { Tag: int columnIndex })
		{
			ResultSetViewItem? selectedDocumentSelectedResultSet = ViewModel.SelectedDocumentSelectedResultSet;
			if (selectedDocumentSelectedResultSet != null)
			{
				selectedDocumentSelectedResultSet.PinnedColumnIndex = selectedDocumentSelectedResultSet.PinnedColumnIndex == columnIndex ? null : columnIndex;
				_ = RenderSelectedResultGridAsync();
			}
		}
	}

	private async void CopyResultMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		if (!_resultWorkspaceController.HasSelection)
		{
			return;
		}
		TopLevel? topLevel = TopLevel.GetTopLevel(this);
		if (topLevel?.Clipboard != null)
		{
			ResultSetViewItem? resultSet = ViewModel.SelectedDocumentSelectedResultSet;
			if (resultSet != null)
			{
				string text = _resultWorkspaceController.BuildSelectionClipboardText(resultSet, includeHeader: false);
				await topLevel.Clipboard.SetTextAsync(text);
			}
		}
	}

	private async void CopySelectedRowsMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		if (sender is MenuItem { Tag: ResultRowViewItem row })
		{
			_resultWorkspaceController.EnsureRowIncludedInSelection(row);
		}
		await CopySelectedRowsToClipboardAsync(includeHeader: false);
	}

	private async void CopySelectedRowsWithHeaderMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ResultRowViewItem? row = null;
		if (sender is MenuItem { Tag: ResultRowViewItem taggedRow })
		{
			row = taggedRow;
		}

		if (row != null)
		{
			_resultWorkspaceController.EnsureRowIncludedInSelection(row);
		}

		await CopySelectedRowsToClipboardAsync(includeHeader: true);
	}

	private async void CopySelectedRowsAsInsertMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ResultRowViewItem? fallbackRow = null;
		if (sender is MenuItem { Tag: ResultRowViewItem row })
		{
			_resultWorkspaceController.EnsureRowIncludedInSelection(row);
			fallbackRow = row;
		}
		ResultSetViewItem? resultSet = ViewModel.SelectedDocumentSelectedResultSet;
		if (resultSet == null)
		{
			return;
		}
		if (!_resultWorkspaceController.TryBuildInsertScript(resultSet, out string script, out string errorMessage, fallbackRow))
		{
			await MessageBoxAsync(ViewModel.UiText.CopyInsertTitle, errorMessage);
			return;
		}
		TopLevel? topLevel = TopLevel.GetTopLevel(this);
		if (topLevel?.Clipboard != null)
		{
			await topLevel.Clipboard.SetTextAsync(script);
		}
	}

	private async void DeleteSelectedRowsMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ResultRowViewItem? row = (sender as MenuItem)?.Tag as ResultRowViewItem;
		if (row != null)
		{
			_resultWorkspaceController.EnsureRowIncludedInSelection(row);
		}
		ResultSetViewItem? resultSet = ViewModel.SelectedDocumentSelectedResultSet;
		if (resultSet == null || !resultSet.CanDeleteRows)
		{
			return;
		}
		ResultWorkspaceController resultWorkspaceController = _resultWorkspaceController;
		IReadOnlyList<ResultRowViewItem> rows = resultWorkspaceController.GetEffectiveSelection(row);
		if (rows.Count == 0 || !(await ConfirmAsync(ViewModel.UiText.DeleteResultRowsTitle, string.Format(CultureInfo.CurrentCulture, ViewModel.UiText.DeleteResultRowsConfirmFormat, rows.Count))))
		{
			return;
		}
		try
		{
			EditableResultMutationResult? result = await ViewModel.DeleteSelectedResultRowsAsync(rows);
			if (result != null)
			{
				AppendUiLog($"EditableResult:delete; summary={result.Summary}; affected={result.AffectedRows}");
			}
			await RenderSelectedResultGridAsync();
		}
		catch (Exception ex)
		{
			await MessageBoxAsync(ViewModel.UiText.DeleteResultRowsTitle, ex.Message);
		}
	}

	private async Task CopySelectedRowsToClipboardAsync(bool includeHeader)
	{
		if (!_resultWorkspaceController.HasSelection)
		{
			return;
		}
		ResultSetViewItem? resultSet = ViewModel.SelectedDocumentSelectedResultSet;
		if (resultSet != null)
		{
			TopLevel? topLevel = TopLevel.GetTopLevel(this);
			if (topLevel?.Clipboard != null)
			{
				string text = _resultWorkspaceController.BuildSelectionClipboardText(resultSet, includeHeader);
				await topLevel.Clipboard.SetTextAsync(text);
			}
		}
	}

	private async Task CopySelectedCellsToClipboardAsync()
	{
		ResultSetViewItem? resultSet = ViewModel.SelectedDocumentSelectedResultSet;
		if (resultSet == null)
		{
			return;
		}
		TopLevel? topLevel = TopLevel.GetTopLevel(this);
		if (topLevel?.Clipboard != null)
		{
			string text = _resultWorkspaceController.BuildSelectedCellClipboardText(resultSet);
			if (!string.IsNullOrEmpty(text))
			{
				await topLevel.Clipboard.SetTextAsync(text);
			}
		}
	}

	private async void CopyResultWithHeaderMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		await CopySelectedRowsToClipboardAsync(includeHeader: true);
	}

	private async void CopyAllResultButton_Click(object? sender, RoutedEventArgs e)
	{
		ResultSetViewItem? resultSet = ViewModel.SelectedDocumentSelectedResultSet;
		if (!(resultSet?.IsMessageOnly ?? true))
		{
			TopLevel? topLevel = TopLevel.GetTopLevel(this);
			if (topLevel?.Clipboard != null)
			{
				await topLevel.Clipboard.SetTextAsync(_resultWorkspaceController.BuildFullResultClipboardText(resultSet, includeHeader: false, ViewModel.FormatResultColumnHeader));
			}
		}
	}

	private async void CopyAllResultWithHeaderButton_Click(object? sender, RoutedEventArgs e)
	{
		ResultSetViewItem? resultSet = ViewModel.SelectedDocumentSelectedResultSet;
		if (!(resultSet?.IsMessageOnly ?? true))
		{
			TopLevel? topLevel = TopLevel.GetTopLevel(this);
			if (topLevel?.Clipboard != null)
			{
				await topLevel.Clipboard.SetTextAsync(_resultWorkspaceController.BuildFullResultClipboardText(resultSet, includeHeader: true, ViewModel.FormatResultColumnHeader));
			}
		}
	}

	private async void ExportResultCsvButton_Click(object? sender, RoutedEventArgs e)
	{
		ResultSetViewItem? resultSet = ViewModel.SelectedDocumentSelectedResultSet;
		if (!(resultSet?.IsMessageOnly ?? true))
		{
			await SaveTextToFileAsync(content: _resultWorkspaceController.BuildCsv(resultSet, ViewModel.FormatResultColumnHeader), suggestedFileName: resultSet.Name + ".csv");
		}
	}

	private async void ExportResultJsonButton_Click(object? sender, RoutedEventArgs e)
	{
		ResultSetViewItem? resultSet = ViewModel.SelectedDocumentSelectedResultSet;
		if (!(resultSet?.IsMessageOnly ?? true))
		{
			await SaveTextToFileAsync(content: _resultWorkspaceController.BuildJson(resultSet, ViewModel.FormatResultColumnHeader), suggestedFileName: resultSet.Name + ".json");
		}
	}

	private async void DesignTableMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		if (node != null)
		{
			await _explorerActionController.OpenTableDesignAsync(TableDesignCoordinator, this, node);
		}
	}

	private async void OpenObjectDetailsMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		ObjectNode? node = _explorerActionController.TryGetNode(sender);
		if (node != null)
		{
			await ObjectDetailsCoordinator.OpenAsync(this, node);
		}
	}

	private void ExpandConnectedRootNode()
	{
		Dispatcher.UIThread.Post(delegate
		{
			foreach (TreeViewItem item in ExplorerTreeView.GetVisualDescendants().OfType<TreeViewItem>())
			{
				if (item.DataContext is ObjectNode { Type: "connection", IsConnected: not false })
				{
					item.IsExpanded = true;
				}
			}
		}, DispatcherPriority.Background);
	}

	private void ExpandExplorerNode(string? nodeKey)
	{
		if (string.IsNullOrWhiteSpace(nodeKey))
		{
			return;
		}
		Dispatcher.UIThread.Post(delegate
		{
			foreach (TreeViewItem item in ExplorerTreeView.GetVisualDescendants().OfType<TreeViewItem>())
			{
				if (item.DataContext is ObjectNode objectNode && string.Equals(objectNode.Key, nodeKey, StringComparison.OrdinalIgnoreCase))
				{
					item.IsExpanded = true;
					break;
				}
			}
		}, DispatcherPriority.Background);
	}

	private HashSet<string> CaptureExpandedExplorerNodeKeys()
	{
		HashSet<string> expandedNodeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (TreeViewItem item in ExplorerTreeView.GetVisualDescendants().OfType<TreeViewItem>())
		{
			if (item.IsExpanded && item.DataContext is ObjectNode objectNode && !string.IsNullOrWhiteSpace(objectNode.Key))
			{
				objectNode.IsExpanded = true;
				expandedNodeKeys.Add(objectNode.Key);
			}
		}
		return expandedNodeKeys;
	}

	private void RestoreExplorerExpansion(IEnumerable<string> expandedKeys, params string?[] forcedExpandedKeys)
	{
		HashSet<string> keysToRestore = new HashSet<string>(expandedKeys.Where((string key) => !string.IsNullOrWhiteSpace(key)).Cast<string>(), StringComparer.OrdinalIgnoreCase);
		foreach (string? forcedKey in forcedExpandedKeys)
		{
			if (!string.IsNullOrWhiteSpace(forcedKey))
			{
				keysToRestore.Add(forcedKey);
			}
		}
		if (keysToRestore.Count != 0)
		{
			RestoreExplorerExpansionPass(keysToRestore, 4);
		}
	}

	private void RestoreExplorerExpansionPass(HashSet<string> expandedKeys, int remainingPasses)
	{
		if (remainingPasses <= 0 || expandedKeys.Count == 0)
		{
			return;
		}
		Dispatcher.UIThread.Post(delegate
		{
			int restoredCount = 0;
			foreach (TreeViewItem item in ExplorerTreeView.GetVisualDescendants().OfType<TreeViewItem>())
			{
				if (item.DataContext is ObjectNode objectNode && !string.IsNullOrWhiteSpace(objectNode.Key) && expandedKeys.Contains(objectNode.Key))
				{
					item.IsExpanded = true;
					objectNode.IsExpanded = true;
					restoredCount++;
				}
			}
			if (restoredCount < expandedKeys.Count)
			{
				RestoreExplorerExpansionPass(expandedKeys, remainingPasses - 1);
			}
		}, DispatcherPriority.Background);
	}

	private void ApplyEditorWrapMode()
	{
		EditorTextBox.WordWrap = true;
	}

	private async Task MessageBoxAsync(string title, string message)
	{
		Button okButton = new Button
		{
			Content = BuildIconButtonContent("Check", ViewModel.UiText.DialogConfirm),
			HorizontalAlignment = HorizontalAlignment.Right,
			MinWidth = 72.0
		};
		Window dialog = new Window
		{
			Title = title,
			Width = 480.0,
			Height = 220.0,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Content = new StackPanel
			{
				Margin = new Thickness(18.0),
				Spacing = 12.0,
				Children = 
				{
					BuildDialogHeader("Info", title),
					(Control)new TextBlock
					{
						Text = message,
						TextWrapping = TextWrapping.Wrap
					},
					okButton
				}
			}
		};
		okButton.Click += delegate
		{
			dialog.Close();
		};
		await dialog.ShowDialog(this);
	}

	private async Task ShowAboutDialogAsync()
	{
		Button okButton = new Button
		{
			Content = BuildIconButtonContent("Check", ViewModel.UiText.DialogConfirm),
			HorizontalAlignment = HorizontalAlignment.Right,
			MinWidth = 82.0
		};
		Window dialog = new Window
		{
			Title = ViewModel.UiText.About,
			Width = 520.0,
			Height = 330.0,
			MinWidth = 480.0,
			MinHeight = 300.0,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = Brush.Parse("#F8FAFC")
		};
		okButton.Click += delegate
		{
			dialog.Close();
		};
		dialog.Content = new Border
		{
			Padding = new Thickness(22.0),
			Child = new StackPanel
			{
				Spacing = 18.0,
				Children =
				{
					BuildAboutHeader(),
					new Border
					{
						Height = 1.0,
						Background = Brush.Parse("#E2E8F0")
					},
					new TextBlock
					{
						Text = $"{ViewModel.UiText.AboutAuthorLabel}: 长安员外",
						FontSize = 14.0,
						Foreground = Brush.Parse("#334155")
					},
					new TextBlock
					{
						Text = ViewModel.UiText.AboutCopyright,
						FontSize = 13.0,
						Foreground = Brush.Parse("#64748B")
					},
					new TextBlock
					{
						Text = ViewModel.UiText.AboutMessage,
						TextWrapping = TextWrapping.Wrap,
						Foreground = Brush.Parse("#475569"),
						LineHeight = 22.0
					},
					okButton
				}
			}
		};
		await dialog.ShowDialog(this);
	}

	private Control BuildAboutHeader()
	{
		Bitmap? logoBitmap = LoadApplicationLogo();
		Control logo = logoBitmap == null
			? new BlueIcon
			{
				Kind = "Database",
				Width = 82.0,
				Height = 82.0,
				AccentBrush = Brush.Parse("#2563EB")
			}
			: new Image
			{
				Source = logoBitmap,
				Width = 92.0,
				Height = 92.0,
				Stretch = Stretch.Uniform
			};
		return new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 18.0,
			Children =
			{
				new Border
				{
					Width = 104.0,
					Height = 104.0,
					CornerRadius = new CornerRadius(18.0),
					Background = Brush.Parse("#FFFFFF"),
					BorderBrush = Brush.Parse("#E2E8F0"),
					BorderThickness = new Thickness(1.0),
					Child = logo,
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center
				},
				new StackPanel
				{
					VerticalAlignment = VerticalAlignment.Center,
					Spacing = 8.0,
					Children =
					{
						new TextBlock
						{
							Text = ViewModel.UiText.ApplicationTitle,
							FontSize = 26.0,
							FontWeight = FontWeight.SemiBold,
							Foreground = Brush.Parse("#0F172A")
						},
						new TextBlock
						{
							Text = ViewModel.UiText.HeaderSubtitle,
							TextWrapping = TextWrapping.Wrap,
							MaxWidth = 330.0,
							Foreground = Brush.Parse("#64748B"),
							LineHeight = 21.0
						}
					}
				}
			}
		};
	}

	private static Bitmap? LoadApplicationLogo()
	{
		try
		{
			return new Bitmap(AssetLoader.Open(new Uri("avares://QueryPaw/Assets/software-logo.png")));
		}
		catch
		{
			return null;
		}
	}

	private async Task<bool> ConfirmAsync(string title, string message)
	{
		Window dialog = new Window
		{
			Title = title,
			Width = 480.0,
			Height = 220.0,
			WindowStartupLocation = WindowStartupLocation.CenterOwner
		};
		bool confirmed = false;
		Button confirmButton = new Button
		{
			Content = BuildIconButtonContent("Check", ViewModel.UiText.DialogConfirm),
			MinWidth = 72.0
		};
		Button cancelButton = new Button
		{
			Content = BuildIconButtonContent("Error", ViewModel.UiText.Cancel),
			MinWidth = 72.0
		};
		confirmButton.Click += delegate
		{
			confirmed = true;
			dialog.Close();
		};
		cancelButton.Click += delegate
		{
			dialog.Close();
		};
		dialog.Content = new StackPanel
		{
			Margin = new Thickness(18.0),
			Spacing = 12.0,
			Children = 
			{
				BuildDialogHeader("Info", title),
				(Control)new TextBlock
				{
					Text = message,
					TextWrapping = TextWrapping.Wrap
				},
				(Control)new StackPanel
				{
					Orientation = Orientation.Horizontal,
					HorizontalAlignment = HorizontalAlignment.Right,
					Spacing = 8.0,
					Children = 
					{
						(Control)cancelButton,
						(Control)confirmButton
					}
				}
			}
		};
		await dialog.ShowDialog(this);
		return confirmed;
	}
	private async Task SaveTextToFileAsync(string suggestedFileName, string content)
	{
		TopLevel? topLevel = TopLevel.GetTopLevel(this);
		if (topLevel?.StorageProvider != null)
		{
			string? path = (await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
			{
				SuggestedFileName = suggestedFileName,
				ShowOverwritePrompt = true
			}))?.TryGetLocalPath();
			if (!string.IsNullOrWhiteSpace(path))
			{
				await File.WriteAllTextAsync(path, content);
			}
		}
	}
	private async Task OpenDocumentFileAsync(string? fixedPath = null)
	{
		string? path = fixedPath;
		if (string.IsNullOrWhiteSpace(path))
		{
			TopLevel? topLevel = TopLevel.GetTopLevel(this);
			if (topLevel?.StorageProvider == null)
			{
				return;
			}
			path = (await topLevel.StorageProvider.OpenFilePickerAsync(_documentLifecycleController.BuildOpenPickerOptions(ViewModel))).FirstOrDefault()?.TryGetLocalPath();
		}
		if (await _documentLifecycleController.OpenDocumentAsync(ViewModel, path))
		{
			PopulateRecentFilesMenu();
			await ViewModel.EnsureSelectedDocumentContextReadyAsync();
			SyncEditorFromDocument();
			ApplySyntaxHighlightingForCurrentDocument(force: true);
		}
	}
	private async Task SaveSelectedDocumentAsync(bool saveAs)
	{
		if (ViewModel.SelectedDocument == null)
		{
			return;
		}
		string? path = ViewModel.SelectedDocument.FilePath;
		if (saveAs || string.IsNullOrWhiteSpace(path))
		{
			TopLevel? topLevel = TopLevel.GetTopLevel(this);
			if (topLevel?.StorageProvider == null)
			{
				return;
			}
			path = (await topLevel.StorageProvider.SaveFilePickerAsync(_documentLifecycleController.BuildSavePickerOptions(ViewModel, path, ViewModel.SelectedDocument.Title, saveAs)))?.TryGetLocalPath();
		}
		if (!string.IsNullOrWhiteSpace(path))
		{
			string content = EditorTextBox.Text ?? string.Empty;
			if (await _documentLifecycleController.SaveDocumentAsync(ViewModel, path, content))
			{
				PopulateRecentFilesMenu();
				await ViewModel.PersistAsync();
			}
		}
	}
	private void PopulateRecentFilesMenu()
	{
		if (RecentFilesMenuItem == null)
		{
			return;
		}
		RecentFilesMenuItem.Items.Clear();
		foreach (RecentFileMenuItemModel item in _documentLifecycleController.BuildRecentFileMenuItems(ViewModel))
		{
			MenuItem menuItem = new MenuItem
			{
				Header = item.Header,
				Tag = item.FilePath,
				IsEnabled = item.IsEnabled
			};
			if (!string.IsNullOrWhiteSpace(item.FilePath))
			{
				menuItem.Click += RecentFileMenuItem_Click;
			}
			RecentFilesMenuItem.Items.Add(menuItem);
		}
	}
	private async void RecentFileMenuItem_Click(object? sender, RoutedEventArgs e)
	{
		string? path = _documentLifecycleController.TryGetRecentFilePath(sender);
		if (!string.IsNullOrWhiteSpace(path))
		{
			await OpenDocumentFileAsync(path);
		}
	}
	private async void MainWindow_Activated(object? sender, EventArgs e)
	{
		if (await _documentLifecycleController.ReloadExternallyChangedDocumentsAsync(ViewModel, AppendUiLog))
		{
			SyncEditorFromDocument();
			ApplySyntaxHighlightingForCurrentDocument(force: true);
		}
	}
	private void RunWithoutCompletion(Action action)
	{
		_suppressCompletionDepth++;
		try
		{
			action();
		}
		finally
		{
			_suppressCompletionDepth--;
		}
	}
	private bool IsEditorPointerEvent(PointerEventArgs e)
	{
		if (!(e.Source is Visual visual))
		{
			return IsPointWithinVisual(EditorTextBox, e.GetPosition(this));
		}
		return visual.GetSelfAndVisualAncestors().Any((Visual item) => item == EditorTextBox || item == EditorTextBox.TextArea || item == EditorTextBox.TextArea.TextView) || IsPointWithinVisual(EditorTextBox, e.GetPosition(this));
	}
	private static bool IsResultEditorPointerEvent(PointerEventArgs e)
	{
		return e.Source is TextBox;
	}
	private bool IsPointWithinVisual(Visual? visual, Point point)
	{
		return IsPointWithinVisual(visual, point, this);
	}
	private bool IsPointWithinVisual(Visual? visual, Point point, Visual relativeTo)
	{
		if (visual == null)
		{
			return false;
		}
		Point? point2 = visual.TranslatePoint(new Point(0.0, 0.0), relativeTo);
		if (!point2.HasValue)
		{
			return false;
		}
		Rect rect = new Rect(point2.Value, visual.Bounds.Size);
		return rect.Contains(point);
	}
	private void ResultBodyScrollViewer_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
	{
		if (!(sender is ScrollViewer scrollViewer))
		{
			return;
		}
		if (e.Property == ScrollViewer.ExtentProperty || e.Property == ScrollViewer.ViewportProperty)
		{
			UpdateResultVerticalScrollBar(scrollViewer);
			return;
		}
		if (e.Property != ScrollViewer.OffsetProperty || _synchronizingResultVerticalScroll)
		{
			return;
		}
		ScrollViewer? synchronizedScrollViewer = scrollViewer == ResultBodyScrollViewer ? ResultFixedBodyScrollViewer : scrollViewer == ResultFixedBodyScrollViewer ? ResultBodyScrollViewer : null;
		if (synchronizedScrollViewer == null)
		{
			return;
		}
		_synchronizingResultVerticalScroll = true;
		try
		{
			synchronizedScrollViewer.Offset = new Vector(synchronizedScrollViewer.Offset.X, scrollViewer.Offset.Y);
			UpdateResultVerticalScrollBar(scrollViewer);
		}
		finally
		{
			_synchronizingResultVerticalScroll = false;
		}
	}
	private void ResultVerticalScrollBar_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
	{
		if (_synchronizingResultVerticalScrollBar || ResultBodyScrollViewer == null)
		{
			return;
		}
		_synchronizingResultVerticalScrollBar = true;
		try
		{
			double nextY = Math.Clamp(e.NewValue, 0.0, Math.Max(0.0, ResultBodyScrollViewer.Extent.Height - ResultBodyScrollViewer.Viewport.Height));
			ResultBodyScrollViewer.Offset = new Vector(ResultBodyScrollViewer.Offset.X, nextY);
			if (ResultFixedBodyScrollViewer != null)
			{
				ResultFixedBodyScrollViewer.Offset = new Vector(ResultFixedBodyScrollViewer.Offset.X, nextY);
			}
		}
		finally
		{
			_synchronizingResultVerticalScrollBar = false;
		}
	}
	private void UpdateResultVerticalScrollBar(ScrollViewer? source = null)
	{
		if (ResultVerticalScrollBar == null || ResultBodyScrollViewer == null || _synchronizingResultVerticalScrollBar)
		{
			return;
		}
		ScrollViewer scrollViewer = source ?? ResultBodyScrollViewer;
		double viewportHeight = Math.Max(0.0, scrollViewer.Viewport.Height);
		double maxOffset = Math.Max(0.0, scrollViewer.Extent.Height - viewportHeight);
		_synchronizingResultVerticalScrollBar = true;
		try
		{
			ResultVerticalScrollBar.Minimum = 0.0;
			ResultVerticalScrollBar.Maximum = maxOffset;
			ResultVerticalScrollBar.ViewportSize = viewportHeight;
			ResultVerticalScrollBar.LargeChange = Math.Max(24.0, viewportHeight * 0.85);
			ResultVerticalScrollBar.SmallChange = 24.0;
			ResultVerticalScrollBar.Value = Math.Clamp(scrollViewer.Offset.Y, 0.0, maxOffset);
			ResultVerticalScrollBar.IsEnabled = maxOffset > 0.5;
		}
		finally
		{
			_synchronizingResultVerticalScrollBar = false;
		}
	}
	private static string BuildLanguageProbeSignature(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return string.Empty;
		}

		StringBuilder builder = new StringBuilder(Math.Min(text.Length, 1024));
		bool hasContent = false;
		for (int i = 0; i < text.Length && builder.Length < 1024; i++)
		{
			char current = text[i];
			if (current == '\r')
			{
				continue;
			}

			if (!char.IsWhiteSpace(current))
			{
				hasContent = true;
			}

			builder.Append(current);
		}

		return hasContent ? builder.ToString() : string.Empty;
	}

}
