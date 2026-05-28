# Third-Party Notices / 第三方声明

[中文](#中文) | [English](#english)

## 中文

QueryPaw 使用了一些第三方开源组件、数据库驱动包和数据库标志资源。本文档用于记录这些组件的许可证和商标说明，便于公开源码和发布二进制版本时一并保留。

QueryPaw 自身代码使用 [MIT License](LICENSE) 开源。第三方组件仍受其各自许可证约束。

### 直接 NuGet 依赖

| 组件 | 版本 | 许可证 / 声明 |
| --- | --- | --- |
| Avalonia | 11.3.12 | MIT |
| Avalonia.Controls.DataGrid | 11.3.12 | MIT |
| Avalonia.Desktop | 11.3.12 | MIT |
| Avalonia.Diagnostics | 11.3.12 | MIT |
| Avalonia.Fonts.Inter | 11.3.12 | MIT |
| Avalonia.Themes.Fluent | 11.3.12 | MIT |
| Avalonia.AvaloniaEdit | 11.4.0 | MIT |
| AvaloniaEdit.TextMate | 11.4.0 | MIT |
| CommunityToolkit.Mvvm | 8.2.1 | MIT |
| Tmds.DBus.Protocol | 0.21.3 | MIT |
| TextMateSharp.Grammars | 2.0.2 | MIT |
| Microsoft.Data.SqlClient | 7.0.0 | MIT |
| MySql.Data | 9.6.0 | GPL-2.0-only WITH Universal-FOSS-exception-1.0 |
| Npgsql | 9.0.3 | PostgreSQL License |
| Oracle.ManagedDataAccess.Core | 23.26.100 | Oracle Free Distribution, Hosting, and Use Terms and Conditions，见 NuGet 包内 `LICENSE.txt` |

发布二进制包时，应确认随包保留各依赖所需的许可证文本、NOTICE 文件或链接。尤其是 Oracle 和 MySQL 相关驱动，应以其官方包内许可证和法律声明为准。

### 内置视觉资源

QueryPaw 仓库包含应用图标、工具栏图标，以及用于识别数据库类型的数据库标志资源。

- `software-logo.png`
- `software-logo.ico`
- `software-logo-source.png`
- `icon-save-check.png`
- `icon-edit-pencil.png`
- `icon-cancel-x.png`
- `DatabaseLogos/postgresql.svg`
- `DatabaseLogos/oracle.svg`
- `DatabaseLogos/mysql.svg`
- `DatabaseLogos/mongodb.svg`
- `DatabaseLogos/microsoftsqlserver.svg`
- `DatabaseLogos/kingbase-logo-icon.png`
- `DatabaseLogos/dameng-logo-icon.png`

数据库名称、产品名称、公司名称、标志和商标均归其各自所有者所有。QueryPaw 仅为识别数据库类型而使用这些名称和标志，不表示任何数据库厂商对本项目的认可、赞助或背书。

如果某个数据库标志来自第三方图标集合，应同时遵守该图标集合的许可证和对应厂商的商标规则。例如 Simple Icons 使用 CC0 许可证发布其图标集合，但其免责声明也指出，CC0 不代表相关品牌商标权被放弃或授权。

### 建议发布规则

- 源码仓库保留本文件、[LICENSE](LICENSE)、[README.md](README.md) 和 [SECURITY.md](SECURITY.md)。
- 二进制发布包保留本文件或等效的第三方声明。
- 如后续新增依赖或资源，请同步更新本文件。
- 如后续移除某个内置数据库标志，请同步删除对应声明。

## English

QueryPaw uses third-party open-source components, database driver packages, and database logo assets. This document records their licenses and trademark notices so they can be retained when publishing source code or binary releases.

QueryPaw's own source code is licensed under the [MIT License](LICENSE). Third-party components remain governed by their respective licenses.

### Direct NuGet Dependencies

| Component | Version | License / Notice |
| --- | --- | --- |
| Avalonia | 11.3.12 | MIT |
| Avalonia.Controls.DataGrid | 11.3.12 | MIT |
| Avalonia.Desktop | 11.3.12 | MIT |
| Avalonia.Diagnostics | 11.3.12 | MIT |
| Avalonia.Fonts.Inter | 11.3.12 | MIT |
| Avalonia.Themes.Fluent | 11.3.12 | MIT |
| Avalonia.AvaloniaEdit | 11.4.0 | MIT |
| AvaloniaEdit.TextMate | 11.4.0 | MIT |
| CommunityToolkit.Mvvm | 8.2.1 | MIT |
| Tmds.DBus.Protocol | 0.21.3 | MIT |
| TextMateSharp.Grammars | 2.0.2 | MIT |
| Microsoft.Data.SqlClient | 7.0.0 | MIT |
| MySql.Data | 9.6.0 | GPL-2.0-only WITH Universal-FOSS-exception-1.0 |
| Npgsql | 9.0.3 | PostgreSQL License |
| Oracle.ManagedDataAccess.Core | 23.26.100 | Oracle Free Distribution, Hosting, and Use Terms and Conditions; see `LICENSE.txt` in the NuGet package |

Binary distributions should retain the license text, NOTICE files, or links required by each dependency. Oracle and MySQL related drivers should be reviewed against the official license and legal notices shipped with their packages.

### Bundled Visual Assets

The QueryPaw repository includes app icons, toolbar icons, and database logo assets used to identify database types.

- `software-logo.png`
- `software-logo.ico`
- `software-logo-source.png`
- `icon-save-check.png`
- `icon-edit-pencil.png`
- `icon-cancel-x.png`
- `DatabaseLogos/postgresql.svg`
- `DatabaseLogos/oracle.svg`
- `DatabaseLogos/mysql.svg`
- `DatabaseLogos/mongodb.svg`
- `DatabaseLogos/microsoftsqlserver.svg`
- `DatabaseLogos/kingbase-logo-icon.png`
- `DatabaseLogos/dameng-logo-icon.png`

All database names, product names, company names, logos, and trademarks are property of their respective owners. QueryPaw uses these names and logos only to identify database types. Their use does not imply endorsement, sponsorship, or approval by the respective database vendors.

If a database logo comes from a third-party icon collection, follow both that icon collection's license and the corresponding vendor's trademark rules. For example, Simple Icons is released under CC0, but its disclaimer also states that CC0 does not waive or license the trademark rights associated with the represented brands.

### Release Guidance

- Keep this file, [LICENSE](LICENSE), [README.md](README.md), and [SECURITY.md](SECURITY.md) in the source repository.
- Include this file or an equivalent third-party notice in binary releases.
- Update this file whenever a dependency or asset is added.
- Remove an entry when the corresponding bundled database logo is removed.
