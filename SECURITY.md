# Security Policy / 安全策略

[中文](#中文) | [English](#english)

## 中文

### 支持版本

QueryPaw 目前仍处于 1.0 之前的阶段。除非后续引入发布分支，安全修复应优先面向当前 `main` 分支。

### 报告安全问题

如果你怀疑存在安全问题，请通过仓库所有者偏好的私密联系方式报告。请不要在公开 issue 中发布可执行利用方式、真实数据库凭据或连接导出文件。

报告安全问题时，请尽量包含：

- 问题的简短描述。
- 受影响的平台和数据库提供方，如适用。
- 使用脱敏数据复现问题的步骤。
- 已移除敏感信息的日志。

### 敏感数据规则

- 不要提交 `.wlb` 文件、已保存连接档案、编辑器会话文件、日志、打包产物或数据库驱动二进制文件。
- 避免分享包含主机名、用户名、schema 名称、生产数据或敏感业务 SQL 的截图。
- 数据库厂商名称和标志可能是其各自所有者的商标。文档中应保持中性表述，不暗示厂商背书。

### 凭据存储

QueryPaw 可以在本地保存连接档案。已保存的密码会使用首次运行时生成的本机用户密钥加密；这个设计用于本地使用便利，不应被视为企业级密钥管理方案。

`.wlb` 导出文件会刻意移除密码，确保导出的连接文件不携带数据库凭据。导出文件仍可能包含主机名、用户名、数据库名、schema、驱动路径和备注，因此仍应作为敏感文件处理。

## English

### Supported Versions

QueryPaw is currently pre-1.0 software. Security fixes should target the current `main` branch unless release branches are introduced later.

### Reporting a Vulnerability

Please report suspected security issues privately through the repository owner's preferred contact channel. Do not publish working exploits, real database credentials, or connection export files in public issues.

When reporting a vulnerability, include:

- A short description of the issue.
- Affected platform and database provider, if relevant.
- Steps to reproduce with sanitized data.
- Any logs with secrets removed.

### Sensitive Data Rules

- Never commit `.wlb` files, saved connection profiles, editor session files, logs, packaged binaries, or database driver binaries.
- Avoid sharing screenshots that show host names, usernames, schema names, production data, or SQL containing sensitive business logic.
- Database vendor names and logos may be trademarks of their respective owners. Do not imply endorsement by those vendors.

### Credential Storage

QueryPaw can store connection profiles locally. Saved passwords are encrypted with a user-local key generated on first use, which is intended for local convenience rather than enterprise secret management.

`.wlb` exports intentionally omit passwords so exported connection files remain portable without carrying database credentials. Exported files can still contain host names, usernames, database names, schemas, driver paths, and notes, so treat them as sensitive.
