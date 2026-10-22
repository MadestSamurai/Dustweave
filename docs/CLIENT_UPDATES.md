# Client update checklist / 游戏更新适配

This checklist covers Dustweave's host and integrated tools. Run it once for a changed client/data release, or selectively after a concrete compatibility failure. It is not a full scan on every launch. Offline builds, package checks and live-game acceptance are separate.

本清单用于客户端大更新、热更新表及流程变化。更新时检查一次，有异常时定向复查，不把全量检查加入每次启动。连接成功、组件编译通过和真实日常完成分别记录。

## Current update / 本次更新

- 2026-10-22：离线核对客户端 `20261016000`，MVID `fd0d770c-6f10-4faf-b14d-e22668d15b70`，主程序集 SHA256 `4EF1573B4BE8D26737F34F00136F063C40CD7DDB586DAF221BA9D7A3356379BC`，数据库 SHA256 `6E8583DA355CBC81462FC3767AD94C29E2E1BE8DA235C5FB12CF46A915FB6BD1`。
- 公共观察组件、15个按需模块及全部11份 evidence spec 通过当前客户端离线生成。日常、钓鱼、连连看、领地、音游、塔防、抽抽乐、SECRET VISION、恶魔猎人、装备、MANSION RUNAWAY、潘多拉及日常复用模块均覆盖。没有将全量准备加入正常启动。
- 导出29张日常/跑商表；新跑商目录含119项食材/商品、309条报价配置、50个交易配方。与1.4.16目录相比，交易规则、制作配方和角色天赋能力映射不变，只有客户端身份及来源摘要变化。
- 修复跑商因安装包旧程序集身份而提前阻断：先确认观察中的进程及MVID与当前游戏一致，再只读导出当前表，验证来源、完整性和支持的公式后启用独立缓存。价格、溢价比例、限购、配方和角色天赋数值从当前表读取，变化不再要求发布新版本。相同输入复用已校验缓存；不复用旧计划或重放交易。新增不支持的货币／计价机制仍需适配。
- 302项定向跑商回归通过；使用1.4.16实际包的导出器和本机新版客户端文件，完成离线重新绑定验证，原包哈希未变。进程身份为合成测试数据，没有连接/启动游戏。新版主程序编译及源码边界检查通过。
- 活动批量战斗改以游戏当前终局状态为准；免费次数用尽后退出并继续队列，历史回执截断不再造成等待或接续死锁。扫荡读取当前免费次数变化。角色／进程身份与免费次数消费入口保持限制。
- 宾果接入游戏原生批量按钮，读取当前活动周期、棋盘、代币及结束状态；和拼图共用设置、邮箱补领依赖及盘面核对。支持首板无缓存、整板自动刷新和最终奖励耗尽，无固定活动编号。未知未来活动类型进入诊断与未完成说明，不再静默忽略。
- 隔离启动自动配置游戏程序与资源的只读本机映射。用户关闭所有游戏后，只需在普通官方启动器更新一次；后续隔离启动忽略旧的程序覆盖副本，登录信息、渠道配置和可写缓存仍然独立。首次迁移仅在该隔离空间空闲时进行，不删除任何旧数据。
- 沙盒更新同时核对 `LocalLow/Gamfs/BrownDust II/Data/t` 中的设计表覆盖副本：只替换游戏文件清单列出的、比本机下载旧的表，保留一个旧副本供恢复。文件复制后校验摘要并再次确认空间空闲，再原子替换；较新的沙盒热更新、用户数据库、账号、登录信息和其他缓存均不覆盖。只有安装目录版本一致不足以证明资源已更新。
- **尚未实机验证**：本次新流程没有连接、启动或操作游戏；当前客户端日常桥接编译通过，真实登录／普通与隔离连接／宾果动画与奖励过程仍需实际验收。没有发布织尘新版本。

