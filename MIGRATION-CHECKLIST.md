# Migration Checklist / 迁移清单

[中文](#中文) | [English](#english)

## 中文

本文件用于跟踪旧版 WinForms 客户端的功能面，以及迁移到 Avalonia 版本后的目标状态。

### 已识别的旧版模块

- 主应用外壳
  - 菜单和工具栏操作
  - 对象浏览器和文件树
  - 结果/日志标签页
  - 连接导入/导出
  - 语言切换
- SQL 编辑器
  - 多标签编辑
  - 打开/保存/另存为
  - 自动保存和会话恢复
  - F5 / Ctrl+Enter 执行
  - 选中 SQL 执行
  - SQL/JSON/XML 格式化
  - 语法高亮
  - 搜索/替换
  - 模板插入
  - Tab 模板补全
  - 关键字/表/字段弹窗补全
- 数据库功能
  - 多数据库连接档案
  - 驱动路径配置
  - Oracle 认证模式
  - 执行计划
  - 对象元数据浏览
  - CSV 导出
- 平台功能
  - 加密连接档案存储
  - `.wlb` 导入/导出
  - 本地化
  - 状态栏摘要

### 迁移状态

- 已完成
  - 新解决方案结构
  - Avalonia 应用外壳
  - 数据库提供方目录骨架
  - 连接/会话持久化骨架
  - 本地依赖还原配置
- 进行中
  - 编辑器迁移
  - 连接管理界面
  - 数据库执行管线
  - 对象浏览器元数据加载
- 尚未开始
  - 执行计划界面
  - 本地化资源迁移
  - 导入/导出界面
  - 驱动配置界面
  - 补全和语法高亮

## English

This file tracks the feature surface from the legacy WinForms client and the migration target in Avalonia.

### Legacy Modules Identified

- Main shell
  - menu and toolbar actions
  - object explorer and file tree
  - result/log tabs
  - connection import/export
  - language switching
- SQL editor
  - multi-tab editing
  - open/save/save as
  - autosave and session restore
  - F5 / Ctrl+Enter execution
  - selected-SQL execution
  - SQL/JSON/XML formatting
  - syntax highlight
  - search/replace
  - template insertion
  - tab-template completion
  - popup keyword/table/column completion
- Database features
  - multi-provider connection profiles
  - driver path configuration
  - Oracle authentication modes
  - execution plan
  - object metadata browsing
  - CSV export
- Platform features
  - encrypted connection profile storage
  - import/export `.wlb`
  - localization
  - status bar summaries

### Migration Status

- Done
  - new solution structure
  - Avalonia shell
  - provider catalog skeleton
  - connection/session persistence skeleton
  - local restore configuration
- In progress
  - editor migration
  - connection management UI
  - database execution pipeline
  - object explorer metadata loading
- Not started
  - execution plan UI
  - localization resource migration
  - import/export UI
  - driver configuration UI
  - completion and syntax highlighting
