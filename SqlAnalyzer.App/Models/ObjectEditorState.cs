using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Models;
public sealed class ObjectEditorState : ObservableObject
{
    private string _connectionName = string.Empty;
    private string _providerName = string.Empty;
    private string _schemaName = string.Empty;
    private string _objectName = string.Empty;
    private string _displayName = string.Empty;
    private string _objectType = string.Empty;
    private string _commentText = string.Empty;
    private string _returnType = string.Empty;
    private string _originalDefinition = string.Empty;
    private string _previewSql = string.Empty;
    private string _messageText = string.Empty;
    private ObjectEditorCapability _capability;
    private bool _isLoaded;
    private bool _isBusy;

    public string ConnectionName
    {
        get => _connectionName;
        set => SetProperty(ref _connectionName, value);
    }

    public string ProviderName
    {
        get => _providerName;
        set => SetProperty(ref _providerName, value);
    }

    public string SchemaName
    {
        get => _schemaName;
        set => SetProperty(ref _schemaName, value);
    }

    public string ObjectName
    {
        get => _objectName;
        set => SetProperty(ref _objectName, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string ObjectType
    {
        get => _objectType;
        set => SetProperty(ref _objectType, value);
    }

    public string CommentText
    {
        get => _commentText;
        set => SetProperty(ref _commentText, value);
    }

    public string ReturnType
    {
        get => _returnType;
        set => SetProperty(ref _returnType, value);
    }

    public string OriginalDefinition
    {
        get => _originalDefinition;
        set => SetProperty(ref _originalDefinition, value);
    }

    public string PreviewSql
    {
        get => _previewSql;
        set => SetProperty(ref _previewSql, value);
    }

    public string MessageText
    {
        get => _messageText;
        set => SetProperty(ref _messageText, value);
    }

    public ObjectEditorCapability Capability
    {
        get => _capability;
        set
        {
            if (SetProperty(ref _capability, value))
            {
                OnPropertyChanged(nameof(CapabilityText));
                OnPropertyChanged(nameof(IsReadOnly));
            }
        }
    }

    public bool IsLoaded
    {
        get => _isLoaded;
        set => SetProperty(ref _isLoaded, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public ObservableCollection<ObjectParameterDefinition> Parameters { get; } = [];

    public ObservableCollection<ObjectCompileMessage> CompileMessages { get; } = [];

    public bool IsReadOnly => Capability != ObjectEditorCapability.Editable;

    public string CapabilityText => Capability switch
    {
        ObjectEditorCapability.Editable => "可编辑",
        ObjectEditorCapability.PreviewOnly => "仅预览",
        _ => "不支持"
    };

    public string CompileResultText =>
        CompileMessages.Count == 0
            ? string.Empty
            : string.Join(
                Environment.NewLine,
                CompileMessages.Select(item =>
                    item.Line > 0
                        ? $"[{item.Severity}] 第 {item.Line} 行 第 {item.Column} 列: {item.Message}"
                        : $"[{item.Severity}] {item.Message}"));
    public void LoadFromModel(string connectionName, ObjectEditorModel model)
    {
        ConnectionName = connectionName;
        ProviderName = model.ProviderName;
        SchemaName = model.SchemaName;
        ObjectName = model.ObjectName;
        DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? model.ObjectName : model.DisplayName;
        ObjectType = model.ObjectType;
        CommentText = model.CommentText;
        ReturnType = model.ReturnType;
        OriginalDefinition = model.OriginalDefinition;
        Capability = model.Capability;
        PreviewSql = string.Empty;
        MessageText = "对象定义已加载。";

        Parameters.Clear();
        foreach (ObjectParameterDefinition parameter in model.Parameters)
        {
            Parameters.Add(parameter);
        }

        CompileMessages.Clear();
        IsLoaded = true;
        IsBusy = false;
        OnPropertyChanged(nameof(CompileResultText));
    }
    public void SetPreviewSql(string sql)
    {
        PreviewSql = sql;
        MessageText = string.IsNullOrWhiteSpace(sql) ? "当前没有可预览的 SQL。" : "已生成 SQL 预览。";
    }
    public void SetExecutionResult(string message, IReadOnlyList<ObjectCompileMessage>? compileMessages)
    {
        MessageText = message ?? string.Empty;
        CompileMessages.Clear();
        if (compileMessages != null)
        {
            foreach (ObjectCompileMessage item in compileMessages)
            {
                CompileMessages.Add(item);
            }
        }

        OnPropertyChanged(nameof(CompileResultText));
    }
    public void MarkSaved(string savedDefinition)
    {
        OriginalDefinition = savedDefinition ?? string.Empty;
    }
}