English: the October 22 client passes offline compilation of all 15 on-demand modules and all 11 evidence specifications. Trading validates current table structure and supported formulas, then replans with current prices, recipes and limits; compatible value changes no longer require a release. Native event completion follows current game state rather than historical receipt completeness. Bingo uses current schedules, boards, tokens and native bulk actions, including first-board creation and automatic renewal. Unknown future mechanics are reported. Offline compatibility and synthetic validation do not substitute for live flow acceptance; no game was launched or controlled and no release was published.

### Previous update / 上次更新（2026-10-08）

- 2026-10-08 新客户端已安装并开服。242 个 Managed DLL 中主程序集发生变化，登录 SDK／框架保持相同；新 MVID 为 `132cda4f-d57f-4991-8b09-6310f09b7fe5`。
- 0.9.6 增加日常执行源码与证据配置的共同绑定契约，离线确认 407 类型／824 成员及完整日常配置的 164 项读取规则。证据路径与集合列名保留业务协议名称，反射时才转换成当前游戏名称。
- 普通启动已实测到 TOUCH TO START；两个隔离实例同时启动、各自连接观察组件，均停在 `AgreementPopupUI`，未确认协议、未进入游戏。没有发送日常操作；主机 14 个账号槽位文件哈希保持不变。
- 新客户端删除魔兽页 `_buttonQuickBattle` 字段。缺少入口只让该环节明确报告待处理，不影响其他环节准备，也不替代成实际战斗。新领取方式及全部业务流程仍待进入游戏后的定向验收。
- 0.9.4 的集中队列补位、账号登录后的身份验证和任务控制仍待本次更新后实测；开始画面连接不能代替这些结论。

English: the new client passes interface and read-path validation. Normal startup reached TOUCH TO START; two isolated instances connected independently but stopped at the agreement dialog. No daily actions were sent. The removed Fiend Hunt quick-battle entry is isolated and reported; in-game completion and concurrent queue scheduling remain pending.

## Update and multi-instance sequence / 更新与多开顺序

1. 结束任务，关闭 Dustweave 和所有游戏实例。从普通桌面启动官方启动器，完成一次正常更新；不要分别在多个沙箱中更新同一安装目录。
2. 下载完成后先离线核对：程序库增删及内容、主程序集 MVID、登录 SDK、Unity 与 Mono。保存前后指纹，再编译公共连接及所需日常组件。只准备本次需要的功能，不在每次启动扫描全部业务。
3. 先验证普通单开：启动、登录身份、只读连接、主菜单状态。真实任务另行验收，不能把“连接成功”记为日常通过。
4. 再验证两个不同账号的隔离启动：主机与沙箱所见程序文件一致、各自账号绑定正确、连接互不串号；暂停／继续一个账号不影响另一个。
5. 最后验证集中队列：三个账号、同时上限 2，结束后自动补位；单账号失败隔离；退出主窗口后执行器停止；只关闭本次创建并成功完成的游戏。
6. 新版表和 UI 行为按下方受影响项核对。更新后若接口确实变化，修复对应解析与流程，再重新打包；无需为了检查耗尽每日／每周机会。

English: close the host and all games, update once using the normal official launcher, compare installed inputs offline, then verify single-instance identity and read-only connection before two isolated accounts and queue scheduling. Keep account data and distinguish connection checks from completed gameplay.

### Implemented safeguards / 已实现保护

