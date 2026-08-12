# 更新记录

## [未发布] - 2026-08-12

### 插件市场（PCL2-Nex）

- 插件市场加载顺序调整：先加载 Nex_Server 官方索引（`plugin-market.json`），实时 GitHub 搜索仅补充索引中缺失的仓库，避免重复请求；GitHub 限流/超时时，索引本身作为商店列表兜底。
- 移除仓库级本地磁盘缓存（`RepositoryCacheTtl` / `ReadCache` / `WriteCache` / `SafeFileName`）及过期的 sources 缓存回退；仓库统计获取失败时静默降级，不再拖累条目。
- 新增 `ParseRepositoryFullName` 与 `LoadIndexAndTopicForTestingAsync` 测试钩子；测试覆盖索引优先组合、GitHub 失败兜底与仓库跳过逻辑。

### Nex_Server 索引生成

- 新增 GitHub Actions 工作流，通过两条错开的 3 小时 cron 实现每 90 分钟生成一次 `apiv2/plugin-market.json`（POSIX cron 无法直接表达 90 分钟间隔）。
- 新增 `scripts/index_plugin_market.py`：将 pclnexplugin 主题下通过契约校验的仓库内联为静态索引，客户端读取单个 JSON 文档即可，避免启动时高频调用 GitHub API。
