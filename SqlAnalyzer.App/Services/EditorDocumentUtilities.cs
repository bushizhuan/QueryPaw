using System;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public static class EditorDocumentUtilities
{
    public static EditorDocument Clone(EditorDocument source)
    {
        return new EditorDocument
        {
            Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id,
            Title = string.IsNullOrWhiteSpace(source.Title) ? "Query" : source.Title,
            DocumentKind = string.IsNullOrWhiteSpace(source.DocumentKind) ? "Query" : source.DocumentKind,
            WorkspaceKey = source.WorkspaceKey ?? string.Empty,
            FilePath = source.FilePath ?? string.Empty,
            ConnectionProfileId = source.ConnectionProfileId ?? string.Empty,
            DefaultSchema = source.DefaultSchema ?? string.Empty,
            ObjectSchemaName = source.ObjectSchemaName ?? string.Empty,
            ObjectRawName = source.ObjectRawName ?? string.Empty,
            ObjectDisplayName = source.ObjectDisplayName ?? string.Empty,
            ObjectType = source.ObjectType ?? string.Empty,
            ModelSchemaName = source.ModelSchemaName ?? string.Empty,
            ModelFocusTableName = source.ModelFocusTableName ?? string.Empty,
            Content = source.Content ?? string.Empty,
            CaretOffset = source.CaretOffset,
            ScrollOffsetX = source.ScrollOffsetX,
            ScrollOffsetY = source.ScrollOffsetY,
            IsDirty = source.IsDirty,
            LastFileWriteTimeUtc = source.LastFileWriteTimeUtc
        };
    }
}
