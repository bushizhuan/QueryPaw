# Open Source Release Checklist / 开源发布检查清单

[中文](#中文) | [English](#english)

## 中文

在将 QueryPaw 推送到公开 GitHub 仓库之前，请使用本清单做最后检查。

### 仓库整洁度

- 确认 `.gitignore` 已排除构建输出、本地包、`.vs`、`artifacts`、压缩包、日志、`.wlb` 文件和用户会话文件。
- 只从 `SqlAnalyzer.Next` 开始发布。除非明确要作为单独归档项目发布，否则不要包含旧版 WinForms 工作区。
- 在接近全新克隆的目录中执行一次干净的依赖还原和构建。

### 安全检查

- 首次提交前搜索凭据、主机名、令牌和私有连接字符串。
- 确认没有提交真实 `.wlb` 连接导出文件。
- 确认新的本地连接档案保存不依赖源码中固定的受控密钥。
- 确认 `.wlb` 导出仍然不包含密码，除非未来加入基于口令的导出设计。
- 将数据库驱动路径和原生客户端路径视为用户配置，而不是仓库内容。

### 许可证和第三方资产

- 确认项目许可证选择。当前仓库使用 MIT。
- 确认有权重新分发应用图标和数据库标志资源。
- 除非许可证明确允许再分发，否则不要把专有数据库驱动加入仓库。
- 在文档中以中性方式提及第三方商标。

### 文档

- README 应聚焦于应用功能、构建方式和当前稳定状态。
- 保持重构计划公开且诚实，让贡献者知道哪些地方最需要帮助。
- 添加截图前，请确认截图不包含私有连接名称或数据。

## English

Use this checklist before pushing QueryPaw to a public GitHub repository.

### Repository Hygiene

- Confirm `.gitignore` excludes build output, local packages, `.vs`, `artifacts`, archives, logs, `.wlb` files, and user session files.
- Start from `SqlAnalyzer.Next` only. Do not include the legacy WinForms workspace unless you intentionally publish it as a separate archival project.
- Run a clean restore and build from a fresh clone-like directory.

### Security Review

- Search for credentials, host names, tokens, and private connection strings before the first commit.
- Confirm no real `.wlb` connection exports are committed.
- Confirm credential storage does not rely on source-controlled fixed secrets for new local profile saves.
- Confirm `.wlb` export remains password-free unless a future passphrase-based export design is added.
- Treat database driver paths and native client paths as user configuration, not repository content.

### Licensing and Third-party Assets

- Confirm the chosen project license. The repository currently uses MIT.
- Verify the right to redistribute app logo assets and database logo assets.
- Keep proprietary database drivers out of the repository unless their license explicitly allows redistribution.
- Mention third-party trademarks neutrally in docs.

### Documentation

- Keep README focused on what the app does, how to build it, and what is stable.
- Keep the refactor plan public and honest so contributors know where help is most useful.
- Add screenshots only after checking they contain no private connection names or data.
