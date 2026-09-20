# Broforce 构建 12964083 分析结论

- Steam App ID：`274190`。
- 可执行文件：`Broforce_beta.exe`，Windows 64 位。
- Unity 脚本后端：Mono；`Assembly-CSharp.dll` 可直接读取元数据和 IL。
- `HeroController.players` 是最多四名玩家的静态 `Player[]`。
- `Player.Lives` 是命数属性，普通模式使用私有 `int lives`，硬核和程序生成模式由属性访问器转发到各自的命数存储。
- `Player.AddLife()` 先确认玩家属于本机，再调用 `Lives = Lives + 1`，并调用 `PlayerHUD.ShowFreeLife()`。
- `Player.SetLivesRPC()` 会刷新 HUD 命数字段。
- `Connect.IsOffline` 可用于拒绝在线房间。

因此不做普通整数扫描，也不写绝对地址；普通模式使用游戏自身的 `Player.AddLife()`。实时监视由常驻 `MonoBehaviour` 在 Unity 正常的 `Update()` 阶段读取当前启用 HUD 的命数，再写入 40 字节的命名共享内存；外部界面只读缓存，不直接访问 Unity 对象。共享区包含普通加命和钢铁兄弟加命两个独立命令计数器，并保留心跳用于检测并自动修复切关后的监视中断。

## 钢铁兄弟命数

钢铁兄弟模式中，`Player.Lives` 转发到 `currentWorldmapSave.hardcoreLives`，但真正可用于重生的角色来自 `hardcoreModeAvailableBros`。单独调用 `Player.AddLife()` 会让二者失配：角色死亡后可能剩余数字命数，却没有可用角色，游戏既不结束也无法重生。

本机构建公开了 `HeroUnlockController.AreAnyMoreBrosAvailableToBeSavedInHardcoreMode()` 和 `HeroUnlockController.FreeBro()`。`Player.RemoveLife()` 在钢铁兄弟模式下先从 `hardcoreModeAvailableBros` 移除当前 `HeroType`，再追加到 `hardcoreModeDeadBros`；`HeroController.GetHeroType()` 没有待出场队列时直接从可用名单随机选择。因此复活角色只需执行死亡流程的反向名单迁移，不需要伪造救人事件或修改额外队列。

新版独立选项优先取死亡名单最后一名（最近死亡角色），将其移回可用名单；没有死亡角色时才调用 `FreeBro()` 加入尚未参战的新角色。确认可用角色名单恰好增加一人后，才在同步存档中调用 `Player.AddLife()`。如果检测到 `hardcoreLives` 已大于可用角色数，则本次只补一名角色以修复一个幽灵命数，不额外增加数字；死亡名单和未参战角色都为空时拒绝操作。

## 运行验证

2026-08-31 在本机 Steam 构建 `12964083` 完成冒烟测试：Mono 桥接加载成功，游戏窗口主线程收到自定义消息，返回 `OK|37028|0|2`，表示玩家索引 0 的命数已从 1 增加到 2。测试后只关闭了本次测试启动的 Broforce 进程。

实时监视版本用一次性消息 `0x8452` 在主线程创建监视组件，创建后立即恢复游戏原窗口过程，不保留窗口子类钩子。共享状态中命数值的高 32 位记录玩家索引加一，低 32 位记录当前命数；`0` 表示尚未进入关卡，`-1` 表示在线房间。另一个共享计数器传递 +1 命请求，由 `Update()` 执行。

早期实现曾持续保留窗口过程钩子，并在窗口消息回调中访问玩家对象；死亡和坐飞机过关时玩家对象会重建，用户观察到两次“未响应”。新版移除了这条持续钩子路径，并把过渡期读取异常降级为空快照。

2026-09-02 补强了切关恢复逻辑：4 秒心跳暂停只显示游戏忙，不立即重注入；心跳持续停止 15 秒才启动恢复。重连时会重置命名事件，桥接在重建监视器前也会清除共享内存中可能残留的就绪标记。

## 人物锁定

当前构建的 `HeroType` 普通人物编号为 `0..51`，`Final=52` 是普通人物范围的结束标记。`HeroUnlockController.GetUnlockedHeroes(true)` 可读取当前存档已解锁人物；`HeroController.GetCurrentHeroType(playerNum)` 可读取当前人物；`HeroController.ChangeBro(playerNum, heroType)` 会在保留坐标的情况下使用游戏自身的 `Player.SpawnHero()` 完成换人。

V4 桥接在共享状态中增加人物锁定目标和已解锁人物 64 位位图。它只接受位图中已解锁的普通人物，避免 `Player.SpawnHero()` 把未解锁人物加入存档。启用后只在本地玩家存活、人物对象存在且不处于复活/救人过渡时换人；人物已经匹配时不重复操作，失败重试间隔不短于 0.75 秒。

钢铁兄弟模式的随机人物来自 `hardcoreModeAvailableBros`，死亡后还会移动到 `hardcoreModeDeadBros`。固定同一人物会绕过永久死亡并造成名单/命数不一致，因此 V4 会在钢铁兄弟、在线房间或两名以上本地玩家时关闭人物锁定。2026-09-05 已完成接口反汇编核对、C# 编译和 Python 单元测试；实际死亡复活、救人换角和切关流程仍待游戏内冒烟测试。
