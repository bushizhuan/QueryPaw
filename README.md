# QueryPaw

[中文](#中文) | [English](#english)

[![Latest release](https://img.shields.io/github/v/release/bushizhuan/QueryPaw?style=flat-square)](https://github.com/bushizhuan/QueryPaw/releases/latest)
[![License: MIT](https://img.shields.io/github/license/bushizhuan/QueryPaw?style=flat-square)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/UI-Avalonia-8B44F7?style=flat-square)](https://avaloniaui.net/)

## 中文

QueryPaw 是一款开源 SQL 查询分析器和数据库元数据工具，使用 C#、.NET 9 和 Avalonia 构建。

它不是想成为又一个笨重的通用数据库客户端，而是更关注中文业务系统开发者的日常痛点：表名、字段名、注释、业务含义、查询结果和元数据维护经常散落在不同地方。QueryPaw 希望把这些信息放回同一个工作台里，让写 SQL、看结果、理解字段和维护备注这几件事更顺手。

### 快速下载

- 最新版本：[GitHub Releases](https://github.com/bushizhuan/QueryPaw/releases/latest)
- 当前发布包：Windows x64 便携版，解压后运行 `QueryPaw.exe`
- 许可证：[MIT License](LICENSE)

截图和演示 GIF 会在后续补充。

### 适合谁

- 需要频繁写 SQL、查数据、导出结果的后端开发者和测试人员。
- 面对大量英文物理字段，但业务沟通主要使用中文的人。
- 需要维护表备注、字段备注、模型关系和数据库对象信息的开发或运维人员。
- 想要一个开源、可改造、可审计的数据库查询工具的团队。

### 核心特性

- 多标签 SQL 编辑器，支持打开、保存、自动保存、搜索、替换、格式化和语法高亮。
- 查询执行，支持结果表格、消息输出、取消执行、结果导出和基础可编辑结果能力。
- 结果表格支持中文表名、字段备注展示，并支持列宽调整、复制、导出和上下文操作。
- SQL 补全支持关键字、表名、字段名、别名，以及元数据可用时的中文表/字段注释。
- 连接管理支持搜索、收藏、环境标签、无密码导入/导出、驱动路径配置和诊断。
- 对象浏览器支持 schema、表、视图、物化视图、函数、过程、序列、触发器、同义词和包等对象。
- 注释维护界面支持表备注和字段备注的筛选、编辑、导入、导出、SQL 预览和批量应用。
- 表结构设计、对象详情视图和模型关系图正在持续完善。
- 当前界面文本支持中文和英文。

### 中文备注辅助查询

QueryPaw 的一个核心目标，是让数据库查询更贴近业务语言，而不只是面对物理字段名。

当数据库元数据中包含表备注、字段备注或本地化字典时，查询结果表格可以直接展示中文备注。用户查看结果时，不必只面对 `cust_id`、`org_code`、`settle_amt` 这类字段名，也可以看到对应的中文业务含义。

在编写 SQL 时，QueryPaw 也会利用这些备注提供辅助能力。你可以根据中文表名、中文字段备注或业务关键词查找对象，编辑器会帮助定位真实表名、字段名和别名。这对不熟悉底层命名、但熟悉业务的人尤其有用。

### 支持的数据库

当前数据库提供方目录包含：

- SQL Server
- Oracle
- MySQL
- MariaDB
- PostgreSQL
- SQLite
- KingbaseES
- Dameng
- MongoDB

部分数据库需要用户自行安装客户端库，或手动配置驱动路径。本仓库不包含专有数据库驱动。

### 当前状态

QueryPaw 已发布首个公开版本 `v1.0.0`，可以用于日常试用、开发测试和兼容性反馈。项目仍在快速迭代中，欢迎通过 Issue 反馈数据库兼容问题、界面问题和真实使用场景。

后续方向请查看 [ROADMAP.md](ROADMAP.md)。架构重构记录请查看 [REFACTOR-PLAN.md](REFACTOR-PLAN.md)。

### 从源码构建

源码构建需要 .NET 9 SDK。

还原依赖：

```powershell
dotnet restore SqlAnalyzer.Next.sln
```

构建：

```powershell
dotnet build SqlAnalyzer.Next.sln -c Debug --no-restore
```

运行：

```powershell
dotnet run --project SqlAnalyzer.App\SqlAnalyzer.App.csproj --no-build
```

在非 Windows shell 中，请将路径里的反斜杠替换为正斜杠。

### 解决方案结构

- `SqlAnalyzer.App`：Avalonia 界面、应用外壳、视图、控制器和视图模型。
- `SqlAnalyzer.Core`：领域模型和服务契约。
- `SqlAnalyzer.Data`：数据库提供方目录、元数据加载、查询执行、格式化和数据库服务。
- `SqlAnalyzer.Infrastructure`：本地持久化和工作区存储。

### 本地数据与安全

QueryPaw 会将用户相关数据保存在操作系统的本地应用数据目录中，而不是仓库目录中。请不要提交连接导出文件、已保存连接档案、日志或编辑器会话文件。

本地保存的连接密码会使用首次运行时生成的本机用户密钥保护。`.wlb` 导出文件会刻意移除密码，便于在不携带数据库凭据的情况下迁移连接配置。

报告问题或分享日志前，请阅读 [SECURITY.md](SECURITY.md)。请勿公开真实数据库凭据、连接导出文件、主机名，或包含私有数据的截图。

### 参与贡献

欢迎贡献代码、文档、数据库兼容测试和真实场景反馈。请先阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。

当前特别欢迎：

- 不同数据库版本的连接、元数据读取和查询执行反馈。
- MariaDB、SQLite、Dameng、KingbaseES 等数据库的兼容性测试。
- Windows 安装包、winget、Scoop、Chocolatey 等分发方式改进。
- README 截图、演示 GIF、文档和英文表达优化。

### 许可证

QueryPaw 使用 [MIT License](LICENSE) 开源。

第三方依赖、数据库标志和商标说明请查看 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## English

QueryPaw is an open-source SQL query analyzer and database metadata workspace built with C#, .NET 9, and Avalonia.

It is not trying to be another heavyweight all-purpose database client. QueryPaw focuses on a practical workflow that many business-system developers deal with every day: writing SQL, understanding result sets, mapping physical column names to business meaning, and maintaining table or column comments in one place.

### Download

- Latest version: [GitHub Releases](https://github.com/bushizhuan/QueryPaw/releases/latest)
- Current package: Windows x64 portable package. Unzip and run `QueryPaw.exe`.
- License: [MIT License](LICENSE)

Screenshots and demo GIFs will be added soon.

### Who It Is For

- Backend developers and testers who write SQL, inspect data, and export results frequently.
- Teams that work with databases where physical names and business descriptions are both important.
- Developers or operators who maintain table comments, column comments, object details, and model relationships.
- Anyone who wants an open-source, inspectable, and customizable database query tool.

### Highlights

- Multi-tab SQL editor with open, save, autosave, search, replace, formatting, and syntax highlighting.
- Query execution with result grids, messages, cancellation, result export, and editable result-grid groundwork.
- Result grids can show localized table and column comments, resize columns, copy values, and export data.
- SQL completion for keywords, table names, column names, aliases, and localized table or column comments when metadata is available.
- Connection management with profile search, favorites, environment tags, password-free import/export, driver path configuration, and diagnostics.
- Object explorer for schemas, tables, views, materialized views, functions, procedures, sequences, triggers, synonyms, and packages.
- Comment maintenance workspace for filtering, editing, importing, exporting, previewing, and applying table or column comments.
- Table design, object details, and model-diagram features are being improved continuously.
- Chinese and English UI text through the current localization model.

### Comment-Assisted Querying

One of QueryPaw's core goals is to make database querying closer to business language instead of exposing only physical database naming.

When table comments, column comments, or a localization dictionary are available, result grids can show localized descriptions directly. Users do not have to interpret fields such as `cust_id`, `org_code`, or `settle_amt` only from physical names; they can also see the related business meaning while reading result sets.

During SQL authoring, QueryPaw can use those comments as query assistance. Users can search by table comments, column comments, or business keywords, and the editor helps map them back to real table names, column names, and aliases.

### Supported Providers

The provider catalog currently includes:

- SQL Server
- Oracle
- MySQL
- MariaDB
- PostgreSQL
- SQLite
- KingbaseES
- Dameng
- MongoDB

Some providers require user-installed client libraries or manually configured driver paths. Proprietary database drivers are not bundled in this repository.

### Project Status

QueryPaw has its first public `v1.0.0` release and is ready for daily trial use, development testing, and compatibility feedback. The project is still evolving quickly, so issue reports and real-world database compatibility notes are very welcome.

See [ROADMAP.md](ROADMAP.md) for planned work and [REFACTOR-PLAN.md](REFACTOR-PLAN.md) for architecture notes.

### Build From Source

Building from source requires the .NET 9 SDK.

Restore dependencies:

```powershell
dotnet restore SqlAnalyzer.Next.sln
```

Build:

```powershell
dotnet build SqlAnalyzer.Next.sln -c Debug --no-restore
```

Run:

```powershell
dotnet run --project SqlAnalyzer.App\SqlAnalyzer.App.csproj --no-build
```

On non-Windows shells, replace backslashes in paths with forward slashes.

### Solution Layout

- `SqlAnalyzer.App`: Avalonia UI, shell, views, controllers, and view models.
- `SqlAnalyzer.Core`: domain models and service contracts.
- `SqlAnalyzer.Data`: provider catalog, metadata loading, execution, formatting, and database services.
- `SqlAnalyzer.Infrastructure`: local persistence and workspace storage.

### Local Data And Security

QueryPaw stores user-specific data outside the repository, under the operating system's local application data folder. Do not commit connection exports, saved profiles, logs, or editor session files.

Saved local connection passwords are protected with a user-local key generated on first use. `.wlb` exports intentionally omit passwords so connection files remain portable without carrying database credentials.

Please read [SECURITY.md](SECURITY.md) before reporting issues or sharing logs. Never publish real database credentials, connection export files, host names, or screenshots containing private data.

### Contributing

Contributions are welcome: code, documentation, database compatibility reports, screenshots, installation notes, and real workflow feedback all help. Please read [CONTRIBUTING.md](CONTRIBUTING.md) first.

Especially welcome:

- Compatibility feedback for different database versions.
- Testing for MariaDB, SQLite, Dameng, KingbaseES, and other provider combinations.
- Packaging improvements for Windows installer, winget, Scoop, or Chocolatey.
- README screenshots, demo GIFs, documentation, and English copy improvements.

### License

QueryPaw is licensed under the [MIT License](LICENSE).

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party dependencies, database logo assets, and trademark notices.
