using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlAnalyzer.Core.Models;

public static class TableColumnTypeCatalog
{
    private static readonly IReadOnlyList<TableColumnTypeOption> SqlServerTypes =
    [
        Numeric("bit", "布尔位"),
        Numeric("tinyint", "8 位整型"),
        Numeric("smallint", "16 位整型"),
        Numeric("int", "32 位整型"),
        Numeric("bigint", "64 位整型"),
        Decimal("decimal", "定点数", 18, 2),
        Decimal("numeric", "定点数", 18, 2),
        Numeric("money", "货币"),
        Numeric("smallmoney", "小范围货币"),
        Numeric("float", "浮点数"),
        Numeric("real", "单精度浮点数"),
        DateTime("date", "日期"),
        Time("time", "时间", 7),
        DateTime("datetime", "日期时间"),
        Time("datetime2", "高精度日期时间", 7),
        DateTime("smalldatetime", "短日期时间"),
        Time("datetimeoffset", "带时区日期时间", 7),
        Character("char", "定长字符", 20),
        Character("varchar", "变长字符", 100),
        Character("text", "长文本"),
        Character("nchar", "Unicode 定长字符", 20),
        Character("nvarchar", "Unicode 变长字符", 100),
        Character("ntext", "Unicode 长文本"),
        Binary("binary", "定长二进制", 16),
        Binary("varbinary", "变长二进制", 64),
        Binary("image", "二进制大对象"),
        Misc("uniqueidentifier", "GUID"),
        Misc("xml", "XML 文本")
    ];

    private static readonly IReadOnlyList<TableColumnTypeOption> OracleTypes =
    [
        Character("CHAR", "定长字符", 20),
        Character("NCHAR", "定长 Unicode 字符", 20),
        Character("VARCHAR2", "变长字符", 100),
        Character("NVARCHAR2", "变长 Unicode 字符", 100),
        Character("CLOB", "大文本"),
        Character("NCLOB", "Unicode 大文本"),
        Character("LONG", "长文本"),
        Decimal("NUMBER", "通用数值类型", 18, 2),
        Numeric("FLOAT", "浮点数"),
        Numeric("BINARY_FLOAT", "单精度浮点数"),
        Numeric("BINARY_DOUBLE", "双精度浮点数"),
        DateTime("DATE", "日期时间"),
        Time("TIMESTAMP", "时间戳", 6),
        Time("TIMESTAMP WITH TIME ZONE", "带时区时间戳", 6),
        Time("TIMESTAMP WITH LOCAL TIME ZONE", "本地时区时间戳", 6),
        Time("INTERVAL YEAR TO MONTH", "年月间隔", 2),
        Time("INTERVAL DAY TO SECOND", "日秒间隔", 6),
        Binary("RAW", "二进制", 32),
        Binary("LONG RAW", "长二进制"),
        Binary("BLOB", "二进制大对象"),
        Misc("BFILE", "外部文件"),
        Misc("XMLTYPE", "XML 类型")
    ];

    private static readonly IReadOnlyList<TableColumnTypeOption> MySqlTypes =
    [
        Numeric("bit", "位类型"),
        Numeric("tinyint", "8 位整型"),
        Numeric("smallint", "16 位整型"),
        Numeric("mediumint", "24 位整型"),
        Numeric("int", "32 位整型"),
        Numeric("bigint", "64 位整型"),
        Decimal("decimal", "定点数", 18, 2),
        Decimal("numeric", "定点数", 18, 2),
        Numeric("float", "单精度浮点数"),
        Numeric("double", "双精度浮点数"),
        DateTime("year", "年份"),
        DateTime("date", "日期"),
        Time("time", "时间", 6),
        Time("datetime", "日期时间", 6),
        Time("timestamp", "时间戳", 6),
        Character("char", "定长字符", 20),
        Character("varchar", "变长字符", 100),
        Binary("binary", "定长二进制", 16),
        Binary("varbinary", "变长二进制", 64),
        Character("tinytext", "短文本"),
        Character("text", "文本"),
        Character("mediumtext", "中等文本"),
        Character("longtext", "长文本"),
        Binary("tinyblob", "短二进制"),
        Binary("blob", "二进制"),
        Binary("mediumblob", "中等二进制"),
        Binary("longblob", "长二进制"),
        Misc("json", "JSON"),
        Misc("enum", "枚举"),
        Misc("set", "集合")
    ];

