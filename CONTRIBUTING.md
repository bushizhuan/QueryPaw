# Contributing to QueryPaw

[中文](#中文) | [English](#english)

## 中文

感谢你愿意花时间改进 QueryPaw。

QueryPaw 是一个面向真实数据库查询、结果分析和元数据维护场景的桌面工具。我们欢迎代码贡献，也欢迎数据库兼容性反馈、文档修正、截图、演示 GIF、安装包建议和真实使用体验。

### 开发环境

1. 安装 .NET 9 SDK。
2. 还原依赖：

```powershell
dotnet restore SqlAnalyzer.Next.sln
```

3. 构建解决方案：

```powershell
dotnet build SqlAnalyzer.Next.sln -c Debug --no-restore
```

4. 运行桌面应用：

```powershell
dotnet run --project SqlAnalyzer.App\SqlAnalyzer.App.csproj --no-build
```

### 贡献方向

当前最欢迎这些类型的贡献：

- 不同数据库版本的连接、元数据读取、查询执行和结果展示兼容性反馈。
- MariaDB、SQLite、Dameng、KingbaseES、Oracle、SQL Server、PostgreSQL、MySQL、MongoDB 等数据库的真实测试结果。
- SQL 编辑器、结果表格、注释维护、对象浏览器、表设计和模型关系图的体验优化。
- Windows 安装包、winget、Scoop、Chocolatey、Microsoft Store/MSIX 等分发方式。
- README 截图、演示 GIF、文档改写、英文表达和示例数据。

### 代码贡献建议

- 保持改动聚焦。QueryPaw 的功能面较广，小而清晰的 pull request 更容易 review。
- 修改连接、查询执行、结果表格、补全、保存配置等核心行为时，请说明验证范围。
- 新增界面文案时，请尽量通过现有本地化模型维护，避免直接硬编码新标签。
- 尽量避免继续扩大 `MainWindow.axaml.cs`、`MainWindowViewModel.cs` 和宽泛的数据服务。当前重构方向请查看 [REFACTOR-PLAN.md](REFACTOR-PLAN.md)。
- 如果暂时无法添加自动化测试，请在 PR 中写清楚人工验证步骤。

### 请不要提交

- 数据库密码、连接导出文件、真实主机名或私有环境信息。
- `.wlb` 连接导出文件、本地编辑器会话、日志、构建输出和打包产物。
- 专有数据库驱动、商业 SDK 或无法再分发的二进制文件。
- 包含客户数据、生产数据或敏感信息的截图。

### 提 Issue 时最好包含

- 操作系统版本。
- QueryPaw 版本或 commit。
- 数据库类型和版本。
- 复现步骤。
- 期望行为和实际行为。
- 相关日志、截图或 SQL 示例。请先移除敏感信息。

## English

Thank you for taking the time to improve QueryPaw.

QueryPaw is a desktop tool for real database querying, result inspection, and metadata maintenance workflows. Code contributions are welcome, and so are database compatibility reports, documentation fixes, screenshots, demo GIFs, packaging suggestions, and real-world usage feedback.

### Development Setup

1. Install the .NET 9 SDK.
2. Restore dependencies:

```powershell
dotnet restore SqlAnalyzer.Next.sln
```

3. Build the solution:

```powershell
dotnet build SqlAnalyzer.Next.sln -c Debug --no-restore
```

4. Run the desktop app:

```powershell
dotnet run --project SqlAnalyzer.App\SqlAnalyzer.App.csproj --no-build
```

### Good Contribution Areas

The most useful contributions right now are:

- Compatibility feedback for different database versions, metadata loading, query execution, and result rendering.
- Real testing for MariaDB, SQLite, Dameng, KingbaseES, Oracle, SQL Server, PostgreSQL, MySQL, MongoDB, and related providers.
- UX improvements for the SQL editor, result grids, comment maintenance, object explorer, table design, and model diagrams.
- Packaging and distribution work for Windows installer, winget, Scoop, Chocolatey, and Microsoft Store/MSIX.
- README screenshots, demo GIFs, documentation polish, English copy improvements, and sample data.

### Code Contribution Notes

- Keep changes focused. QueryPaw has a broad feature surface, and small pull requests are much easier to review.
- When changing core behavior such as connections, execution, result grids, completion, or persisted settings, describe the validation scope.
- Keep UI strings localizable through the existing localization model instead of hard-coding new labels.
- Avoid growing `MainWindow.axaml.cs`, `MainWindowViewModel.cs`, and broad data services when a narrower module is practical. See [REFACTOR-PLAN.md](REFACTOR-PLAN.md).
- If automated tests are not practical for a change, include clear manual verification steps in the PR.

### Do Not Commit

- Database passwords, connection exports, real host names, or private environment details.
- `.wlb` connection exports, local editor sessions, logs, build output, or packaged binaries.
- Proprietary database drivers, commercial SDKs, or binary files that cannot be redistributed.
- Screenshots containing customer data, production data, or sensitive information.

### Helpful Issue Details

- Operating system version.
- QueryPaw version or commit.
- Database type and version.
- Reproduction steps.
- Expected behavior and actual behavior.
- Relevant logs, screenshots, or SQL examples with sensitive information removed.
