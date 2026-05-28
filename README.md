# QueryPaw

[中文](#中文) | [English](#english)

## 中文

QueryPaw 是一款使用 C# 和 Avalonia 构建的跨平台数据库查询与分析桌面应用。项目源自旧版 WinForms SQL 查询分析器，目前正在重构为一个现代化、多数据库支持的工具，用于编写 SQL、浏览数据库对象、执行查询、查看结果，以及维护数据库元数据。

产品以小熊猫作为品牌形象，名称结合了查询工作和轻量、易记的品牌表达。

### 当前状态

QueryPaw 目前仍处于 1.0 之前的阶段。它已经可以用于开发和测试，但部分功能仍在稳定和重构中。当前架构计划请查看 [REFACTOR-PLAN.md](REFACTOR-PLAN.md)。

### 功能特性

- 多标签 SQL 编辑器，支持打开、保存、自动保存、搜索、替换、格式化和语法高亮。
- 查询执行，支持结果表格、消息输出、执行计划入口、取消执行、结果导出，以及可编辑结果表格的基础能力。
- 结果表格可显示中文表名和字段备注，减少在英文物理字段与业务含义之间来回对照的成本。
- 可基于中文备注编写 SQL，编辑器会在元数据可用时辅助匹配真实表名、字段名和别名。
- 连接管理，支持连接档案搜索、收藏、环境标签、无密码导入/导出、驱动路径配置和诊断。
- 对象浏览器，支持 schema、表、视图、物化视图、函数、过程、序列、触发器、同义词和包等对象。
- SQL 补全，支持关键字、表名、字段名、别名，以及在元数据可用时补全中文表/字段注释。
- 注释维护、表结构设计、对象详情视图和模型关系图等功能正在持续开发。
- 当前本地化模型支持中文和英文界面文本。

### 中文备注辅助查询

QueryPaw 的一个核心目标，是让数据库查询更贴近业务语言，而不只是数据库物理命名。

当数据库元数据中包含表备注、字段备注或本地化字典时，查询结果表格可以直接展示中文备注。用户查看结果时，不必只面对 `cust_id`、`org_code`、`settle_amt` 这类物理字段名，也可以看到对应的中文业务含义，从而更快理解数据。

在编写 SQL 时，QueryPaw 也会利用这些中文备注提供辅助能力。你可以根据中文表名、中文字段备注或业务关键词查找对象，编辑器会帮助定位对应的真实表名、字段名和别名。这让不熟悉底层字段命名的人，也能更自然地完成查询编写、字段选择和结果核对。

### 支持的数据库

当前数据库提供方目录包含：

- SQL Server
- Oracle
- MySQL
- PostgreSQL
- KingbaseES
- Dameng
- MongoDB

部分数据库需要用户自行安装客户端库，或手动配置驱动路径。本仓库不包含专有数据库驱动。

### 环境要求

- .NET 9 SDK
- Avalonia 支持的 Windows、macOS 或 Linux 桌面环境
- 某些数据库提供方所需的原生或托管客户端库

### 构建与运行

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

### 本地数据

QueryPaw 会将用户相关数据保存在操作系统的本地应用数据目录中，而不是仓库目录中。请不要提交连接导出文件、已保存连接档案、日志或编辑器会话文件。

本地保存的连接密码会使用首次运行时生成的本机用户密钥保护。`.wlb` 导出文件会刻意移除密码，便于在不携带数据库凭据的情况下迁移连接配置。

`.gitignore` 已排除常见本地数据和构建输出，包括 `.wlb`、`bin`、`obj`、`artifacts`、`.vs`、压缩包和本地 NuGet 缓存。

### 参与贡献

欢迎贡献代码。请先阅读 [CONTRIBUTING.md](CONTRIBUTING.md)，并尽量保持改动聚焦。当前优先级是稳定主要功能模块，避免主窗口和宽泛的数据服务继续膨胀。

### 安全

报告问题或分享日志前，请阅读 [SECURITY.md](SECURITY.md)。请勿公开真实数据库凭据、连接导出文件、主机名，或包含私有数据的截图。

### 许可证

QueryPaw 使用 [MIT License](LICENSE) 开源。

第三方依赖、数据库标志和商标说明请查看 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## English

QueryPaw is a cross-platform database query and analysis desktop app built with C# and Avalonia. The project grew out of a legacy WinForms SQL query analyzer and is now being rebuilt as a modern, multi-provider tool for writing SQL, browsing database objects, running queries, inspecting results, and working with database metadata.

The red panda is the product mascot, and the name combines query work with a light, memorable brand.

### Status

QueryPaw is pre-1.0 software. It is usable for active development and testing, but several areas are still being stabilized and refactored. See [REFACTOR-PLAN.md](REFACTOR-PLAN.md) for the current architecture plan.

### Features

- Multi-tab SQL editor with open, save, autosave, search, replace, formatting, and syntax highlighting.
- Query execution with result grids, messages, execution-plan entry points, cancellation, result export, and editable result-grid groundwork.
- Result grids can display localized Chinese table and column comments, reducing the need to manually map physical field names to business meaning.
- SQL authoring can use Chinese comments and business keywords when metadata is available, helping users discover the real table names, column names, and aliases behind those descriptions.
- Connection management with profile search, favorites, environment tags, password-free import/export, driver path configuration, and diagnostics.
- Object explorer for schemas, tables, views, materialized views, functions, procedures, sequences, triggers, synonyms, and packages.
- SQL completion for keywords, table names, column names, aliases, and localized Chinese table/field comments when metadata is available.
- Comment maintenance, table design, object detail views, and model-diagram features in active development.
- Chinese and English UI text through the current localization model.

### Chinese Comment Assisted Querying

One of QueryPaw's core goals is to make database querying closer to business language instead of exposing only physical database naming.

When table comments, column comments, or a localization dictionary are available, result grids can show localized Chinese descriptions directly. Users do not have to interpret fields such as `cust_id`, `org_code`, or `settle_amt` only from their physical names; they can also see the related business meaning while reading the result set.

During SQL authoring, QueryPaw can also use those comments as query assistance. Users can search by Chinese table names, column comments, or business keywords, and the editor helps map them back to the actual table names, column names, and aliases. This makes query writing, field selection, and result verification more natural for users who understand the business domain but may not know every underlying database identifier.

### Supported Providers

The provider catalog includes:

- SQL Server
- Oracle
- MySQL
- PostgreSQL
- KingbaseES
- Dameng
- MongoDB

Some providers require user-installed client libraries or manually configured driver paths. Proprietary database drivers are not bundled in this repository.

### Requirements

- .NET 9 SDK
- Windows, macOS, or Linux desktop environment supported by Avalonia
- Database client libraries for providers that require native or managed drivers

### Build and Run

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

### Local Data

QueryPaw stores user-specific data outside the repository, under the operating system's local application data folder. Do not commit connection exports, saved profiles, logs, or editor session files.

Saved local connection passwords are protected with a user-local key generated on first use. `.wlb` exports intentionally omit passwords so connection files remain portable without carrying database credentials.

The `.gitignore` excludes common local data and build output, including `.wlb`, `bin`, `obj`, `artifacts`, `.vs`, packaged archives, and local NuGet caches.

### Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) and keep changes focused. The current priority is stabilizing major feature modules so future work does not continue to grow the main window and broad data services.

### Security

Please read [SECURITY.md](SECURITY.md) before reporting issues or sharing logs. Never publish real database credentials, connection export files, host names, or screenshots containing private data.

### License

QueryPaw is licensed under the [MIT License](LICENSE).

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party dependencies, database logo assets, and trademark notices.