- 组件缓存使用全部 Managed DLL 的文件名、长度和内容 SHA256；登录 SDK 或框架单独变更也会失效。主程序集 MVID 另外保留用于诊断。
- 编译结束和注入前再次核对输入，防止边下载边编译；已知连接记录中的同一游戏进程若对应旧输入则停止重连，提示正常关闭旧进程后再开。没有历史记录时，磁盘指纹本身不能证明进程加载了哪个版本，因此仍要求更新前关闭所有游戏。
- 隔离启动在写入账号登录状态、启动游戏之前，比对主机与沙箱所见的全部 Managed DLL、游戏 EXE、UnityPlayer 和 Mono。发现旧覆盖文件时报告原因，不删除账号、不清空整个沙箱。检查结果保留在该隔离实例的 `client-update-check.json`。
- 归属与根目录确认后，空闲隔离空间自动加入 Sandboxie `ReadFilePath`：游戏 EXE、UnityPlayer、崩溃处理器、`BrownDust II_Data` 和 `MonoBleedingEdge`。该机制忽略旧覆盖文件并禁止隔离游戏改写本机程序；不包括 `launcher.settings`、注册表、LocalLow、账号、日志或缓存。规则已齐全时直接复用；中断后只补缺失规则。首次配置时空间仍在运行则提示关闭该账号的窗口并重试。
- 不一致仍会在账号导入与游戏启动前拦截。提示用户完成本机更新、关闭旧进程并从主窗口重试；保留账号与旧覆盖文件供诊断，不清空整个沙箱。只读路径不代替客户端身份检查。
- 指纹不覆盖所有资源包、热更新表或服务器行为；这些仍按表身份和现场证据单独验证。检查仅在准备／连接／启动时进行，不进入实时观察轮询。
- 每次打包记录客户端程序输入，在结束时再次核对；构建过程中游戏更新会使打包失败，防止交付混合版本。
- 日常执行与只读配置共享 `src/Connection/live-binding-contract.json`。生成契约必须使用与嵌入源码匹配的基线程序集；使用 `live generate-live-contract <baseline Managed> <output>` 显式生成，不能在新程序解析失败时把错误绑定反过来当作基线。只保存接口结构、引用关联和单向摘要，不提交游戏程序集或反编译正文。
- 绑定要求唯一解释，已知枚举值变化直接拒绝；只读路径按真实所属类型和泛型集合关系收集，不按全局同名字段猜测。成功名称映射按客户端内容、契约和编译器版本缓存，损坏缓存重建，避免每个日常环节重新扫描接口。
- 隔离启动使用与普通启动相同的渠道准备逻辑；备份位于各自环境，更新重置渠道后不需要先启动一次普通游戏才能运行隔离实例。
- 发布检查必须逐个生成所有 evidence spec，不能只确认桥接 DLL 编译通过。别名往返、集合列名、枚举漂移和被移除的 UI 入口由合成回归覆盖。

The cache includes all managed compiler references. Owned idle sandboxes use Sandboxie's read-only host view for immutable game code and resources, ignoring obsolete overlays while leaving account data and launcher channel settings private. Users update once through the normal launcher with all games closed. Isolated startup still compares managed and native inputs before importing sign-in state. It does not certify server behavior or an unobserved running process. See [Sandboxie ReadFilePath](https://sandboxie-plus.github.io/sandboxie-docs/Content/ReadFilePath/).

## Sources and outputs / 来源与目标

| Source / 来源 | Check / 检查 | Owner / 维护位置 |
| --- | --- | --- |
| Actual installed client Managed assemblies | Hash/MVID, Mono/dependency changes, methods/fields and native UI handlers; distinguish installed files from a running old process | `src/Compatibility`, `src/Connection`, `src/Core`, `tools/CompatibilityCli` |
| Actual installed game design tables | Compare affected table identities and values; match the exporter to the installed assemblies | `tools/TableExporter`, `tools/TradeData`, `package.ps1` |
| Captured live account/UI/task state | Compare prerequisites, transitions, success evidence, resource costs and recovery conditions | `assets/specs`, `assets/flows`, corresponding host/connection stages |
| Independent tool changes | Import reviewed source and preserve each tool's attribution/version | `standalone/`, `src/ToolHost`, `src/Core/DailySuite*` |
| Distribution | Refresh packaged daily data and trade catalog, test both flavors and updater | `package.ps1`, `scripts/new-update-*.ps1`, `scripts/test-update-packages.ps1` |

Private account captures, game assemblies and external plugin implementations do not enter the source repository or public documentation. Record only the necessary identities and sanitized results.

## Per-update checks / 逐项检查

Use one result per row: passed, unaffected with evidence, pending runtime, or blocked with a reason. Inspect only affected data and source paths.

### 1. Identify and prepare / 识别更新

