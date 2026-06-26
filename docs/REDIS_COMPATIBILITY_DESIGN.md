# Redis 兼容性设计

## 目标

让 QueryPaw 支持 Redis 连接、命令执行和结果查看，同时不影响现有 Oracle、SQL Server、MySQL、MariaDB、PostgreSQL、KingbaseES、Dameng、SQLite 等关系型数据库功能。

Redis 是 key-value / 数据结构服务器，不是关系型数据库。最低风险方案是保留当前 ADO.NET 关系型执行链路不变，为 Redis 增加独立执行链路，但把执行结果转换成现有 `QueryExecutionResult` / `QueryResultSet`，继续复用当前结果集表格。

## 当前架构适配点

- 数据库类型集中注册在 `DatabaseProviderCatalog`。
- `ConnectionProfile` 已有可复用字段：`Server`、`Port`、`Database`、`UserName`、`Password`、`AdvancedOptions`、`SavePassword`。
- 当前 SQL 执行在 `SqlExecutionService` 中，核心基于 ADO.NET。
- `DatabaseProviderDefinition.Kind` 已经预留非关系型能力，MongoDB 目前以 `Document` 注册并被 SQL 执行拦截。
- 结果渲染层只依赖 `QueryResultSet`，对数据来源不敏感。
- 关键词补全已按 provider 分流，适合新增 Redis 命令补全。

结论：Redis 应该作为非 ADO.NET provider 接入，不能硬塞进 `DbProviderRuntime.ResolveFactory`。

## Provider 定义

新增 Redis provider：

| 字段 | 建议值 |
| --- | --- |
| `Name` | `Redis` |
| `DisplayName` | `Redis` |
| `Kind` | `KeyValue` |
| `DriverFamily` | `Redis` |
| `SupportLevel` | `Experimental` |
| `RecommendedDriver` | `StackExchange.Redis` |
| `DefaultManagedDriver` | `StackExchange.Redis` |
| `TestSql` | `PING` |

能力开关建议：

- `SupportsExplain = false`
- `SupportsExportInsert = false`
- `SupportsDataEdit = false`，第一阶段不做可视化编辑
- `SupportsDirectTableAlter = false`
- 表、视图、存储过程、触发器、序列、包等关系型对象能力全部关闭

## 驱动选择

使用 `StackExchange.Redis`，添加到 `SqlAnalyzer.Data`。

依据：

- Redis 官方 .NET 文档使用 `ConnectionMultiplexer` 和 `ConfigurationOptions` 连接 Redis，支持 host、username、password、cluster endpoints、TLS 等配置，并强调不要频繁打开关闭连接，应复用连接。
- StackExchange.Redis 官方配置文档支持 `user`、`password`、`ssl`、`sslHost`、`connectTimeout`、`syncTimeout`、`asyncTimeout`、`serviceName`、`abortConnect` 等配置项。

参考：

- https://redis.io/docs/latest/develop/clients/dotnet/connect/
- https://stackexchange.github.io/StackExchange.Redis/Configuration.html

## 连接字段映射

复用现有 `ConnectionProfile`，避免模型迁移：

| QueryPaw 字段 | Redis 含义 |
| --- | --- |
| `Server` | Host；后续可支持逗号分隔多个 endpoint |
| `Port` | 端口，默认 `6379` |
| `Database` | DB index，默认 `0` |
| `UserName` | Redis ACL 用户，可为空；为空时使用 default user |
| `Password` | Redis password / ACL password |
| `AdvancedOptions` | StackExchange.Redis 配置项，例如 `ssl=true,connectTimeout=5000,abortConnect=false` |
| `AuthenticationMode` | 第一阶段固定 `Default` |
| `Schema` | Redis 不使用，UI 应隐藏 |

连接编辑 UI 调整：

- Redis 默认端口：`6379`。
- `Server` 标签显示为 `Host`。
- `Database` 标签显示为 `DB Index`。
- 隐藏 `Schema` 输入。
- 保留用户名、密码、记住密码和高级参数。
- 导入/导出连接配置无需新增字段。

Redis ACL 兼容：

- Redis 6+ 支持 `AUTH <username> <password>`。
- 旧的单密码认证等价于 default 用户，因此用户名必须允许为空。

## 连接生命周期

新增 Redis 连接管理组件：

- `RedisConnectionOptionsBuilder`
- `RedisConnectionManager`

职责：