    private static readonly IReadOnlyList<TableColumnTypeOption> SqliteTypes =
    [
        Numeric("INTEGER", "整型"),
        Numeric("REAL", "浮点数"),
        Decimal("NUMERIC", "数值", 18, 2),
        Character("TEXT", "文本"),
        Binary("BLOB", "二进制"),
        DateTime("DATE", "日期"),
        DateTime("DATETIME", "日期时间"),
        Numeric("BOOLEAN", "布尔")
    ];

    private static readonly IReadOnlyList<TableColumnTypeOption> PostgreSqlTypes =
    [
        Numeric("smallint", "16 位整型"),
        Numeric("integer", "32 位整型"),
        Numeric("bigint", "64 位整型"),
        Decimal("decimal", "定点数", 18, 2),
        Decimal("numeric", "定点数", 18, 2),
        Numeric("real", "单精度浮点数"),
        Misc("double precision", "双精度浮点数"),
        Numeric("smallserial", "自增短整型"),
        Numeric("serial", "自增整型"),
        Numeric("bigserial", "自增长整型"),
        Numeric("money", "货币"),
        Numeric("boolean", "布尔"),
        Character("char", "定长字符", 20),
        Character("character varying", "变长字符", 100),
        Character("text", "文本"),
        Binary("bytea", "二进制"),
        DateTime("date", "日期"),
        Time("time", "时间", 6),
        Time("time with time zone", "带时区时间", 6),
        Time("timestamp", "时间戳", 6),
        Time("timestamp with time zone", "带时区时间戳", 6),
        Time("interval", "时间间隔", 6),
        Misc("uuid", "UUID"),
        Misc("json", "JSON"),
        Misc("jsonb", "二进制 JSON"),
        Misc("xml", "XML"),
        Misc("cidr", "CIDR 网络地址"),
        Misc("inet", "IP 地址"),
        Misc("macaddr", "MAC 地址")
    ];

    private static readonly IReadOnlyList<TableColumnTypeOption> DamengTypes =
    [
        Character("CHAR", "定长字符", 20),
        Character("VARCHAR", "变长字符", 100),
        Character("VARCHAR2", "变长字符", 100),
        Character("NCHAR", "定长 Unicode 字符", 20),
        Character("NVARCHAR", "变长 Unicode 字符", 100),
        Character("CLOB", "大文本"),
        Character("TEXT", "文本"),
        Numeric("TINYINT", "8 位整型"),
        Numeric("SMALLINT", "16 位整型"),
        Numeric("INT", "32 位整型"),
        Numeric("BIGINT", "64 位整型"),
        Numeric("BIT", "位类型"),
        Decimal("DECIMAL", "定点数", 18, 2),
        Decimal("NUMERIC", "定点数", 18, 2),
        Decimal("NUMBER", "通用数值类型", 18, 2),
        Numeric("REAL", "单精度浮点数"),
        Numeric("FLOAT", "浮点数"),
        Numeric("DOUBLE", "双精度浮点数"),
        DateTime("DATE", "日期"),
        Time("TIME", "时间", 6),
        Time("DATETIME", "日期时间", 6),
        Time("TIMESTAMP", "时间戳", 6),
        Time("INTERVAL YEAR TO MONTH", "年月间隔", 2),
        Time("INTERVAL DAY TO SECOND", "日秒间隔", 6),
        Binary("BINARY", "定长二进制", 16),
        Binary("VARBINARY", "变长二进制", 64),
        Binary("BLOB", "二进制大对象")
    ];

