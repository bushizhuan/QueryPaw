using System.Collections.Generic;
using System.Text;
using SqlAnalyzer.Core.Models;

namespace SqlAnalyzer.App.Services;

public static class ModelDiagramRelationCsvBuilder
{
    public static string Build(IReadOnlyList<ModelRelationExportRow> rows)
    {
        StringBuilder builder = new();
        builder.AppendLine("parent_table,parent_comment,child_table,child_comment,parent_columns,child_columns,constraint_name");
        foreach (ModelRelationExportRow row in rows)
        {
            builder.AppendLine(string.Join(",",
                Escape(row.ParentTable),
                Escape(row.ParentComment),
                Escape(row.ChildTable),
                Escape(row.ChildComment),
                Escape(row.ParentColumnsText),
                Escape(row.ChildColumnsText),
                Escape(row.ConstraintName)));
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        string text = value ?? string.Empty;
        if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
        {
            return $"\"{text.Replace("\"", "\"\"", System.StringComparison.Ordinal)}\"";
        }

        return text;
    }
}
