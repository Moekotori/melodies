# 专辑公开身份与元数据来源

日期：2026-10-02。

## 数据与迁移

新曲库的第六版迁移增加 `album_identity`、`album_metadata_values` 和有效名称 FTS。迁移由既有唯一 SQLite writer 执行，识别新应用 application ID，不打开旧 ECHO 生产库。

已有库升级前通过 SQLite Backup API 建立同目录 `*.before-v6-<ID>.sqlite` 一致性快照，包含 WAL 中已提交的数据。新建空库不额外备份。迁移使用事务；已有收藏/评分继续关联内部聚合键，不重建用户状态。

公开 `AlbumRow.Id` 使用独立稳定 ID，显示标题和艺术家使用有效元数据。内部原始分组键仍用于既有扫描聚合、艺术家成员关系和索引。旧聚合键仍可用于曲目与个人状态查询，保持已有引用兼容。

## 合并规则和真实入口

当前支持专辑标题与专辑艺术家两个字段，保留文件值、在线值和用户值。有效值优先级为用户、在线、文件；清除某个来源的值后回退到下一可用来源。每个读取结果包含全部来源与最终来源，不覆盖文件标签。

写入通过 `IAlbumMetadata` 进入既有唯一 writer，来源与字段枚举校验，单值不超过 1024 字符，修改与 library revision 同事务提交。重新扫描原始标签不会覆盖用户值。修改显示名称不改变公开 ID、收藏和评分；专辑暂时全部 missing 后保留身份，重新出现时恢复原 ID。

列表显示、名称排序与专辑 FTS 使用同一个持久化投影。专辑曲目与按路径读取的播放信息使用有效专辑名。分页仍最多 200 行；原始标签和曲目身份保持独立。

CLI 已提供实际入口：

```text
albums --db FILE [--search TEXT] [--limit 100]
album-metadata --db FILE --id ID
set-album-metadata --db FILE --id ID --field title|artist --source user|online --value TEXT
clear-album-metadata --db FILE --id ID --field title|artist --source user|online
```

例如先修改 user 标题，再按新名称搜索专辑，随后清除 user 标题，会恢复 online 或 file 值。online 表示存储来源，本轮没有自动连接在线元数据服务，也没有新增图形标签编辑器。

## 当前身份边界

本轮稳定的是现有专辑聚合的公开身份与显示编辑。尚未建立全局作品、发行版本或多份本地副本的关系模型。相同标题与专辑艺术家的不同版本仍可能共用原分组；原始标签导致成员重新分组时的身份关联也待进一步迁移，不能宣称已经达到 Roon 的完整身份模型。

## 短验证与回滚

`dotnet run --project tests/Echo.Library.Smoke -- metadata misc/metadata-check` 使用约 8 KiB 的生成数据 v5 压缩快照，覆盖真实旧格式升级与备份、收藏/评分、公开 ID、来源优先级/回退、有效搜索与播放信息、重扫、missing 恢复和取消。

测试从固定 v5 快照开始，不通过修改最新 schema 的版本号伪造旧库。后续并行迁移不需要从测试库逐个删列。临时数据与日志放在 `misc/architecture-validation/`。

schema 不能原地降级。回滚应在关闭新应用后恢复一致性备份；后续其他模块的迁移可能继续提升版本，不能直接交给旧版本打开。备份不会自动删除用户已有数据。

2026-10-02 本轮结果：`metadata-verified.out` 的真实 v5 升级、备份与元数据行为检查通过；`cli-complete.out` 的 CLI 构建为 0 警告、0 错误，实际 CLI 修改、搜索与清除回退也已通过。构建、检查及失败重跑累计约 80 秒，没有运行全量或长性能测试。后续并行 schema 迁移已接在本迁移之后，当前最终版本以 SchemaMigrator 为准；迁移验证使用固定 v5 夹具，不伪造新库版本号。
