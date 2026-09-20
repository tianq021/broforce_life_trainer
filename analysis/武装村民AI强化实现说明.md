# 武装村民 AI 强化 V1 / 桥接 V8

## V10 主动进攻补记

用户再次报告 V9 无主动攻击。读取本机 V9 日志确认多数周期为 no-shootable-target，同时曾出现 attack=firing/fire=True。日志并未确认弹丸生成，因此不能据此宣称上一版开火已验证。

本次将 IsEnemy 与 IsShootable 分离。SelectTarget 在更大的区域选取敌人，即使当前不能射击也保留作为进攻目标；GetInput 在玩家距离允许且无法直接开火时将路径目的地设为敌人格子，达到可射击条件后转入射击。优先当前可射击目标，同时保留近目标偏好。复用 PathAgent 跳跃/梯子/平台输入，不强制传送、不直接改坐标。

追赶阈值 256/112，目标距玩家横向限制 384、纵向 192；自身扫描范围 448/192，射击范围 288。连续两次无位移重寻路后放弃当前目标 4 秒，再重新选敌。射击周期原模板为 0.333，启用时降为 min(原周期,0.12)，关闭或回退时恢复捕获的原值。没有更改血量或弹丸伤害。

射线遮挡改用与原生 FollowerMinion.CanSeeEnemy 一致的 Terrain 层；仅在不存在该层时回退到 groundLayerStrict。此前扩大掩码的行为可能造成额外拒绝，不在未知碰撞层语义上继续叠加过滤。

日志每 3 秒记录 scanned/hostile/range/shootable/rejectedTeam/rejectedState、attack/fire、fireRate/nativeFireCounter、actorXY/targetXY，为后续实际运行定位提供证据。fire=True 仍只是输入，不能当作弹丸成功生成。

版本标识统一升级 V10，成品 Broforce_加一命修改器_V10.exe。新增模拟场景覆盖射程外主动接近、不同高度接敌、到位开火、隔墙接近、墙消失后开火、活动边界以及射速开启/恢复。真实引用编译及回归测试通过，未进行游戏内实战验证。

## V9 修复补记

用户实测 V8 只跟随、不攻击；本机 V8 日志持续出现 target=0。重新读取 networkobjects.assetbundle 确认 zmook、zmookbazooka 的 Unit.height 均为 6、阵营 _playerNum=-1。证据保存于 `enemy_hitbox_samples.json`。

V8 的 `muzzleY <= targetY + height` 对同层普通兵等价于 `9 <= 6`，错误地排除目标。V9 改为原生 GeLivingtUnit 使用的范围：extent=min(height,4)+height*0.5；center=targetY+extent*0.5+5；abs(center-muzzleY)<extent+1。模拟测试默认敌人高度由错误假设的 16 改为真实值 6。

追赶不再无条件禁止 fire：保持路径输入，目标在行进方向且已面向目标时允许开火。追赶选敌时跳过反向候选；避免为了开火扭转移动方向。遮挡掩码使用 Terrain | Map.groundLayerStrict，排除包含可攻击大物体的 LargeObjects 层。

协议名称升级 V9、程序集版本 9.0.0.0；输出 `Broforce_加一命修改器_V9.exe`，以保留用户可能仍在运行的 V8 成品。新增日志字段 attack/fire/targetHeight。真实引用编译和回归测试通过不等同于游戏内攻击已确认；需重启后重新生成单位验证。

日期：2026-09-11。实现基于本机实际 Assembly-CSharp.dll 的反射及 IL，不覆盖原始游戏程序集。

## 原逻辑与修改原因

`Villager.SetSpawnState → ArmVillager → ArmVillagerRPC → VillagerAI.EnterMinionMode` 完成武装。随后 `TestVanDammeAnim.GetEnemyMovement → enemyAI.GetInput → VillagerAI.GetInput → FollowerMinion.GetInput` 产生方向、跳跃和 fire 输入。射击仍进入游戏自身的武器流程及 `Villager.SpawnPrimaryProjectile`；本次不创建弹丸、不加攻击伤害。

`FollowerMinion.GetInput` 默认查找右侧检查点；离队后设置 waitingForBroToCatchUp 并可能等待 3–6 秒。看到敌人又会清除左右输入，并将移动延迟改为 0.1–1.4 秒。这解释了为什么只扩大视野可能反而加重掉队。

原 `CanSeeEnemy` 调用 `Map.GeLivingtUnit`，返回列表中第一个符合范围的单位，之后才排除动物、检查 Terrain 遮挡。因此动物或被挡住的候选可能让本次索敌提前失败。原生已有遮挡检测，此次改善的是候选选择、双向范围以及枪口的水平射线。

`PathAgent.GetPath(col,row,timeLimit)` 调用 `PathfindingController.FindPath`；最后一个参数的实际名称为 `timeLimitInMilliseconds`。`GetMove` 负责执行路径。新版本直接复用这些接口，超时仍由原生寻路器返回结果。

## 接入方式

确认生成实例已武装且所属玩家正确后，为实例添加 `EnhancedVillagerAI : VillagerAI`。保留原始 VillagerAI；启用时仅将该实例公开的 `enemyAI` 引用切换到新组件，停用原组件的更新，避免两份逻辑竞争 PathAgent。关闭时恢复引用、原组件 enabled 状态并清除旧路径。