    private static readonly IReadOnlyList<TableColumnTypeOption> GenericTypes =
    [
        Character("varchar", "变长字符", 100),
        Decimal("decimal", "定点数", 18, 2),
        DateTime("date", "日期"),
        Time("timestamp", "时间戳", 6),
        Character("text", "文本")
    ];
    public static IReadOnlyList<TableColumnTypeOption> GetOptions(string? providerName)
    {
        return NormalizeProviderName(providerName) switch
        {
            "oracle" => OracleTypes,
            "sqlserver" => SqlServerTypes,
            "mysql" => MySqlTypes,
            "mariadb" => MySqlTypes,
            "sqlite" => SqliteTypes,
            "postgresql" => PostgreSqlTypes,
            "kingbasees" => PostgreSqlTypes,
            "dameng" => DamengTypes,
            _ => GenericTypes
        };
    }
    public static IReadOnlyList<TableColumnTypeOption> GetOptionsWithCurrent(string? providerName, string? currentType)
    {
        IReadOnlyList<TableColumnTypeOption> options = GetOptions(providerName);
        if (string.IsNullOrWhiteSpace(currentType))
        {
            return options;
        }

        TableColumnTypeOption? existing = FindOption(providerName, currentType);
        if (existing != null)
        {
            return options;
        }

        List<TableColumnTypeOption> extended = [.. options];
        extended.Insert(0, new TableColumnTypeOption
        {
            Name = currentType.Trim(),
            Category = "Current",
            Description = "当前字段使用的数据库类型"
        });
        return extended;
    }
    public static TableColumnTypeOption GetDefault(string? providerName)
    {
        return GetOptions(providerName).First();
    }
    public static TableColumnTypeOption? FindOption(string? providerName, string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        return GetOptions(providerName).FirstOrDefault(option =>
            string.Equals(option.Name, typeName.Trim(), StringComparison.OrdinalIgnoreCase));
    }
    private static string NormalizeProviderName(string? providerName)
    {
        return providerName?.Trim().ToLowerInvariant() ?? string.Empty;
    }
    private static TableColumnTypeOption Character(string name, string description, int? defaultLength = null)
    {
        return new TableColumnTypeOption
        {
            Name = name,
            Category = "Character",
            Description = description,
            SupportsLength = defaultLength.HasValue,
            DefaultLength = defaultLength
        };
    }
    private static TableColumnTypeOption Binary(string name, string description, int? defaultLength = null)
    {
        return new TableColumnTypeOption
        {
            Name = name,
            Category = "Binary",
            Description = description,
            SupportsLength = defaultLength.HasValue,
            DefaultLength = defaultLength
        };
    }
    private static TableColumnTypeOption Numeric(string name, string description)
    {
        return new TableColumnTypeOption
        {
            Name = name,
            Category = "Numeric",
            Description = description
        };
    }
    private static TableColumnTypeOption Decimal(string name, string description, int defaultPrecision, int defaultScale)
    {
        return new TableColumnTypeOption
        {
            Name = name,
            Category = "Numeric",
            Description = description,
            SupportsPrecision = true,
            SupportsScale = true,
            DefaultPrecision = defaultPrecision,
            DefaultScale = defaultScale
        };
    }
    private static TableColumnTypeOption DateTime(string name, string description)
    {
        return new TableColumnTypeOption
        {
            Name = name,
            Category = "DateTime",
            Description = description
        };
    }
    private static TableColumnTypeOption Time(string name, string description, int defaultPrecision)
    {
        return new TableColumnTypeOption
        {
            Name = name,
            Category = "DateTime",
            Description = description,
            SupportsPrecision = true,
            DefaultPrecision = defaultPrecision
        };
    }
    private static TableColumnTypeOption Misc(string name, string description)
    {
        return new TableColumnTypeOption
        {
            Name = name,
            Category = "Misc",
            Description = description
        };
    }
}