- [ ] Record source commit, client build/MVID/hash, design-table identity and running process identity separately. Do not reuse evidence from an older installed client.
- [ ] Wait for downloads to complete before exporting data. Maintenance/pre-download work can finish offline, with live acceptance left pending.
- [ ] Revalidate connection compiler/cache keys and dependencies. A changed client must not reuse a payload compiled for different assemblies.
- [ ] Check title/login/loading handling, account identity and session ownership. Do not weaken account matching to make a new client connect.
- [ ] Confirm lazy preparation still builds only the features needed for the current task; do not prepare all tools at daily startup.

### 2. Daily and weekly behavior / 日常与周常行为

- [ ] Check reset timing, server-provided completion, free counts and rewards; invalidate only genuinely stale results.
- [ ] Check restaurant/business collection, special guests/bubbles, room rewards, dispatch collection and automatic redispatch.
- [ ] Check chapter/bonus hunts, stone selection, Mirror multipliers/free counts, result skip and return states.
- [ ] Check pass/event/returning-player tasks, roulette/free and paid-by-token counts, exchange shop and reward collection.
- [ ] Check weekly collection, NPC quests and theft as independently selected stages sharing a route; verify map/gate/teleport changes and server progress.
- [ ] Check weekly equipment crafting/refining, Book of Apocalypse and mini-game completion detection.
- [ ] Check regular/revival boss availability, highest eligible quick battle and event battle/sweep readiness.
- [ ] Check final mailbox collection and partial-queue resume. Existing passive notices must not become blockers after UI changes.

### 3. Tables and trading / 数据表与跑商

- [ ] Compare Cooking/Food/Product/SellItem/Shop/Talent tables, regenerate the trade catalog, and inspect prices, recipe output, talent medicine cost and bottleneck reserves.
- [ ] Refresh affected daily-name, mission, pass, event, gacha, equipment-making, dispatch, hunt, map, plaza and statue tables included by `package.ps1`.
- [ ] Validate changed translations and names in Simplified Chinese, Traditional Chinese and English.
- [ ] Unknown prices, quantities or consuming confirmations must not be guessed. Isolate the affected stage; unaffected stages remain independently usable.

### 4. Minimal live acceptance / 最小实机验收

- [ ] Only with authorization: read-only connection first, then verify changed stages against the actual game.
- [ ] Check preconditions, command acceptance, UI transition and server-observed completion. A dispatched command alone is not success.
- [ ] Do not consume every daily/weekly opportunity just to test. Start with changed flows; preserve one-time evidence and mark unavailable cases pending.
- [ ] Check one interrupted/resumed flow when feasible, without replaying an unknown consumption.
- [ ] Recheck account switching and the same-process handoff of any affected integrated tool.

### 5. Delivery / 交付

- [ ] Refresh source-manifest entries, run relevant regressions and source boundaries, and inspect changed UI in both themes and all three languages.
- [ ] Generate Portable/Lite through `package.ps1`; record package hashes and actual client identities.
- [ ] If changing distribution: generate signed full/delta metadata and validate upgrade, rollback and interrupted replacement before deployment.
- [ ] Record remaining runtime checks explicitly. Do not describe untested game behavior as fully compatible, and do not publish/deploy without authorization.


### Collection output compatibility / 集合输出兼容（0.9.11）

- Read-path resolution alone does not prove the consumer can read the serialized rows. Lists of client-owned objects must use `CollectionPath` / `ItemPaths` or a stable native DTO, rather than exporting a backing array with version-specific member names.
- Manifest generation checks output member shapes, including nested collections and inherited fields, before producing a usable configuration. Protobuf payloads retain their protocol JSON names. Explicit native `$self` readers keep their own stable DTO contract.
- Pass identifiers and list counts are required fields. Missing/incompatible values must not silently become zero or “no tasks.” Keep real duplicate-ID, account, active-pass and reward-proof checks.
- Regression coverage includes renamed item fields through the real projection, missing IDs, truncated collections and rejection of the former raw pass output. Captured account evidence remains private.
- This validation is part of component preparation and affected packaging, not per-frame observation or a repeated full daily run. It reduces name-only update regressions; a changed business rule or new API still requires targeted verification.
