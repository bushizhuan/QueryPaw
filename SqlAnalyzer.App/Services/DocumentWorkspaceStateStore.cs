using System;
using System.Collections.Generic;
using System.ComponentModel;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public sealed class DocumentWorkspaceStateStore
{
    // 文档内容和几个工作台状态分开存，切换 Tab 时各自保留现场。
    private readonly Dictionary<string, DocumentExecutionState> _documentStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CommentMaintenanceWorkspaceState> _commentWorkspaceStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModelDiagramWorkspaceState> _modelDiagramStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ObjectEditorState> _objectEditorStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly PropertyChangedEventHandler _commentWorkspaceChanged;
    private readonly PropertyChangedEventHandler _modelDiagramChanged;
    private readonly PropertyChangedEventHandler _objectEditorChanged;

    public DocumentWorkspaceStateStore(
        PropertyChangedEventHandler commentWorkspaceChanged,
        PropertyChangedEventHandler modelDiagramChanged,
        PropertyChangedEventHandler objectEditorChanged)
    {
        _commentWorkspaceChanged = commentWorkspaceChanged;
        _modelDiagramChanged = modelDiagramChanged;
        _objectEditorChanged = objectEditorChanged;
    }

    public IEnumerable<DocumentExecutionState> DocumentStates => _documentStates.Values;

    public DocumentExecutionState EnsureDocument(
        EditorDocument? document,
        string executionReadyText,
        Func<EditorDocument?, string> resolveInitialSchema)
    {
        if (document == null)
        {
            return new DocumentExecutionState();
        }

        if (!_documentStates.TryGetValue(document.Id, out DocumentExecutionState? state))
        {
            state = new DocumentExecutionState
            {
                ExecutionStatus = executionReadyText,
                SelectedSchema = resolveInitialSchema(document)
            };
            _documentStates[document.Id] = state;
        }
        else if (string.IsNullOrWhiteSpace(state.SelectedSchema))
        {
            // 老会话里可能没有 schema 字段，第一次访问时补上。
            state.SelectedSchema = resolveInitialSchema(document);
        }

        return state;
    }

    public CommentMaintenanceWorkspaceState EnsureComment(EditorDocument? document)
    {
        if (document == null)
        {
            throw new InvalidOperationException("当前没有可用的注释维护工作台。");
        }

        if (!_commentWorkspaceStates.TryGetValue(document.Id, out CommentMaintenanceWorkspaceState? state))
        {
            state = new CommentMaintenanceWorkspaceState();
            state.PropertyChanged += _commentWorkspaceChanged;
            _commentWorkspaceStates[document.Id] = state;
        }

        return state;
    }

    public CommentMaintenanceWorkspaceState? GetSelectedComment(bool isCommentMaintenanceDocument, EditorDocument? selectedDocument)
    {
        return isCommentMaintenanceDocument && selectedDocument != null ? EnsureComment(selectedDocument) : null;
    }

    public ModelDiagramWorkspaceState EnsureModelDiagram(EditorDocument? document)
    {
        if (document == null)
        {
            throw new InvalidOperationException("当前没有可用的数据模型工作台。");
        }

        if (!_modelDiagramStates.TryGetValue(document.Id, out ModelDiagramWorkspaceState? state))
        {
            state = new ModelDiagramWorkspaceState();
            state.PropertyChanged += _modelDiagramChanged;
            _modelDiagramStates[document.Id] = state;
        }

        return state;
    }

    public ModelDiagramWorkspaceState? GetSelectedModelDiagram(bool isModelDiagramDocument, EditorDocument? selectedDocument)
    {
        return isModelDiagramDocument && selectedDocument != null ? EnsureModelDiagram(selectedDocument) : null;
    }

    public ObjectEditorState EnsureObjectEditor(EditorDocument? document)
    {
        if (document == null)
        {
            throw new InvalidOperationException("当前没有可用的对象编辑页。");
        }

        if (!_objectEditorStates.TryGetValue(document.Id, out ObjectEditorState? state))
        {
            state = new ObjectEditorState();
            state.PropertyChanged += _objectEditorChanged;
            _objectEditorStates[document.Id] = state;
        }

        return state;
    }

    public ObjectEditorState? GetSelectedObjectEditor(bool isObjectEditorDocument, EditorDocument? selectedDocument)
    {
        return isObjectEditorDocument && selectedDocument != null ? EnsureObjectEditor(selectedDocument) : null;
    }

    public void RemoveWorkspaceStates(string documentId)
    {
        // 查询页的执行状态可能还要参与会话恢复，这里只清理重型工作台。
        _commentWorkspaceStates.Remove(documentId);
        _modelDiagramStates.Remove(documentId);
        _objectEditorStates.Remove(documentId);
    }
}
