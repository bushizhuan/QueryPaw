using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;

namespace SqlAnalyzer.App.Services;

public static class ResultColumnMetadataEnricher
{
    public static async Task EnrichAsync(
        IDatabaseExplorerService databaseExplorerService,
        IEnumerable<ResultSetViewItem> resultSets,
        ConnectionProfile? profile,
        string schemaName,
        CancellationToken cancellationToken)
    {
        if (profile == null || string.IsNullOrWhiteSpace(schemaName))
        {
            return;
        }

        List<(ResultColumnViewItem Column, string Key)> columns = CollectColumns(resultSets);
        if (columns.Count == 0)
        {
            return;
        }

        IReadOnlyDictionary<string, ResultColumnMetadataEntry> metadata = await databaseExplorerService.LoadResultColumnMetadataLookupAsync(
            profile,
            schemaName,
            columns.Select(item => new ResultColumnMetadataRequest
            {
                Key = item.Key,
                RawName = string.IsNullOrWhiteSpace(item.Column.RawName) ? item.Column.HeaderText : item.Column.RawName,
                SourceSchema = item.Column.SourceSchema,
                SourceTable = item.Column.SourceTable,
                SourceColumn = item.Column.SourceColumn
            }).ToArray(),
            cancellationToken);
        if (metadata.Count == 0)
        {
            return;
        }

        foreach ((ResultColumnViewItem column, string key) in columns)
        {
            if (!metadata.TryGetValue(key, out ResultColumnMetadataEntry? entry))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                column.DisplayName = entry.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(entry.CommentText))
            {
                column.CommentText = entry.CommentText;
            }
        }
    }

    private static List<(ResultColumnViewItem Column, string Key)> CollectColumns(IEnumerable<ResultSetViewItem> resultSets)
    {
        List<(ResultColumnViewItem Column, string Key)> columns = [];
        int resultSetIndex = 0;
        foreach (ResultSetViewItem resultSet in resultSets)
        {
            for (int columnIndex = 0; columnIndex < resultSet.Columns.Count; columnIndex++)
            {
                ResultColumnViewItem column = resultSet.Columns[columnIndex];
                string rawName = string.IsNullOrWhiteSpace(column.RawName) ? column.HeaderText : column.RawName;
                string sourceColumn = string.IsNullOrWhiteSpace(column.SourceColumn) ? rawName : column.SourceColumn;
                if (!string.IsNullOrWhiteSpace(sourceColumn) && !string.Equals(sourceColumn, "Message", StringComparison.OrdinalIgnoreCase))
                {
                    // 用结果集序号加列序号做回填 key，避免同名列互相串备注。
                    columns.Add((column, $"rs{resultSetIndex}:col{columnIndex}"));
                }
            }

            resultSetIndex++;
        }

        return columns;
    }
}