增强组件不运行通用巡逻思考队列，也不注册为独立网络对象。其 OnDestroy 只恢复本地引用，不调用会发送网络销毁 RPC 的基类实现。原组件保留自身的生命周期。

每次玩家角色对象改变，更新 FollowerMinion.leader 和 leaderPlayerNo，并将村民登记至正确 Player.minions，清除其他本地玩家的错误登记。所属玩家死亡/尚未加入/救人或复活过渡期间停止强化输入，不改投其他玩家。

只对本次修改器生成且确认武装的实例安装组件。AI 出错后锁定为原生回退，避免逐帧反复启用故障组件；重新生成新的村民才会重新尝试。原生游戏生成的村民不接管。

## 决策与预算

1. 存活、离线、地图一致、未暂停、未失明且所属玩家可用时运行。
2. 玩家水平距离加垂直距离超过 112 世界单位进入追赶，降至 64 以内退出，减少临界值抖动。
3. 每 0.15 秒遍历 Map.units；射程 192，按水平距离加双倍垂直距离评分，当前目标减 12。候选须通过阵营、存活、隐身/无敌、枪口高度和地形遮挡检查。
4. 可战斗时先转身，再开火。每帧复核当前目标，地形变化立即停止无效射击；追赶及攀爬阶段优先完成移动。
5. 无战斗且距玩家横向超过 28 或纵向超过 12 时，沿原生路径追随玩家当前格子。没有路径时不强推角色向悬崖移动。
6. 需要移动但 1.8 秒位移不足 4 单位时，标记重新寻路。仅站着射击不会被误判为卡死。
7. 每个单位寻路至少间隔 0.65 秒；全队 FIFO 队列每帧最多处理一次，间隔至少 0.035 秒，单次 2 毫秒。队列避免更新顺序靠后的村民一直拿不到寻路机会。

## 文件与协议

- `managed/EnhancedVillagerAI.cs`：实例接管、选敌、跟随、重寻路、日志和回退。
- `managed/VillagerTactics.cs`：无 Unity 依赖的决策阈值。
- `managed/BroforceLifeBridge.cs`：生成确认后挂载和各玩家开关处理。
- `trainer_gui.py`：AI 勾选开关和各玩家状态读取。
- V8 共享区仍为 320 字节，每玩家 80 字节；槽内 offset 72 为 AI 禁用标记，0=强化，1=原生。新共享区默认开启。命令不复用生成计数器。
- 命名共享区、就绪事件、状态文件升级 V8，程序集版本 8.0.0.0。
- `villager_ai_v8.log` 位于 LocalAppData/BroforceLifeTrainer，状态日志约每村民 10 秒一次，切换/重新绑定/卡死/错误即时记录，文件约 1 MB 后重置。

## 验证与边界

`build.ps1` 编译真实游戏引用后，执行桥接四玩家内存检查、`VillagerAiSmoke.cs` 模拟场景测试、19 项 Python 界面/协议/快捷键回归，再打包 EXE。

模拟测试覆盖：背后近敌、先转身后开火、新增墙体阻挡缓存目标、被挡近敌之后选择可见远敌、玩家归属、换人重新绑定、开关回退、掉队优先、无移动输入的卡死、暂停、所属玩家死亡、在线状态、异常回退不重试、场景变化、20 人队列公平性及阈值滞回。

这些测试不能验证 Unity 引擎内的全部组件生命周期、动画、枪口实际位置及寻路效果。平台/梯子/载具/过关动画/特殊关卡、毒气及其他异常状态仍需游戏内测试。当前不实现爆炸物避让、近战后撤、复杂目标威胁评分或队形；路径不可达时也不传送村民。

## 游戏内测试步骤

1. 重启 Broforce 与修改器，普通离线关卡连接；F8 生成一名，确认提示强化开启。
2. 让敌人分别在左、右侧，确认转向后开火；隔墙敌人不持续射击，旁边有无遮挡敌人时能换目标。
3. 向左右移动并拉远距离，确认优先追赶；再测单层平台、梯子与墙角，观察约两秒后的重寻路。
4. 换英雄、死亡复活后观察重新跟随；本地两玩家分别生成，确认各随各的玩家。
5. 关闭当前玩家的开关，确认恢复原生；另一玩家的开关及村民不受影响。
6. 从 3 名增加到 10–20 名，观察帧率、各单位是否仍获得路径；切关不应遗留单位或重放生成请求。

## 回滚

临时回滚：取消对应玩家的 AI 勾选，已有和以后生成的单位使用原生行为。

成品回滚：退出游戏和修改器，运行 `backups/before_ai_20260911/Broforce_加一命修改器.exe`。旧版包含自己的 DLL，不需写入 Steam 目录。

源码回滚：备份内 trainer_gui.py、BroforceLifeBridge.cs/DLL、build.ps1 为修改前版本；对应还原至根目录、managed 目录后即可按旧构建流程构建。新增 AI 源码不被旧 build.ps1 引用。README 与新增分析/测试文件不是旧版成品运行依赖。

IL 证据：`follower_full_il.txt`、`villager_full_il.txt`、`path_agent_full_il.txt`、`pathfinding_il.txt`、`living_target_il.txt`、`villager_body_full_il.txt`、`hero_body_il.txt`。接口元数据见 `ai_types.txt`，只读检查工具为 InspectVillager / InspectAiTypes。
