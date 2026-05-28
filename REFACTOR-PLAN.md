# SqlAnalyzer.Next Refactor Plan / 重构计划

[中文](#中文) | [English](#english)

## 中文

### 目的

本文档定义当前 `SqlAnalyzer.Next` 代码库的重构路径。目标不是从零重新设计产品，而是在保留当前可用功能的前提下稳定现有功能面，降低回归风险，并建立清晰的模块边界，让后续功能不再继续堆进主窗口。

当前应用已经可用，但几个关键职责仍集中在少数体量较大的文件中：

- [MainWindow.axaml.cs](SqlAnalyzer.App/Views/MainWindow.axaml.cs)
- [MainWindowViewModel.cs](SqlAnalyzer.App/ViewModels/MainWindowViewModel.cs)
- [DatabaseExplorerService.cs](SqlAnalyzer.Data/Explorer/DatabaseExplorerService.cs)
- [TableDesignerWindow.axaml.cs](SqlAnalyzer.App/Views/TableDesignerWindow.axaml.cs)

### 当前快照

#### 功能优势

- 连接管理已经可用，并支持：
  - 连接档案列表
  - 搜索
  - 环境标签
  - 收藏
  - 导入/导出
  - 连接测试
- 对象浏览树已经支持：
  - schema
  - 表
  - 视图
  - 物化视图
  - 函数
  - 过程
  - 序列
  - 触发器
  - 同义词
  - 包
- 编辑器已经支持：
  - 多标签文档
  - 打开/保存/另存为
  - 查找/替换
  - 格式化
  - 执行
  - 执行计划入口
  - 补全
- 结果工作区已经支持：
  - 多结果集
  - 筛选
  - 排序
  - 导出
  - 复制
  - 行选择
  - 操作列

#### 当前结构风险

- `MainWindow.axaml.cs` 同时处理编辑器输入、补全、结果渲染、对象操作、文件生命周期、弹窗编排和日志。
- `DatabaseExplorerService.cs` 混合了对象树元数据、补全元数据、表设计元数据、导出逻辑和保存逻辑。
- 表设计器尚未与元数据加载失败隔离。
- 结果渲染和结果模型行为仍由主窗口紧密编排。
- 补全比之前稳定很多，但生命周期仍由视图层驱动。

### 重构原则

#### 原则 1：迁移过程中保持现有行为可用

不要一次性重写所有内容。每次只把一条功能链迁移到稳定 API 后面。

#### 原则 2：拆分编排与计算

UI 代码负责编排，服务负责计算，模型负责承载状态。

#### 原则 3：让失败局部化

单个功能失败不应该拖垮整个窗口。表设计、结果渲染、补全和元数据加载都应能独立失败和降级。

#### 原则 4：优先按功能模块组织，而不是堆进技术杂物间

新代码应按功能职责分组，不应继续推入 `MainWindow.axaml.cs` 或 `DatabaseExplorerService.cs`。

### 热点和目标边界

#### 1. 主窗口热点

当前热点：

- [MainWindow.axaml.cs](SqlAnalyzer.App/Views/MainWindow.axaml.cs)

目标拆分：

- `EditorController`
- `CompletionController`
- `ResultWorkspaceController`
- `ConnectionDialogController`
- `ObjectActionController`
- `TableDesignCoordinator`

主窗口只应负责：

- 初始化应用外壳
- 将事件接入控制器
- 承载布局

#### 2. Explorer 服务热点

当前热点：

- [DatabaseExplorerService.cs](SqlAnalyzer.Data/Explorer/DatabaseExplorerService.cs)

目标拆分：

- `ExplorerMetadataService`
- `CompletionMetadataService`
- `TableDesignService`
- `ObjectScriptService`
- `ConnectionDiagnosticService`

每个服务都应拥有窄契约和独立缓存范围。

#### 3. 结果工作区热点

当前热点：

- 结果状态位于 [MainWindowViewModel.cs](SqlAnalyzer.App/ViewModels/MainWindowViewModel.cs)
- 渲染逻辑位于 [MainWindow.axaml.cs](SqlAnalyzer.App/Views/MainWindow.axaml.cs)

目标拆分：

- `ResultWorkspaceViewModel`
- `ResultGridViewModel`
- `ResultClipboardService`
- `ResultExportService`
- `ResultHeaderFormatter`

#### 4. 表设计热点

当前热点：

- [TableDesignerWindow.axaml.cs](SqlAnalyzer.App/Views/TableDesignerWindow.axaml.cs)

目标拆分：

- `TableDesignerViewModel`
- `TableDesignLoadResult`
- `TableDesignSaveCoordinator`
- `TableDesignSqlPreviewService`

即使部分元数据标签页加载失败，设计器也必须能够打开。

### 重构阶段

### 阶段 1：稳定层

#### 目标

在不改变主要用户工作流的前提下，降低跨功能耦合。

#### 任务

1. 在 `SqlAnalyzer.App` 下引入轻量功能控制器。
2. 将补全调度移出 `MainWindow`。
3. 将结果渲染编排移出 `MainWindow`。
4. 引入表设计打开/保存协调器。
5. 增加功能局部日志方法，替代散落在窗口里的临时字符串。

#### 验收

- `MainWindow.axaml.cs` 停止继续膨胀。
- 新功能修复落在控制器中，而不是窗口类中。
- 表设计器打开路径有单一协调器入口。

### 阶段 2：Explorer/Data 服务拆分

#### 目标

将 `DatabaseExplorerService` 拆成职责聚焦的服务。

#### 任务

1. 抽取对象树加载：
  - schema
  - 文件夹
  - 对象节点
2. 抽取补全快照加载和缓存。
3. 抽取表设计元数据加载。
4. 抽取 DDL/脚本导出。
5. 抽取连接诊断和提供方检查。

#### 验收

- 不再有单个服务同时负责对象树加载、表设计和补全。
- 补全缓存可以演进而不触碰表设计代码。
- 表设计失败可以独立诊断。

### 阶段 3：结果工作区组件化

#### 目标

将结果区域变成自包含的功能模块。

#### 任务

1. 将结果状态从主视图模型移入结果工作区模型。
2. 将复制/导出逻辑从窗口移入结果服务。
3. 将行选择和操作列行为移入结果控制器。
4. 将结果表头格式化移入专用 formatter。
5. 分离：
  - 表格结果
  - 消息结果
  - 执行计划结果

#### 验收

- 结果渲染代码不再混入编辑器逻辑。
- 复制/导出不再需要直接访问窗口。
- 新结果功能可以不触碰补全或编辑器代码。

### 阶段 4：编辑器和补全分离

#### 目标

让编辑器行为更稳定，也更容易演进。

#### 任务

1. 将按键处理和编辑器输入规则移入编辑器控制器。
2. 将补全上下文分析移入专用补全服务。
3. 保持补全弹窗生命周期与编辑器文本同步相互独立。
4. 为每次补全请求增加小型诊断日志。
5. 为更丰富的 SQL 上下文识别做准备：
  - 关系上下文
  - 字段上下文
  - 别名上下文

#### 验收

- 输入延迟保持稳定。
- 补全行为变更不需要修改无关的结果逻辑。
- 补全问题可以不用扫描整个窗口类就能诊断。

### 阶段 5：表设计器重写

#### 目标

用稳定的功能模块替换当前偏 code-behind 的表设计器。

#### 任务

1. 构建 `TableDesignerViewModel`。
2. 增加独立的标签页加载状态：
  - 字段
  - 索引
  - 外键
  - 检查约束
  - 触发器
  - 选项
3. 即使某个元数据查询失败，也允许预览模式继续工作。
4. 将 SQL 预览生成拆到专用服务。
5. 增加明确的模式状态：
  - 直接保存
  - 仅预览

#### 验收

- 表设计器可以可靠打开。
- 元数据失败不会导致整个弹窗不可用。
- 保存逻辑独立于 UI 控件状态。

### 阶段 6：连接中心组件化

#### 目标

将当前 overlay 变成真正的功能模块。

#### 任务

1. 抽取连接列表筛选逻辑。
2. 抽取各数据库提供方的表单行为。
3. 抽取诊断卡片行为。
4. 分离导入/导出和编辑状态。
5. 增加清晰的能力感知展示规则。

#### 验收

- 连接中心可以演进而不继续膨胀 `MainWindowViewModel`。
- 各数据库提供方字段被隔离，且可测试。

### 迁移规则

#### 规则 1

如果两条功能链共享高风险状态，不要在同一步中同时迁移它们。

#### 规则 2

抽取逻辑时，先保持旧的外部行为，再简化内部实现。

#### 规则 3

任何抽取出来的模块都必须具备：

- 清晰输入
- 清晰输出
- 不直接依赖无关窗口状态

#### 规则 4

当某个功能不稳定时，优先选择降级模式，而不是硬失败。

示例：

- 表设计器在部分元数据失败时以预览模式打开
- 补全在元数据缓存不可用时退回关键字补全
- 结果工作区即使表格渲染失败，也仍能显示消息结果

### 推荐的下一步

#### 立即优先

1. 使用新的 fallback-first 方案继续稳定表设计器。
2. 从 `MainWindow` 中抽取结果工作区控制流。
3. 从 `MainWindow` 中抽取补全调度和弹窗生命周期。

#### 之后

4. 拆分 `DatabaseExplorerService`。
5. 将连接中心抽取为独立功能。

### 重构成功标准

当满足以下条件时，可以认为本轮重构成功：

- `MainWindow.axaml.cs` 缩减为应用外壳编排
- `DatabaseExplorerService.cs` 被拆分为聚焦服务
- 表设计器可以可靠打开，并能优雅降级
- 结果工作区独立于编辑器和补全逻辑
- 修复补全问题不需要触碰结果或表设计代码
- 性能回归可以定位到单个功能模块

## English

### Purpose

This document defines the refactor path for the current `SqlAnalyzer.Next` codebase. The goal is not to redesign the product from scratch. The goal is to stabilize the current working feature set, reduce the risk of regressions, and create clear module boundaries so future features can be added without continuing to overload the main window.

The current application is already usable, but several critical responsibilities are still concentrated in a few oversized files:

- [MainWindow.axaml.cs](SqlAnalyzer.App/Views/MainWindow.axaml.cs)
- [MainWindowViewModel.cs](SqlAnalyzer.App/ViewModels/MainWindowViewModel.cs)
- [DatabaseExplorerService.cs](SqlAnalyzer.Data/Explorer/DatabaseExplorerService.cs)
- [TableDesignerWindow.axaml.cs](SqlAnalyzer.App/Views/TableDesignerWindow.axaml.cs)

### Current Snapshot

#### Functional Strengths

- Connection management is available and already supports:
  - profile list
  - search
  - environment tagging
  - favorites
  - import/export
  - connection testing
- Explorer tree already supports:
  - schemas
  - tables
  - views
  - materialized views
  - functions
  - procedures
  - sequences
  - triggers
  - synonyms
  - packages
- Editor already supports:
  - multi-tab documents
  - open/save/save as
  - find/replace
  - formatting
  - execution
  - explain entry
  - completion
- Result workspace already supports:
  - multiple result sets
  - filtering
  - sorting
  - export
  - copy
  - row selection
  - operation column

#### Current Structural Risks

- `MainWindow.axaml.cs` handles editor input, completion, result rendering, object actions, file lifecycle, dialog orchestration, and logging.
- `DatabaseExplorerService.cs` mixes explorer metadata, completion metadata, table design metadata, export logic, and save logic.
- Table designer is not isolated from metadata loading failures.
- Result rendering and result model behavior are still tightly orchestrated from the main window.
- Completion is now much more stable than before, but its lifecycle is still driven from the view.

### Refactor Principles

#### Principle 1: Preserve Working Behavior During Migration

Do not rewrite everything at once. Move one chain at a time behind a stable API.

#### Principle 2: Split Orchestration From Computation

UI code should orchestrate. Services should compute. Models should carry state.

#### Principle 3: Make Failure Local

One feature failing should not take down the entire window. Table designer, result rendering, completion, and metadata loading should all fail independently.

#### Principle 4: Prefer Feature Modules Over Technical Dumping Grounds

New code should be grouped by feature responsibility rather than pushed into `MainWindow.axaml.cs` or `DatabaseExplorerService.cs`.

### Hotspots and Target Boundaries

#### 1. Main Window Hotspot

Current hotspot:

- [MainWindow.axaml.cs](SqlAnalyzer.App/Views/MainWindow.axaml.cs)

Target split:

- `EditorController`
- `CompletionController`
- `ResultWorkspaceController`
- `ConnectionDialogController`
- `ObjectActionController`
- `TableDesignCoordinator`

Main window should only:

- initialize the shell
- wire events to controllers
- host layout

#### 2. Explorer Service Hotspot

Current hotspot:

- [DatabaseExplorerService.cs](SqlAnalyzer.Data/Explorer/DatabaseExplorerService.cs)

Target split:

- `ExplorerMetadataService`
- `CompletionMetadataService`
- `TableDesignService`
- `ObjectScriptService`
- `ConnectionDiagnosticService`

Each service should have a narrow contract and separate caching scope.

#### 3. Result Workspace Hotspot

Current hotspot:

- result state in [MainWindowViewModel.cs](SqlAnalyzer.App/ViewModels/MainWindowViewModel.cs)
- rendering logic in [MainWindow.axaml.cs](SqlAnalyzer.App/Views/MainWindow.axaml.cs)

Target split:

- `ResultWorkspaceViewModel`
- `ResultGridViewModel`
- `ResultClipboardService`
- `ResultExportService`
- `ResultHeaderFormatter`

#### 4. Table Design Hotspot

Current hotspot:

- [TableDesignerWindow.axaml.cs](SqlAnalyzer.App/Views/TableDesignerWindow.axaml.cs)

Target split:

- `TableDesignerViewModel`
- `TableDesignLoadResult`
- `TableDesignSaveCoordinator`
- `TableDesignSqlPreviewService`

The designer must open even if some metadata tabs fail.

### Refactor Phases

### Phase 1: Stabilization Layer

#### Goal

Reduce cross-feature coupling without changing the main user workflow.

#### Tasks

1. Introduce thin feature controllers under `SqlAnalyzer.App`.
2. Move completion scheduling out of `MainWindow`.
3. Move result rendering orchestration out of `MainWindow`.
4. Introduce table design open/save coordinator.
5. Add feature-local logging methods instead of ad hoc strings scattered in the window.

#### Acceptance

- `MainWindow.axaml.cs` stops growing.
- New feature fixes land in controllers instead of the window class.
- Table designer open path has a single coordinator entry point.

### Phase 2: Explorer/Data Service Split

#### Goal

Break `DatabaseExplorerService` into focused services.

#### Tasks

1. Extract explorer tree loading:
  - schemas
  - folders
  - object nodes
2. Extract completion snapshot loading and caching.
3. Extract table design metadata loading.
4. Extract DDL/script export.
5. Extract connection diagnostic and provider checks.

#### Acceptance

- No single service owns both tree loading and table design and completion.
- Completion cache can be evolved without touching table design code.
- Table design failure can be diagnosed independently.

### Phase 3: Result Workspace Componentization

#### Goal

Turn the result area into a self-contained feature module.

#### Tasks

1. Move result state from main view model into result workspace models.
2. Move copy/export logic from the window into result services.
3. Move row selection and operation-column behavior into a result controller.
4. Move result header formatting into a dedicated formatter.
5. Separate:
  - tabular result
  - message result
  - plan result

#### Acceptance

- Result rendering code is no longer mixed with editor logic.
- Copy/export does not require direct window access.
- New result features can be added without touching completion or editor code.

### Phase 4: Editor and Completion Separation

#### Goal

Make editor behavior stable and easier to evolve.

#### Tasks

1. Move key handling and editor input rules into an editor controller.
2. Move completion context analysis into a dedicated completion service.
3. Keep the completion popup lifecycle separate from editor text synchronization.
4. Add a small completion diagnostics log per request.
5. Prepare for richer SQL context detection:
  - relation context
  - column context
  - alias context

#### Acceptance

- Input latency remains stable.
- Completion behavior changes do not require editing unrelated result logic.
- Completion bugs are diagnosable without scanning the whole window class.

### Phase 5: Table Designer Rewrite

#### Goal

Replace the current code-behind-heavy table designer with a stable feature module.

#### Tasks

1. Build `TableDesignerViewModel`.
2. Add independent tab load states:
  - columns
  - indexes
  - foreign keys
  - checks
  - triggers
  - options
3. Allow preview mode even if one metadata query fails.
4. Separate SQL preview generation into a dedicated service.
5. Add explicit mode state:
  - direct save
  - preview only

#### Acceptance

- Table designer opens reliably.
- Metadata failure does not collapse the entire dialog.
- Save logic is independent from UI widget state.

### Phase 6: Connection Center Componentization

#### Goal

Turn the current overlay into a real feature module.

#### Tasks

1. Extract connection list filtering logic.
2. Extract provider-specific form behavior.
3. Extract diagnostic card behavior.
4. Separate import/export from edit state.
5. Add clear capability-aware display rules.

#### Acceptance

- Connection center can evolve without inflating `MainWindowViewModel`.
- Provider-specific fields are isolated and testable.

### Migration Rules

#### Rule 1

Never move more than one feature chain in the same step if both chains share high-risk state.

#### Rule 2

When extracting logic, preserve the old public behavior first, then simplify internals.

#### Rule 3

Any extracted module must have:

- clear inputs
- clear outputs
- no direct dependence on unrelated window state

#### Rule 4

When a feature is unstable, prefer fallback mode over hard failure.

Examples:

- table designer opens in preview mode if some metadata fails
- completion falls back to keyword-only mode if metadata cache is unavailable
- result workspace still shows message results even if tabular rendering fails

### Recommended Next Actions

#### Immediate Priority

1. Finish stabilizing table designer using the new fallback-first approach.
2. Extract result workspace control flow from `MainWindow`.
3. Extract completion scheduling and popup lifecycle from `MainWindow`.

#### After That

4. Split `DatabaseExplorerService`.
5. Extract connection center into an isolated feature.

### Success Criteria for the Refactor

The refactor should be considered successful when:

- `MainWindow.axaml.cs` is reduced to shell orchestration
- `DatabaseExplorerService.cs` is split into focused services
- table designer opens reliably and degrades gracefully
- result workspace is independent from editor and completion logic
- completion bugs can be fixed without touching result or table design code
- performance regressions can be localized to one feature module
