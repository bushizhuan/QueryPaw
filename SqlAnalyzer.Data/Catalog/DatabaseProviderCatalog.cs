using SqlAnalyzer.Core.Models;
using SqlAnalyzer.Core.Services;

namespace SqlAnalyzer.Data.Catalog;

public sealed class DatabaseProviderCatalog : IDatabaseProviderCatalog
{
    private readonly IReadOnlyList<DatabaseProviderDefinition> _providers =
    [
        new()
        {
            Name = "SqlServer",
            DisplayName = "SQL Server",
            Kind = "Relational",
            DriverFamily = "SQLServer-family",
            SupportLevel = "Verified",
            RecommendedDriver = "Microsoft.Data.SqlClient",
            InvariantName = "Microsoft.Data.SqlClient",
            FactoryTypeName = "Microsoft.Data.SqlClient.SqlClientFactory, Microsoft.Data.SqlClient",
            DefaultManagedDriver = "Microsoft.Data.SqlClient",
            ConnectionTemplate = "Server={server};Database={database};User ID={user};Password={password};TrustServerCertificate=True",
            TestSql = "select 1",
            ExplainPrefix = "set showplan_text on;",
            Capabilities = new ProviderCapabilities
            {
                SupportsExplain = true,
                SupportsExportInsert = true,
                SupportsDataEdit = true,
                SupportsDirectTableAlter = true,
                SupportsSequences = false,
                SupportsProcedures = true,
                SupportsTriggers = true
            }
        },
        new()
        {
            Name = "Oracle",
            DisplayName = "Oracle",
            Kind = "Relational",
            DriverFamily = "Oracle-family",
            SupportLevel = "Verified",
            RecommendedDriver = "Oracle.ManagedDataAccess",
            InvariantName = "Oracle.ManagedDataAccess.Client",
            FactoryTypeName = "Oracle.ManagedDataAccess.Client.OracleClientFactory, Oracle.ManagedDataAccess",
            DefaultManagedDriver = "Oracle.ManagedDataAccess.dll",
            DefaultNativeDependency = "oci.dll",
            ConnectionTemplate = "Data Source={server};User Id={user};Password={password}",
            TestSql = "select 1 from dual",
            ExplainPrefix = "explain plan for",
            Capabilities = new ProviderCapabilities
            {
                SupportsExplain = true,
                SupportsExportInsert = true,
                SupportsDataEdit = true,
                SupportsDirectTableAlter = true,
                SupportsNativeDependency = true,
                SupportsMaterializedViews = true,
                SupportsSequences = true,
                SupportsPackages = true,
                SupportsProcedures = true,
                SupportsTriggers = true,
                SupportsSynonyms = true
            }
        },
        new()
        {
            Name = "MySql",
            DisplayName = "MySQL",
            Kind = "Relational",
            DriverFamily = "MySQL-family",
            SupportLevel = "Verified",
            RecommendedDriver = "MySql.Data",
            InvariantName = "MySql.Data.MySqlClient",
            FactoryTypeName = "MySql.Data.MySqlClient.MySqlClientFactory, MySql.Data",
            DefaultManagedDriver = "MySql.Data",
            ConnectionTemplate = "Server={server};Database={database};Uid={user};Pwd={password};Allow User Variables=True",
            TestSql = "select 1",
            ExplainPrefix = "explain",
            Capabilities = new ProviderCapabilities
            {
                SupportsExplain = true,
                SupportsExportInsert = true,
                SupportsDataEdit = true,
                SupportsDirectTableAlter = true,
                SupportsSequences = false,
                SupportsProcedures = true,
                SupportsTriggers = true
            }
        },
        new()
        {
            Name = "MariaDB",
            DisplayName = "MariaDB",
            Kind = "Relational",
            DriverFamily = "MySQL-family",
            SupportLevel = "Experimental",
            RecommendedDriver = "MySql.Data",
            InvariantName = "MySql.Data.MySqlClient",
            FactoryTypeName = "MySql.Data.MySqlClient.MySqlClientFactory, MySql.Data",
            DefaultManagedDriver = "MySql.Data",
            ConnectionTemplate = "Server={server};Database={database};Uid={user};Pwd={password};Allow User Variables=True",
            TestSql = "select 1",
            ExplainPrefix = "explain",
            Capabilities = new ProviderCapabilities
            {
                SupportsExplain = true,
                SupportsExportInsert = true,
                SupportsDataEdit = true,
                SupportsDirectTableAlter = false,
                SupportsSequences = false,
                SupportsProcedures = true,
                SupportsTriggers = true
            }
        },
        new()
        {
            Name = "PostgreSql",
            DisplayName = "PostgreSQL",
            Kind = "Relational",
            DriverFamily = "PostgreSQL-family",
            SupportLevel = "Verified",
            RecommendedDriver = "Npgsql",
            InvariantName = "Npgsql",
            FactoryTypeName = "Npgsql.NpgsqlFactory, Npgsql",
            DefaultManagedDriver = "Npgsql",
            ConnectionTemplate = "Host={server};Database={database};Username={user};Password={password}",
            TestSql = "select 1",
            ExplainPrefix = "explain",
            Capabilities = new ProviderCapabilities
            {
                SupportsExplain = true,
                SupportsExportInsert = true,
                SupportsDataEdit = true,
                SupportsDirectTableAlter = true,
                SupportsMaterializedViews = true,
                SupportsSequences = true,
                SupportsProcedures = true,
                SupportsTriggers = true
            }
        },
        new()
        {
            Name = "KingbaseES",
            DisplayName = "KingbaseES",
            Kind = "Relational",
            DriverFamily = "PostgreSQL-family",
            SupportLevel = "Experimental",
            RecommendedDriver = "Kdbndp",
            InvariantName = "Kdbndp",
            FactoryTypeName = "Kdbndp.KdbndpFactory, Kdbndp",
            FactoryTypeAliases =
            [
                "Kdbndp.KdbndpFactory",
                "KingbaseES.Client.KingbaseFactory, KingbaseES.Client",
                "KingbaseES.Client.KingbaseFactory"
            ],
            DefaultManagedDriver = "Kdbndp.dll",
            ConnectionTemplate = "Server={server};Port={port};Database={database};User Id={user};Password={password}",
            TestSql = "select 1",
            ExplainPrefix = "explain",
            Capabilities = new ProviderCapabilities
            {
                SupportsExplain = true,
                SupportsExportInsert = true,
                SupportsDataEdit = false,
                SupportsDirectTableAlter = false,
                SupportsMaterializedViews = true,
                SupportsSequences = true,
                SupportsProcedures = true,
                SupportsTriggers = true
            }
        },
        new()
        {
            Name = "Dameng",
            DisplayName = "Dameng",
            Kind = "Relational",
            DriverFamily = "Dameng",
            SupportLevel = "Experimental",
            RecommendedDriver = "DmProvider",
            InvariantName = "Dm",
            FactoryTypeName = "Dm.DmClientFactory, DmProvider",
            FactoryTypeAliases =
            [
                "Dm.DmClientFactory",
                "Dm.DmClientFactory, Dm.DmProvider",
                "Dm.DmClientFactory, DmProvider, Version=1.1.0.0, Culture=neutral, PublicKeyToken=7a2d44aa446c6d01"
            ],
            DefaultManagedDriver = "DmProvider.dll",
            ConnectionTemplate = "Server={server};Port={port};UserId={user};PWD={password}",
            TestSql = "select 1",
            ExplainPrefix = "explain",
            Capabilities = new ProviderCapabilities
            {
                SupportsExplain = true,
                SupportsExportInsert = true,
                SupportsDataEdit = false,
                SupportsDirectTableAlter = false,
                SupportsNativeDependency = true,
                SupportsSequences = true,
                SupportsProcedures = true,
                SupportsTriggers = true
            }
        },
        new()
        {
            Name = "SQLite",
            DisplayName = "SQLite",
            Kind = "Relational",
            DriverFamily = "Embedded-file",
            SupportLevel = "Experimental",
            RecommendedDriver = "Microsoft.Data.Sqlite",
            InvariantName = "Microsoft.Data.Sqlite",
            FactoryTypeName = "Microsoft.Data.Sqlite.SqliteFactory, Microsoft.Data.Sqlite",
            DefaultManagedDriver = "Microsoft.Data.Sqlite",
            ConnectionTemplate = "Data Source={database};Pooling=False",
            TestSql = "select 1",
            ExplainPrefix = "explain query plan",
            Capabilities = new ProviderCapabilities
            {
                SupportsExplain = true,
                SupportsExportInsert = true,
                SupportsDataEdit = false,
                SupportsDirectTableAlter = false,
                SupportsSequences = false,
                SupportsProcedures = false,
                SupportsTriggers = true
            }
        },
        new()
        {
            Name = "MongoDb",
            DisplayName = "MongoDB",
            Kind = "Document",
            DriverFamily = "Document",
            SupportLevel = "Planned",
            RecommendedDriver = "MongoDB.Driver",
            InvariantName = "MongoDB.Driver",
            FactoryTypeName = "MongoDB.Driver.MongoClient",
            DefaultManagedDriver = "MongoDB.Driver.dll",
            ConnectionTemplate = "mongodb://{user}:{password}@{server}/{database}",
            TestSql = "{ ping: 1 }",
            Capabilities = new ProviderCapabilities()
        }
    ];
    public IReadOnlyList<DatabaseProviderDefinition> GetAll() => _providers;
    public DatabaseProviderDefinition? Find(string providerName)
    {
        return _providers.FirstOrDefault(x => string.Equals(x.Name, providerName, StringComparison.OrdinalIgnoreCase));
    }
}
