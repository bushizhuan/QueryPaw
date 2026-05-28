using System;
using System.Linq;
using SqlAnalyzer.App.Models;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public static class EditableResultMutationBuilder
{
    public static EditableResultMutationColumn[] BuildColumns(ResultSetViewItem resultSet)
    {
        return resultSet.Columns
            .Select((column, index) => new EditableResultMutationColumn
            {
                Index = index,
                HeaderName = column.RawName,
                SourceSchema = column.SourceSchema,
                SourceTable = column.SourceTable,
                SourceColumn = column.SourceColumn,
                DataTypeName = column.DataTypeName,
                ClrTypeName = column.ClrTypeName,
                IsPrimaryKey = column.IsPrimaryKey,
                IsEditable = column.IsEditable
            })
            .ToArray();
    }

    public static EditableResultRowMutation[] BuildChangedRows(ResultSetViewItem resultSet)
    {
        return resultSet.Rows
            .Select((row, index) => new { row, index })
            .Where(static item => item.row.HasChanges)
            .Select(item => new EditableResultRowMutation
            {
                RowIndex = item.index,
                // 更新时带上原值，服务层才能拼出更保守的 where 条件。
                OriginalValues = item.row.OriginalValues.ToArray(),
                OriginalDisplayValues = item.row.OriginalDisplayValues.ToArray(),
                CurrentValues = item.row.Values.ToArray()
            })
            .ToArray();
    }

    public static EditableResultRowMutation? BuildRow(ResultSetViewItem resultSet, ResultRowViewItem row)
    {
        int rowIndex = resultSet.Rows.IndexOf(row);
        if (rowIndex < 0)
        {
            return null;
        }

        return new EditableResultRowMutation
        {
            RowIndex = rowIndex,
            OriginalValues = row.OriginalValues.ToArray(),
            OriginalDisplayValues = row.OriginalDisplayValues.ToArray(),
            CurrentValues = row.Values.ToArray()
        };
    }

    public static string ResolveSchema(DocumentExecutionState state, ResultSetViewItem resultSet)
    {
        string selectedSchema = NormalizeSchemaSelection(state.SelectedSchema);
        if (!string.IsNullOrWhiteSpace(selectedSchema))
        {
            return selectedSchema;
        }

        return resultSet.BaseSchemaName ?? string.Empty;
    }

    private static string NormalizeSchemaSelection(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema) || string.Equals(schema, "(Default)", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return schema.Trim();
    }
}
