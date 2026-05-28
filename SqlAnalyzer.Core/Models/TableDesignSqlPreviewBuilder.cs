using System.Text;

namespace SqlAnalyzer.Core.Models;

public static class TableDesignSqlPreviewBuilder
{
    public static string Build(TableDesignModel design)
    {
        StringBuilder builder = new();
        string qualifiedTableName = BuildQualifiedName(design.ProviderName, design.SchemaName, design.TableName);

        List<string> columnLines = [];
        foreach (TableColumnDefinition column in design.Columns)
        {
            columnLines.Add($"    {BuildColumnDefinitionSql(column)}");
        }

        string primaryKey = string.Join(", ", design.Columns.Where(item => item.IsPrimaryKey).Select(item => NormalizeIdentifier(item.Name)));
        if (!string.IsNullOrWhiteSpace(primaryKey))
        {
            columnLines.Add($"    constraint {NormalizeIdentifier($"PK_{design.TableName}")} primary key ({primaryKey})");
        }

        builder.Append("create table ")
            .Append(qualifiedTableName)
            .AppendLine(" (")
            .Append(string.Join($",{Environment.NewLine}", columnLines))
            .AppendLine()
            .AppendLine(");");

        foreach (TableIndexDefinition uniqueKey in design.UniqueKeys.Where(item => item.IsUnique))
        {
            builder.Append("alter table ")
                .Append(qualifiedTableName)
                .Append(" add constraint ")
                .Append(NormalizeIdentifier(uniqueKey.Name))
                .Append(" unique (")
                .Append(uniqueKey.Columns)
                .AppendLine(");");
        }

        foreach (TableCheckDefinition check in design.Checks.Where(item => !string.IsNullOrWhiteSpace(item.Expression)))
        {
            builder.Append("alter table ")
                .Append(qualifiedTableName)
                .Append(" add constraint ")
                .Append(NormalizeIdentifier(check.Name))
                .Append(" check (")
                .Append(check.Expression)
                .AppendLine(");");
        }

        if (!string.IsNullOrWhiteSpace(design.TableComment))
        {
            builder.Append("comment on table ")
                .Append(qualifiedTableName)
                .Append(" is '")
                .Append(EscapeSqlLiteral(design.TableComment))
                .AppendLine("';");
        }

        foreach (TableColumnDefinition column in design.Columns.Where(item => !string.IsNullOrWhiteSpace(item.Comment)))
        {
            builder.Append("comment on column ")
                .Append(qualifiedTableName)
                .Append('.')
                .Append(NormalizeIdentifier(column.Name))
                .Append(" is '")
                .Append(EscapeSqlLiteral(column.Comment))
                .AppendLine("';");
        }

        return builder.ToString().TrimEnd();
    }
    private static string BuildColumnDefinitionSql(TableColumnDefinition column)
    {
        string type = column.DataType?.Trim() ?? string.Empty;
        if (column.Length.HasValue && column.Length.Value > 0 &&
            !type.Contains("date", StringComparison.OrdinalIgnoreCase) &&
            !type.Contains("time", StringComparison.OrdinalIgnoreCase) &&
            !type.Contains("clob", StringComparison.OrdinalIgnoreCase) &&
            !type.Contains("blob", StringComparison.OrdinalIgnoreCase))
        {
            type += $"({column.Length.Value})";
        }
        else if (column.Precision.HasValue && column.Precision.Value > 0)
        {
            type += column.Scale.HasValue && column.Scale.Value > 0
                ? $"({column.Precision.Value},{column.Scale.Value})"
                : $"({column.Precision.Value})";
        }

        return $"{NormalizeIdentifier(column.Name)} {type}{(column.IsNullable ? string.Empty : " not null")}";
    }
    private static string BuildQualifiedName(string providerName, string schemaName, string tableName)
    {
        if (string.IsNullOrWhiteSpace(schemaName))
        {
            return NormalizeIdentifier(tableName);
        }

        return $"{NormalizeIdentifier(schemaName)}.{NormalizeIdentifier(tableName)}";
    }
    private static string NormalizeIdentifier(string value)
    {
        return value.Replace("\"", string.Empty).Trim();
    }
    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''");
    }
}
