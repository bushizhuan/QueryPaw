using System;
using System.Collections.Generic;
using System.Linq;
using SqlAnalyzer.App.Models;

namespace SqlAnalyzer.App.Services;

public static class SqlCompletionKeywordProvider
{
    private readonly record struct CompletionKeyword(string Text, int Priority, string Description, string Kind = "keyword");

    private static readonly CompletionKeyword[] CommonKeywords =
    {
        new("select", 0, "Query rows from a table"),
        new("from", 1, "Specify source table"),
        new("where", 2, "Filter rows"),
        new("delete from", 3, "Delete rows from a table"),
        new("update", 4, "Update table rows"),
        new("set", 5, "Assign updated values"),
        new("insert into", 6, "Insert new rows"),
        new("values", 7, "Provide inserted values"),
        new("join", 8, "Join related tables"),
        new("left join", 9, "Left outer join"),
        new("inner join", 10, "Inner join"),
        new("right join", 11, "Right outer join"),
        new("group by", 12, "Group rows"),
        new("order by", 13, "Sort rows"),
        new("having", 14, "Filter grouped rows"),
        new("distinct", 15, "Return unique rows"),
        new("count", 16, "Aggregate row count", "function"),
        new("sum", 17, "Aggregate sum", "function"),
        new("avg", 18, "Aggregate average", "function"),
        new("min", 19, "Aggregate minimum", "function"),
        new("max", 20, "Aggregate maximum", "function"),
        new("case", 21, "Conditional expression"),
        new("when", 22, "CASE condition"),
        new("then", 23, "CASE branch result"),
        new("else", 24, "CASE fallback result"),
        new("end", 25, "Close CASE block"),
        new("and", 26, "Logical and"),
        new("or", 27, "Logical or"),
        new("not", 28, "Logical not"),
        new("exists", 29, "Exists predicate"),
        new("in", 30, "Membership predicate"),
        new("between", 31, "Range predicate"),
        new("like", 32, "Pattern predicate"),
        new("is null", 33, "Null predicate"),
        new("is not null", 34, "Not-null predicate"),
        new("union", 35, "Combine result sets"),
        new("union all", 36, "Combine without deduplication"),
        new("with", 37, "Common table expression"),
        new("merge into", 38, "Merge matched rows"),
        new("truncate table", 39, "Remove all rows"),
        new("create table", 40, "Create table"),
        new("alter table", 41, "Alter table definition"),
        new("drop table", 42, "Drop table"),
        new("create view", 43, "Create view"),
        new("create materialized view", 44, "Create materialized view"),
        new("create index", 45, "Create index"),
        new("create sequence", 46, "Create sequence"),
        new("create procedure", 47, "Create procedure"),
        new("create function", 48, "Create function"),
        new("primary key", 49, "Primary key constraint"),
        new("foreign key", 50, "Foreign key constraint"),
        new("unique", 51, "Unique constraint"),
        new("not null", 52, "Not-null constraint"),
        new("partition by", 53, "Window partition clause"),
        new("over", 54, "Window function clause"),
        new("row_number", 55, "Window function", "function"),
        new("dense_rank", 56, "Window ranking function", "function"),
        new("lead", 57, "Window lead function", "function"),
        new("lag", 58, "Window lag function", "function"),
        new("substr", 59, "Substring function", "function"),
        new("coalesce", 60, "Return first non-null value", "function"),
        new("current_timestamp", 61, "Current timestamp", "function"),
        new("commit", 62, "Commit transaction"),
        new("rollback", 63, "Rollback transaction")
    };

    private static readonly CompletionKeyword[] OracleFamilyKeywords =
    [
        new("rownum", 53, "Oracle pseudo column for row limiting", "pseudo-column"),
        new("comment on", 54, "Comment database objects"),
        new("explain plan", 55, "Generate execution plan"),
        new("dual", 56, "Oracle single-row helper table"),
        new("nvl", 57, "Oracle null replacement", "function"),
        new("decode", 58, "Oracle decode function", "function"),
        new("sysdate", 59, "Oracle current date", "pseudo-column"),
        new("systimestamp", 60, "Oracle current timestamp", "pseudo-column"),
        new("to_char", 61, "Convert value to string", "function"),
        new("to_date", 62, "Convert value to date", "function"),
        new("connect by", 63, "Oracle hierarchical query clause"),
        new("start with", 64, "Oracle hierarchical root clause"),
        new("level", 65, "Oracle hierarchy depth pseudo column", "pseudo-column"),
        new("row_number", 140, "Oracle analytic window function", "function")
    ];