- 从 `ConnectionProfile` 构造 `ConfigurationOptions`。
- 将 `Database` 解析为 int，解析失败时提示用户。
- 使用 `ConfigurationOptions.Parse` 解析 `AdvancedOptions`，再显式覆盖 UI 字段，保证界面字段优先级更高。
- 以 profile endpoint 为 key 缓存 `ConnectionMultiplexer`。
- profile 修改、删除或应用退出时释放 multiplexer。
- 不允许每次执行命令都重新连接 Redis。

## 执行链路设计

在 `SqlExecutionService.ExecuteAsync` 入口按 provider 分流：

```text
SqlExecutionService.ExecuteAsync
  -> provider = _runtime.GetProvider(profile)
  -> provider.Name == "Redis" 时调用 RedisExecutionService
  -> 其它 provider 继续走现有 ADO.NET 路径
```

第一阶段可以先用内部类或私有服务实现 Redis 分支，减少接口改动。等 MongoDB 重新启动时，再抽象成通用 `IProviderCommandExecutor`。

## Redis 命令语法

继续使用现有文本编辑器，但语义是 Redis command，不是 SQL。

示例：

```redis
PING
GET user:1
SET user:1 "{\"name\":\"Tom\"}" EX 3600
HGETALL session:abc
SCAN 0 MATCH user:* COUNT 100
TTL user:1
```

解析规则：

- 默认一行一条命令。
- 空行和以 `#` 开头的行忽略。
- 支持带空格的引号字符串。
- 支持转义引号。
- 分号只在引号外作为可选命令结束符。
- 不复用 SQL splitter，避免 SQL 语义污染 Redis 命令。

## 第一阶段支持范围

第一阶段支持通过 StackExchange.Redis `ExecuteAsync` 执行常规命令，并增加风险保护。

直接允许：

- 读命令：`PING`、`GET`、`MGET`、`EXISTS`、`TYPE`、`TTL`、`PTTL`、`INFO`、`DBSIZE`
- 常见写命令：`SET`、`DEL`、`EXPIRE`、`HSET`、`LPUSH`、`SADD`、`ZADD`
- 结构读取：`HGETALL`、`LRANGE`、`SMEMBERS`、`ZRANGE`、`XRANGE`
- key 遍历：`SCAN`

第一阶段阻断或必须二次确认：

- `FLUSHALL`
- `FLUSHDB`
- `SHUTDOWN`
- `CONFIG SET`
- `CONFIG REWRITE`
- `ACL SETUSER`
- `ACL DELUSER`
- `SCRIPT KILL`
- `CLIENT KILL`
- `CLUSTER` 的变更类子命令
- `DEBUG`
- `MONITOR`
- 长阻塞命令：`SUBSCRIBE`、`PSUBSCRIBE`、`XREAD BLOCK` 等

## 结果映射

Redis 响应转换成 `QueryResultSet`：

| Redis 响应 | QueryPaw 结果形态 |
| --- | --- |
| 简单字符串 / bulk string / integer | 单列 `Result` |
| null | `Result = (null)` |
| array | `Index`, `Value` |
| nested array | `Index`, `Value`，嵌套值序列化为紧凑文本 |
| `HGETALL` 风格键值对 | `Field`, `Value` |
| `SCAN` | `Cursor`, `Key`，后续可补 `Type`, `TTL` |
| `INFO` | `Section`, `Name`, `Value` |
| 错误 | 复用当前错误结果集风格，显示命令序号和错误信息 |

安全限制：

- 复用现有 `MaxPreviewRows`。
- 单元格显示长度限制，例如 64 KB。
- 二进制值优先尝试 UTF-8，失败时显示 hex/base64 预览和原始字节长度。
- 被截断时设置 `IsPreviewTruncated`。

Redis keyspace 遍历必须使用 `SCAN`，不要使用 `KEYS`。Redis 官方文档说明 `SCAN` 是 cursor-based 增量遍历，完整遍历仍然是 O(N)，所以 UI 必须分页和懒加载：

- https://redis.io/docs/latest/commands/scan/

## 对象树设计

第一阶段不做完整 key 浏览器，只保证连接节点、命令执行、结果展示。

第二阶段再做懒加载对象树：

```text
Redis connection
  DB 0
    Strings
    Hashes
    Lists
    Sets
    Sorted Sets
    Streams
    Other
```

规则：

