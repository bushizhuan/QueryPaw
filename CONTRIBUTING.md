# Contributing to QueryPaw / 参与 QueryPaw 贡献

[中文](#中文) | [English](#english)

## 中文

感谢你愿意花时间改进 QueryPaw。

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

### 贡献指南

- 保持功能改动聚焦。QueryPaw 的功能面较广，小而清晰的 pull request 更容易 review。
- 修改连接、查询执行、结果表格或补全行为时，优先补充测试，或在提交说明中写清楚窄范围验证结果。
- 不要提交数据库密码、`.wlb` 连接导出文件、本地编辑器会话、构建输出或打包后的二进制文件。
- 不要把专有数据库驱动或 SDK 直接加入仓库。请使用包引用，或在文档中说明手动驱动配置方式。
- 新增界面文案时，请通过现有本地化模型维护，避免直接硬编码新标签。

### 当前重构方向

当前重构计划记录在 [REFACTOR-PLAN.md](REFACTOR-PLAN.md)。新的功能和修复应尽量避免继续扩大 `MainWindow.axaml.cs`、`MainWindowViewModel.cs` 和宽泛的数据服务。只有在没有实际替代方案时，才把逻辑放入这些热点文件。

## English

Thank you for taking the time to improve QueryPaw.

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

### Contribution Guidelines

- Keep feature changes focused. QueryPaw has a broad feature surface, and small pull requests are much easier to review.
- Prefer adding tests or narrow verification notes when changing connection, execution, result-grid, or completion behavior.
- Do not commit database passwords, `.wlb` connection exports, local editor sessions, build output, or packaged binaries.
- Do not add proprietary database drivers or SDKs directly to the repository. Use package references or document manual driver configuration.
- Keep UI strings localizable by adding text through the existing localization model instead of hard-coding new labels.

### Current Refactor Direction

The active refactor plan is tracked in [REFACTOR-PLAN.md](REFACTOR-PLAN.md). New work should avoid growing `MainWindow.axaml.cs`, `MainWindowViewModel.cs`, and broad data services unless there is no practical alternative.