    private static readonly CompletionKeyword[] SqlServerKeywords =
    [
        new("top", 53, "SQL Server row limiting clause"),
        new("isnull", 54, "SQL Server null replacement function", "function"),
        new("getdate", 55, "SQL Server current datetime", "function"),
        new("nvarchar", 56, "SQL Server Unicode string type"),
        new("create or alter", 57, "SQL Server create-or-update object statement"),
        new("with(nolock)", 58, "SQL Server table hint")
    ];

    private static readonly CompletionKeyword[] PostgreSqlFamilyKeywords =
    [
        new("limit", 53, "PostgreSQL row limiting clause"),
        new("offset", 54, "PostgreSQL row offset clause"),
        new("returning", 55, "Return changed rows"),
        new("ilike", 56, "Case-insensitive pattern predicate"),
        new("serial", 57, "PostgreSQL auto-increment type"),
        new("comment on", 58, "Comment database objects"),
        new("to_char", 59, "Convert value to string", "function")
    ];

    private static readonly CompletionKeyword[] MySqlKeywords =
    [
        new("limit", 53, "MySQL row limiting clause"),
        new("show create table", 54, "Show table DDL"),
        new("ifnull", 55, "MySQL null replacement function", "function"),
        new("replace into", 56, "MySQL replace rows statement"),
        new("explain", 57, "Explain execution plan")
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<CompletionKeyword>> ProviderProfiles =
        BuildProviderProfiles();

    public static IEnumerable<CompletionItem> BuildItems(string normalizedPrefix, string completionContext, string? providerName)
    {
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
        {
            return Array.Empty<CompletionItem>();
        }

        if (IsRelationContext(completionContext) || string.Equals(completionContext, "member-column", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<CompletionItem>();
        }

        return from keyword in GetProfile(providerName)
            where CanShowInContext(keyword, completionContext)
            where keyword.Text.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
                  (normalizedPrefix.Length >= 2 && keyword.Text.Contains(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            select new CompletionItem
            {
                DisplayText = keyword.Text,
                InsertText = keyword.Text,
                Kind = keyword.Kind,
                Description = keyword.Description,
                MatchKeys = new[] { keyword.Text },
                SourceObject = keyword.Text,
                SortWeight = keyword.Priority
            };
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<CompletionKeyword>> BuildProviderProfiles()
    {
        return new Dictionary<string, IReadOnlyList<CompletionKeyword>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Oracle"] = Merge(CommonKeywords, OracleFamilyKeywords),
            ["Dameng"] = Merge(CommonKeywords, OracleFamilyKeywords),
            ["SqlServer"] = Merge(CommonKeywords, SqlServerKeywords),
            ["PostgreSql"] = Merge(CommonKeywords, PostgreSqlFamilyKeywords),
            ["KingbaseES"] = Merge(CommonKeywords, PostgreSqlFamilyKeywords),
            ["MySql"] = Merge(CommonKeywords, MySqlKeywords)
        };
    }

    private static IReadOnlyList<CompletionKeyword> Merge(
        IEnumerable<CompletionKeyword> commonKeywords,
        IEnumerable<CompletionKeyword> providerKeywords)
    {
        Dictionary<string, CompletionKeyword> merged = new(StringComparer.OrdinalIgnoreCase);
        foreach (CompletionKeyword keyword in commonKeywords)
        {
            merged[keyword.Text] = keyword;
        }

        foreach (CompletionKeyword keyword in providerKeywords)
        {
            merged[keyword.Text] = keyword;
        }

        return merged.Values
            .OrderBy(static item => item.Priority)
            .ThenBy(static item => item.Text, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<CompletionKeyword> GetProfile(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return CommonKeywords;
        }

        return ProviderProfiles.TryGetValue(providerName, out IReadOnlyList<CompletionKeyword>? keywords)
            ? keywords
            : CommonKeywords;
    }

    private static bool CanShowInContext(CompletionKeyword keyword, string completionContext)
    {
        if (!IsColumnContext(completionContext))
        {
            return true;
        }

        return string.Equals(keyword.Kind, "function", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(keyword.Kind, "pseudo-column", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRelationContext(string completionContext)
    {
        return string.Equals(completionContext, "relation", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsColumnContext(string completionContext)
    {
        return string.Equals(completionContext, "column", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(completionContext, "select-column", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(completionContext, "member-column", StringComparison.OrdinalIgnoreCase);
    }
}