- 展开连接时不预加载所有 key。
- 展开类型节点时才扫描 key。
- 优先使用 `SCAN cursor MATCH pattern COUNT n TYPE type`。
- 默认每页 200 个 key。
- cursor 不为 `0` 时显示 `Load more` 虚拟节点。
- Redis Cluster 默认只支持 DB 0。

Redis 官方数据类型包含 strings、hashes、lists、sets、sorted sets、streams、JSON、time series 等。第二阶段先支持内置核心类型，模块类型先归入 `Other`：

- https://redis.io/docs/latest/develop/data-types/

## 补全设计

在 `SqlCompletionKeywordProvider` 中新增 Redis profile：

- 仅当 `providerName == "Redis"` 时启用。
- 不合并 SQL common keywords。
- 第一阶段只补全 Redis 命令。

建议命令：

```text
GET, SET, DEL, EXISTS, EXPIRE, TTL, PTTL, TYPE, SCAN,
HGET, HGETALL, HSET,
LRANGE, LLEN,
SMEMBERS, SCARD,
ZRANGE, ZCARD,
XINFO, XRANGE,
INFO, DBSIZE, PING
```

key 补全不要启动时全量扫描。后续如需支持，只做 prefix-based `SCAN MATCH <prefix>* COUNT 100`，并设置开关。

## 功能屏蔽

Redis 连接下禁用或隐藏这些关系型功能：

- 注释维护
- 模型图
- 表设计
- 对象编辑器
- 直接改表
- 可编辑结果集保存/删除
- 执行计划
- 导出 insert SQL
- schema 工作台

保留：

- 文本编辑器
- 执行按钮和快捷键
- 结果集表格
- 结果复制、导出 CSV/JSON
- 查询历史，名称可沿用但文案后续应改为“执行历史”

## 实施阶段

### 第一阶段：Redis 命令执行

交付内容：

- 注册 Redis provider。
- 添加 `StackExchange.Redis` package。
- 新增 Redis connection option builder。
- 新增 Redis multiplexer manager。
- 新增 Redis command parser。
- 新增 Redis execution service 和 result mapper。
- `SqlExecutionService` 只对 Redis 分支。
- 连接测试执行 `PING`。
- UI 增加 Redis 默认端口和字段标签。
- Redis 命令补全。
- 屏蔽关系型专属功能。

验证点：

- 现有关系型数据库编译和冒烟测试不回退。
- Redis standalone 无密码：`PING`、`SET`、`GET`、`DEL`、`SCAN` 可用。
- Redis password-only 认证可用。
- Redis ACL username/password 认证可用。
- TLS 可通过 `AdvancedOptions` 配置。
- 高风险命令被阻断或需要二次确认。

### 第二阶段：懒加载 key 浏览器

交付内容：

- DB index 节点。
- 类型分组节点。
- `SCAN` 分页加载 key。
- `Load more` 虚拟节点。
- 打开 key 详情并以结果集显示。
- key prefix 搜索。

验证点：

- 大 keyspace 展开不会卡 UI。
- `SCAN` cursor 分页正确。
- 不同 Redis 类型展示正确。

### 第三阶段：key 编辑工具

交付内容：

- string/hash/list/set/zset 基础编辑。
- TTL 编辑。
- rename/delete 二次确认。
- 可选导入/导出 key value。

验证点：

- 编辑后值和 TTL 正确。
- 删除、flush 等危险操作必须确认。
- 二进制值不被破坏。

## 主要风险和控制

| 风险 | 控制方式 |
| --- | --- |
| Redis 不是 ADO.NET | 独立 Redis 执行分支，不改关系型主链路 |
| 大 keyspace 卡 UI | 只用 `SCAN` 分页，不用 `KEYS` |
| 频繁连接导致资源占用高 | 复用 `ConnectionMultiplexer` |
| 高风险命令误执行 | blocklist 或二次确认 |
| Redis Cluster DB index 差异 | Cluster 默认 DB 0 |
| 二进制值显示异常 | 文本检测 + hex/base64 预览 |
| ACL 权限不足 | 连接测试和命令错误提示要明确 |
| Redis 补全污染 SQL 编辑 | Redis profile 不合并 SQL 关键词 |

## 推荐第一版宣传口径

第一版 Redis 支持建议描述为：

> 支持 Redis 连接、命令执行、结果查看和基础命令补全。

暂时不要宣传为完整 Redis GUI 或 key 管理器。这样符合 QueryPaw 当前“查询分析器”的定位，也能把实现风险控制在可验证范围内。
