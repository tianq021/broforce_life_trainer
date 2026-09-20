using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Threading;

[assembly: AssemblyVersion("12.0.0.0")]

namespace BroforceLifeTrainer
{
    /// <summary>
    /// Loaded into Broforce's Mono runtime by the external trainer.
    /// A one-shot window message creates a persistent MonoBehaviour on Unity's
    /// main thread. The window hook is then removed immediately. Lives and
    /// commands are exchanged through a tiny named shared-memory block.
    /// </summary>
    public static class Bootstrap
    {
        public const uint InstallMonitorMessage = 0x8452; // WM_APP + 0x452

        private const int GwlWndProc = -4;
        private const uint GwOwner = 4;
        private const uint PageReadWrite = 0x04;
        private const uint FileMapWrite = 0x0002;
        private const int ErrorAlreadyExists = 183;
        private const int SharedMemorySize = 352;
        private const int LocalPlayerCount = 4;
        private const int PlayerStride = 88;
        private static int currentPlayer;
        private sealed class PlayerState
        {
            public long lastHeroSwitchCommand;
            public float nextHeroSwitchTime;
            public long lastSpawnVillagerSequence;
            public long lastSpawnCompanionSequence;
            public float nextVillagerSpawnTime;
            public long lastAddLifeSequence;
            public long lastIronbroAddLifeSequence;
            public long lastHeroLockTarget = Int64.MinValue;
            public HeroType activeHeroLock = HeroType.None;
            public HeroType reportedHeroLockType = HeroType.None;
            public int reportedHeroLockPlayer = -1;
            public float nextHeroLockAttemptTime;
            public Villager pendingVillager;
            public int pendingVillagerPlayer;
            public float pendingVillagerDeadline;
        }
        private static readonly PlayerState[] playerStates = new PlayerState[] {
            new PlayerState(), new PlayerState(), new PlayerState(), new PlayerState()
        };

        private const int LivesSnapshotOffset = 0;
        private const int AddLifeSequenceOffset = 8;
        private const int MonitorReadyOffset = 16;
        private const int MonitorHeartbeatOffset = 24;
        private const int IronbroAddLifeSequenceOffset = 32;
        private const int HeroLockTargetOffset = 40;
        private const int UnlockedHeroMaskOffset = 48;
        private const int SpawnVillagerSequenceOffset = 56;
        private const int HeroSwitchCommandOffset = 64;
        private const int VillagerAiDisabledOffset = 72;
        private const int SpawnCompanionSequenceOffset = 80;
        private static long lastHeroSwitchCommand { get { return playerStates[currentPlayer].lastHeroSwitchCommand; } set { playerStates[currentPlayer].lastHeroSwitchCommand = value; } }
        private static float nextHeroSwitchTime { get { return playerStates[currentPlayer].nextHeroSwitchTime; } set { playerStates[currentPlayer].nextHeroSwitchTime = value; } }
        private static long lastSpawnVillagerSequence { get { return playerStates[currentPlayer].lastSpawnVillagerSequence; } set { playerStates[currentPlayer].lastSpawnVillagerSequence = value; } }
        private static float nextVillagerSpawnTime { get { return playerStates[currentPlayer].nextVillagerSpawnTime; } set { playerStates[currentPlayer].nextVillagerSpawnTime = value; } }
        private static Map villagerMap;
        private static readonly System.Collections.Generic.List<Villager> spawnedVillagers = new System.Collections.Generic.List<Villager>();
        private const int PlayableHeroTypeCount = (int)HeroType.Final;
        private const int MonitorHealthProbeMilliseconds = 1000;
        private const float HeroLockRetrySeconds = 0.75f;

        private static readonly int CurrentPid = Process.GetCurrentProcess().Id;
        private static readonly string AppDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BroforceLifeTrainer"
        );
        private static readonly string StatusPath = Path.Combine(AppDataDirectory, "bridge_status_v12.txt");

        private static WndProcDelegate windowProcedure;
        private static IntPtr originalWindowProcedure = IntPtr.Zero;
        private static IntPtr gameWindow = IntPtr.Zero;
        private static IntPtr readyEvent = IntPtr.Zero;
        private static IntPtr sharedMemoryHandle = IntPtr.Zero;
        private static IntPtr sharedMemoryView = IntPtr.Zero;
        private static long lastAddLifeSequence { get { return playerStates[currentPlayer].lastAddLifeSequence; } set { playerStates[currentPlayer].lastAddLifeSequence = value; } }
        private static long lastIronbroAddLifeSequence { get { return playerStates[currentPlayer].lastIronbroAddLifeSequence; } set { playerStates[currentPlayer].lastIronbroAddLifeSequence = value; } }
        private static long lastHeroLockTarget { get { return playerStates[currentPlayer].lastHeroLockTarget; } set { playerStates[currentPlayer].lastHeroLockTarget = value; } }
        private static HeroType activeHeroLock { get { return playerStates[currentPlayer].activeHeroLock; } set { playerStates[currentPlayer].activeHeroLock = value; } }
        private static HeroType reportedHeroLockType { get { return playerStates[currentPlayer].reportedHeroLockType; } set { playerStates[currentPlayer].reportedHeroLockType = value; } }
        private static int reportedHeroLockPlayer { get { return playerStates[currentPlayer].reportedHeroLockPlayer; } set { playerStates[currentPlayer].reportedHeroLockPlayer = value; } }
        private static float nextHeroLockAttemptTime { get { return playerStates[currentPlayer].nextHeroLockAttemptTime; } set { playerStates[currentPlayer].nextHeroLockAttemptTime = value; } }
        private static int currentMonitorGeneration;
        private static bool windowHookInstalled;
        private static bool monitorInstalled;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
        private delegate bool EnumWindowsDelegate(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsDelegate callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr newValue);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int index, IntPtr newValue);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(
            IntPtr previousWindowProcedure,
            IntPtr hWnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam
        );

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateEvent(
            IntPtr eventAttributes,
            bool manualReset,
            bool initialState,
            string name
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetEvent(IntPtr eventHandle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileMapping(
            IntPtr fileHandle,
            IntPtr attributes,
            uint protection,
            uint maximumSizeHigh,
            uint maximumSizeLow,
            string name
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(
            IntPtr mappingHandle,
            uint desiredAccess,
            uint fileOffsetHigh,
            uint fileOffsetLow,
            UIntPtr bytesToMap
        );

        [DllImport("kernel32.dll")]
        private static extern void SetLastError(uint errorCode);

        /// <summary>
        /// Entry point called by the external Mono injector.
        /// </summary>
        public static void Start()
        {
            try
            {
                InitializeSharedMemory();

                bool sharedMonitorReady = ReadSharedInt64(MonitorReadyOffset) == 1;
                if ((monitorInstalled || sharedMonitorReady) && IsMonitorHeartbeatMoving())
                {
                    monitorInstalled = true;
                    SignalReady();
                    WriteStatus("READY", "MONITOR");
                    return;
                }

                // A previous scene transition or bridge instance can leave the
                // shared ready bit behind after its monitor has stopped. Clear
                // it before installing the one-shot main-thread hook so the GUI
                // cannot mistake stale state for a successfully rebuilt monitor.
                monitorInstalled = false;
                WriteSharedInt64(MonitorReadyOffset, 0);
                WriteSharedInt64(LivesSnapshotOffset, 0);

                if (windowHookInstalled && gameWindow != IntPtr.Zero && IsWindow(gameWindow))
                {
                    SignalReady();
                    WriteStatus("READY", gameWindow.ToInt64().ToString());
                    return;
                }

                windowHookInstalled = false;
                gameWindow = WaitForGameWindow();
                originalWindowProcedure = IntPtr.Zero;
                if (gameWindow == IntPtr.Zero)
                {
                    WriteStatus("ERROR", "找不到 Broforce 主窗口");
                    return;
                }

                windowProcedure = new WndProcDelegate(WindowProcedure);
                IntPtr functionPointer = Marshal.GetFunctionPointerForDelegate(windowProcedure);
                SetLastError(0);
                originalWindowProcedure = SetWindowLongPtr(gameWindow, GwlWndProc, functionPointer);
                int error = Marshal.GetLastWin32Error();
                if (originalWindowProcedure == IntPtr.Zero && error != 0)
                {
                    WriteStatus("ERROR", "安装一次性主线程初始化钩子失败，Win32=" + error);
                    return;
                }

                windowHookInstalled = true;
                SignalReady();
                WriteStatus("READY", gameWindow.ToInt64().ToString());
            }
            catch (Exception exception)
            {
                WriteStatus("ERROR", exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static void InitializeSharedMemory()
        {
            if (sharedMemoryView != IntPtr.Zero)
            {
                return;
            }

            SetLastError(0);
            sharedMemoryHandle = CreateFileMapping(
                new IntPtr(-1),
                IntPtr.Zero,
                PageReadWrite,
                0,
                SharedMemorySize,
                "Local\\BroforceLifeTrainer_SharedV13_" + CurrentPid
            );
            if (sharedMemoryHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("创建共享状态失败，Win32=" + Marshal.GetLastWin32Error());
            }
            int createError = Marshal.GetLastWin32Error();

            sharedMemoryView = MapViewOfFile(
                sharedMemoryHandle,
                FileMapWrite,
                0,
                0,
                new UIntPtr((uint)SharedMemorySize)
            );
            if (sharedMemoryView == IntPtr.Zero)
            {
                throw new InvalidOperationException("映射共享状态失败，Win32=" + Marshal.GetLastWin32Error());
            }

            for (currentPlayer = 0; currentPlayer < LocalPlayerCount; currentPlayer++)
            {
                if (createError != ErrorAlreadyExists)
                {
                    WriteSharedInt64(LivesSnapshotOffset, 0);
                    WriteSharedInt64(AddLifeSequenceOffset, 0);
                    WriteSharedInt64(IronbroAddLifeSequenceOffset, 0);
                    WriteSharedInt64(SpawnVillagerSequenceOffset, 0);
                    WriteSharedInt64(SpawnCompanionSequenceOffset, 0);
                    WriteSharedInt64(HeroSwitchCommandOffset, 0);
                    WriteSharedInt64(HeroLockTargetOffset, -1);
                }
            }
            currentPlayer = 0;
            ResetPlayerSequences();
        }

        private static void ResetPlayerSequences()
        {
            for (currentPlayer = 0; currentPlayer < LocalPlayerCount; currentPlayer++)
            {
                lastAddLifeSequence = ReadSharedInt64(AddLifeSequenceOffset);
                lastIronbroAddLifeSequence = ReadSharedInt64(IronbroAddLifeSequenceOffset);
                lastSpawnVillagerSequence = ReadSharedInt64(SpawnVillagerSequenceOffset);
                playerStates[currentPlayer].lastSpawnCompanionSequence = ReadSharedInt64(SpawnCompanionSequenceOffset);
                lastHeroSwitchCommand = ReadSharedInt64(HeroSwitchCommandOffset);
                lastHeroLockTarget = Int64.MinValue;
            }
            currentPlayer = 0;
        }

        private static bool IsMonitorHeartbeatMoving()
        {
            long before = ReadSharedInt64(MonitorHeartbeatOffset);
            Thread.Sleep(MonitorHealthProbeMilliseconds);
            return ReadSharedInt64(MonitorHeartbeatOffset) != before;
        }

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newValue)
        {
            if (IntPtr.Size == 8)
            {
                return SetWindowLongPtr64(hWnd, index, newValue);
            }
            return SetWindowLong32(hWnd, index, newValue);
        }

        private static IntPtr WaitForGameWindow()
        {
            int attempts;
            for (attempts = 0; attempts < 150; attempts++)
            {
                IntPtr found = FindGameWindow();
                if (found != IntPtr.Zero)
                {
                    return found;
                }
                Thread.Sleep(100);
            }
            return IntPtr.Zero;
        }

        private static IntPtr FindGameWindow()
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows(
                delegate(IntPtr hWnd, IntPtr unused)
                {
                    uint processId;
                    GetWindowThreadProcessId(hWnd, out processId);
                    if (processId == (uint)CurrentPid && IsWindowVisible(hWnd) && GetWindow(hWnd, GwOwner) == IntPtr.Zero)
                    {
                        result = hWnd;
                        return false;
                    }
                    return true;
                },
                IntPtr.Zero
            );
            return result;
        }

        private static IntPtr WindowProcedure(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == InstallMonitorMessage)
            {
                try
                {
                    EnsureMonitorOnMainThread();
                    if (!RestoreOriginalWindowProcedure(hWnd))
                    {
                        return IntPtr.Zero;
                    }
                    return new IntPtr(1);
                }
                catch (Exception exception)
                {
                    WriteStatus("ERROR", exception.GetType().Name + ": " + exception.Message);
                    return IntPtr.Zero;
                }
            }

            return CallWindowProc(originalWindowProcedure, hWnd, message, wParam, lParam);
        }

        private static void EnsureMonitorOnMainThread()
        {
            if (monitorInstalled)
            {
                return;
            }

            UnityEngine.Application.runInBackground = true;
            UnityEngine.GameObject host = new UnityEngine.GameObject("BroforceLifeTrainerMonitor");
            UnityEngine.Object.DontDestroyOnLoad(host);
            LivesMonitorBehaviour behaviour = (LivesMonitorBehaviour)host.AddComponent(typeof(LivesMonitorBehaviour));
            currentMonitorGeneration++;
            behaviour.Generation = currentMonitorGeneration;
            ResetPlayerSequences();
            monitorInstalled = true;
            WriteSharedInt64(MonitorReadyOffset, 1);
            WriteStatus("MONITOR", "READY");
        }

        private static bool RestoreOriginalWindowProcedure(IntPtr hWnd)
        {
            if (!windowHookInstalled)
            {
                return true;
            }

            SetLastError(0);
            IntPtr replaced = SetWindowLongPtr(hWnd, GwlWndProc, originalWindowProcedure);
            int error = Marshal.GetLastWin32Error();
            if (replaced == IntPtr.Zero && error != 0)
            {
                WriteStatus("ERROR", "移除一次性窗口钩子失败，Win32=" + error);
                return false;
            }

            windowHookInstalled = false;
            gameWindow = IntPtr.Zero;
            return true;
        }

        internal static void ProcessPendingAddLife()
        {
            long sequence = ReadSharedInt64(AddLifeSequenceOffset);
            if (sequence == lastAddLifeSequence)
            {
                return;
            }
            lastAddLifeSequence = sequence;

            try
            {
                AddOneLifeToCurrentPlayer();
            }
            catch (Exception exception)
            {
                WriteStatus("ERROR", exception.GetType().Name + ": " + exception.Message);
            }
            RefreshLivesSnapshot();
        }

        internal static void ProcessPendingIronbroAddLife()
        {
            long sequence = ReadSharedInt64(IronbroAddLifeSequenceOffset);
            if (sequence == lastIronbroAddLifeSequence)
            {
                return;
            }
            lastIronbroAddLifeSequence = sequence;

            try
            {
                AddOneNormalIronbroLifeToCurrentPlayer();
            }
            catch (Exception exception)
            {
                WriteStatus("ERROR", exception.GetType().Name + ": " + exception.Message);
            }
            RefreshLivesSnapshot();
        }

        private static Villager pendingVillager { get { return playerStates[currentPlayer].pendingVillager; } set { playerStates[currentPlayer].pendingVillager = value; } }
        private static int pendingVillagerPlayer { get { return playerStates[currentPlayer].pendingVillagerPlayer; } set { playerStates[currentPlayer].pendingVillagerPlayer = value; } }
        private static float pendingVillagerDeadline { get { return playerStates[currentPlayer].pendingVillagerDeadline; } set { playerStates[currentPlayer].pendingVillagerDeadline = value; } }

        private static bool ValidVillagerPrefab(Villager value)
        {
            if (value == null || value.GetComponent<Citizen>() != null ||
                value.GetComponent<VillagerAI>() == null || value.GetComponent<FollowerMinion>() == null)
                return false;
            FieldInfo gun = typeof(Villager).GetField("hasGun", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return gun != null && !(bool)gun.GetValue(value);
        }

        private static Villager FindVillagerPrefab()
        {
            if (Map.Instance.activeTheme != null && Map.Instance.activeTheme.villager1 != null)
                foreach (TestVanDammeAnim item in Map.Instance.activeTheme.villager1)
                {
                    Villager villager = item == null ? null : item.GetComponent<Villager>();
                    if (ValidVillagerPrefab(villager)) return villager;
                }
            // Use the game's manifest keys, not guessed Resources.Load paths.
            foreach (Networking.SpawnablePrefab entry in InstantiationController.PrefabManifest.Spawnables)
            {
                string key = entry.ResourceName;
                if (String.IsNullOrEmpty(key)) continue;
                string name = Path.GetFileNameWithoutExtension(key.Replace('\\', '/')).ToLowerInvariant();
                if (name != "villager1" && name != "villager2") continue;
                UnityEngine.GameObject prefab = InstantiationController.GetPrefabFromResourceName(key);
                Villager villager = prefab == null ? null : prefab.GetComponent<Villager>();
                if (ValidVillagerPrefab(villager)) return villager;
            }
            return null;
        }

        internal static void ProcessPendingVillager()
        {
            try
            {
                if (villagerMap != Map.Instance)
                {
                    villagerMap = Map.Instance;
                    spawnedVillagers.Clear();
                    foreach (PlayerState state in playerStates) state.pendingVillager = null;
                }
                if (pendingVillager != null)
                {
                    FieldInfo gun = typeof(Villager).GetField("hasGun", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (gun != null && (bool)gun.GetValue(pendingVillager) && pendingVillager.playerNum == pendingVillagerPlayer)
                    {
                        try
                        {
                            EnhancedVillagerAI.Attach(pendingVillager, pendingVillagerPlayer);
                            WriteStatus("VILLAGER_OK", "已生成武装平民（玩家 " + (pendingVillagerPlayer + 1) + "，" +
                                (ReadSharedInt64(VillagerAiDisabledOffset) == 0 ? "AI 强化开启" : "原生 AI") + "）");
                        }
                        catch (Exception aiError)
                        {
                            WriteStatus("VILLAGER_DENIED", "已生成武装平民，但强化组件未加载，保留原生行为：" + aiError.Message);
                        }
                        pendingVillager = null;
                    }
                    else if (UnityEngine.Time.realtimeSinceStartup > pendingVillagerDeadline)
                    {
                        WriteStatus("VILLAGER_DENIED", "平民已生成，但武装状态未确认，请检查游戏");
                        pendingVillager = null;
                    }
                }
                foreach (Villager villager in spawnedVillagers)
                {
                    if (villager == null || villager.playerNum != currentPlayer) continue;
                    EnhancedVillagerAI ai = villager.GetComponent<EnhancedVillagerAI>();
                    if (ai != null)
                    {
                        ai.SetEnhanced(ReadSharedInt64(VillagerAiDisabledOffset) == 0);
                        if (ai.ConsumeFailure())
                            WriteStatus("VILLAGER_DENIED", "村民 AI 强化出现异常，该村民已恢复原生行为，详情见 villager_ai_v10.log");
                    }
                }
                long sequence = ReadSharedInt64(SpawnVillagerSequenceOffset);
                if (sequence == lastSpawnVillagerSequence) return;
                lastSpawnVillagerSequence = sequence;
                Player player;
                int index;
                if (!Connect.IsOffline)
                { WriteStatus("VILLAGER_DENIED", "生成平民仅支持离线本地关卡"); return; }
                if (Map.Instance == null || !TryFindCurrentLocalPlayer(out player, out index) ||
                    !player.IsAlive() || player.character == null || player.IsPendingRespawnOrResuingInProgress() ||
                    UnityEngine.Time.timeScale <= 0f)
                { WriteStatus("VILLAGER_DENIED", "请在角色存活且关卡正常运行时生成"); return; }
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now < nextVillagerSpawnTime || pendingVillager != null)
                { WriteStatus("VILLAGER_WAIT", "请稍后再生成平民"); return; }
                nextVillagerSpawnTime = now + 0.5f;
                spawnedVillagers.RemoveAll(delegate(Villager v) { return v == null || v.health <= 0; });
                if (spawnedVillagers.Count >= 20)
                { WriteStatus("VILLAGER_DENIED", "本关由修改器生成的存活平民已达 20 名"); return; }
                UnityEngine.Vector3 pos = player.GetCharacterPosition();
                bool found = false;
                float x = pos.x, y = pos.y;
                foreach (float offset in new float[] { 16f, -16f, 24f, -24f })
                {
                    float candidateX = pos.x + offset;
                    float ground = Map.GetGroundHeight(candidateX, pos.y + 8f);
                    if (candidateX <= 0f || ground < 0f || System.Math.Abs(ground - pos.y) > 24f) continue;
                    UnityEngine.Vector3 point = new UnityEngine.Vector3(candidateX, ground + 8f, pos.z);
                    if (UnityEngine.Physics.CheckSphere(point, 5f, Map.groundLayer)) continue;
                    x = candidateX; y = ground + 2f; found = true; break;
                }
                if (!found) { WriteStatus("VILLAGER_DENIED", "身旁没有合适落脚点，请移动到开阔地面"); return; }
                Villager prefab = FindVillagerPrefab();
                if (prefab == null) { WriteStatus("VILLAGER_DENIED", "未能加载完整平民模板"); return; }
                Villager created = MapController.SpawnVillager_Networked(prefab, x, y, 0f, 0f,
                    false, false, false, false, false, index);
                if (created == null) { WriteStatus("VILLAGER_DENIED", "游戏未返回平民实例"); return; }
                spawnedVillagers.Add(created);
                pendingVillager = created;
                pendingVillagerPlayer = index;
                pendingVillagerDeadline = now + 2f;
                WriteStatus("VILLAGER_WAIT", "平民已生成，正在确认武装状态……");
            }
            catch (Exception exception)
            {
                lastSpawnVillagerSequence = ReadSharedInt64(SpawnVillagerSequenceOffset);
                pendingVillager = null;
                WriteStatus("VILLAGER_DENIED", "生成失败：" + exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal static void ProcessPendingCompanion()
        {
            try
            {
                string error = CompanionSpawner.Maintain(currentPlayer);
                if (error != null) WriteStatus("COMPANION_DENIED", error);
                long sequence = ReadSharedInt64(SpawnCompanionSequenceOffset);
                if (sequence == playerStates[currentPlayer].lastSpawnCompanionSequence) return;
                playerStates[currentPlayer].lastSpawnCompanionSequence = sequence;
                Player player; int index;
                if (!TryFindCurrentLocalPlayer(out player, out index))
                { WriteStatus("COMPANION_DENIED", "所选玩家尚未进入本地关卡"); return; }
                int heroValue = (int)(sequence & 255);
                ulong unlocked;
                if (heroValue >= PlayableHeroTypeCount || !TryGetUnlockedHeroMask(out unlocked) ||
                    (unlocked & (1UL << heroValue)) == 0)
                { WriteStatus("COMPANION_DENIED", "所选队友人物无效或当前存档尚未解锁"); return; }
                WriteStatus("COMPANION_OK", CompanionSpawner.Spawn(player, index, (HeroType)heroValue));
            }
            catch (Exception error)
            {
                playerStates[currentPlayer].lastSpawnCompanionSequence = ReadSharedInt64(SpawnCompanionSequenceOffset);
                WriteStatus("COMPANION_DENIED", "电脑队友生成失败：" + error.Message);
            }
        }

        internal static void ProcessPendingHeroSwitch()
        {
            long command = ReadSharedInt64(HeroSwitchCommandOffset);
            if (command == lastHeroSwitchCommand) return;
            // Consume once, including rejected requests; never carry a switch into another life.
            lastHeroSwitchCommand = command;
            try
            {
                int heroValue = (int)(command & 255);
                ulong unlockedMask;
                if (!Connect.IsOffline || GameModeController.IsHardcoreMode)
                { WriteStatus("HERO_SWITCH_DENIED", "人物切换仅支持普通离线本地关卡"); return; }
                if (heroValue >= PlayableHeroTypeCount || !TryGetUnlockedHeroMask(out unlockedMask) ||
                    (unlockedMask & (1UL << heroValue)) == 0)
                { WriteStatus("HERO_SWITCH_DENIED", "所选人物无效或尚未解锁"); return; }
                Player player;
                int index;
                if (Map.Instance == null || !TryFindCurrentLocalPlayer(out player, out index) ||
                    !CanChangeHeroNow(player))
                { WriteStatus("HERO_SWITCH_DENIED", "请等待角色完成出场，并在角色存活且关卡正常运行时重新点击切换"); return; }
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now < nextHeroSwitchTime)
                { WriteStatus("HERO_SWITCH_DENIED", "切换过快，请稍后重新点击"); return; }
                HeroType target = (HeroType)heroValue;
                if (HeroController.GetHeroPrefab(target) == null)
                { WriteStatus("HERO_SWITCH_DENIED", "当前游戏构建中找不到所选人物"); return; }
                nextHeroSwitchTime = now + HeroLockRetrySeconds;
                if (HeroController.GetCurrentHeroType(index) != target)
                    ChangeHeroAtCurrentPosition(player, index, target);
                if (HeroController.GetCurrentHeroType(index) != target)
                { WriteStatus("HERO_SWITCH_DENIED", "人物切换未生效，请稍后重新点击"); return; }
                // A successful one-shot switch cancels persistence, including an older lock.
                WriteSharedInt64(HeroLockTargetOffset, -1);
                lastHeroLockTarget = -1;
                activeHeroLock = HeroType.None;
                reportedHeroLockPlayer = -1;
                reportedHeroLockType = HeroType.None;
                WriteStatus("HERO_SWITCH_OK", index.ToString(), heroValue.ToString());
            }
            catch (Exception exception)
            { WriteStatus("HERO_SWITCH_DENIED", "人物切换异常：" + exception.Message); }
        }

        private static bool CanChangeHeroNow(Player player)
        {
            // IsAlive alone also accepts the hidden hero still held by the transport.
            // ChangeBro destroys that object, leaving the transport with a dead reference.
            return Map.Instance != null && HeroController.Instance != null &&
                HeroController.BrosReleased && UnityEngine.Time.timeScale > 0f &&
                player.IsAliveAndSpawnedHero() && !player.IsPendingRespawnOrResuingInProgress() &&
                player.character.enabled && player.character.gameObject.activeInHierarchy &&
                !player.character.isOnHelicopter;
        }

        private static void ChangeHeroAtCurrentPosition(Player player, int playerIndex, HeroType target)
        {
            // ChangeBro uses playerFollowPos, which can still lag behind the transport's
            // release position. Preserve the actual character coordinates as well as
            // the velocities already preserved by the game's ChangeBro implementation.
            float x = player.character.X;
            float y = player.character.Y;
            HeroController.ChangeBro(playerIndex, target);
            if (player.character != null && HeroController.GetCurrentHeroType(playerIndex) == target)
            {
                player.character.SetXY(x, y);
                player.character.SetPosition();
            }
        }

        internal static void ProcessHeroLock()
        {
            try
            {
                ProcessHeroLockCore();
            }
            catch (Exception exception)
            {
                RejectHeroLock(
                    "人物锁定发生异常并已关闭：" +
                    exception.GetType().Name + ": " + exception.Message
                );
            }
        }

        private static void ProcessHeroLockCore()
        {
            long requestedTarget = ReadSharedInt64(HeroLockTargetOffset);
            bool firstObservation = lastHeroLockTarget == Int64.MinValue;
            bool requestChanged = requestedTarget != lastHeroLockTarget;
            if (requestChanged)
            {
                lastHeroLockTarget = requestedTarget;
                reportedHeroLockPlayer = -1;
                reportedHeroLockType = HeroType.None;
                nextHeroLockAttemptTime = 0f;
            }

            if (requestedTarget == -1)
            {
                if (requestChanged && !firstObservation)
                {
                    HeroType previous = activeHeroLock;
                    activeHeroLock = HeroType.None;
                    WriteStatus(
                        "HERO_LOCK_OFF",
                        ((int)previous).ToString(),
                        previous == HeroType.None ? String.Empty : previous.ToString()
                    );
                }
                activeHeroLock = HeroType.None;
                return;
            }

            if (requestedTarget < 0 || requestedTarget >= PlayableHeroTypeCount)
            {
                RejectHeroLock("人物编号无效，人物锁定已关闭");
                return;
            }
            if (!Connect.IsOffline)
            {
                RejectHeroLock("仅支持单机或本地游戏，已关闭人物锁定");
                return;
            }
            if (GameModeController.IsHardcoreMode)
            {
                RejectHeroLock("钢铁兄弟模式有人物死亡名单，已关闭人物锁定以保护存档");
                return;
            }

            ulong unlockedMask;
            if (!TryGetUnlockedHeroMask(out unlockedMask))
            {
                if (requestChanged)
                {
                    WriteStatus(
                        "HERO_LOCK_ARMED",
                        requestedTarget.ToString(),
                        ((HeroType)(int)requestedTarget).ToString()
                    );
                }
                return;
            }
            WriteSharedInt64(UnlockedHeroMaskOffset, unchecked((long)unlockedMask));
            if ((unlockedMask & (1UL << (int)requestedTarget)) == 0)
            {
                RejectHeroLock("所选人物尚未在当前存档中解锁，未进行切换");
                return;
            }

            HeroType target = (HeroType)(int)requestedTarget;
            activeHeroLock = target;

            Player player;
            int playerIndex;
            if (!TryFindCurrentLocalPlayer(out player, out playerIndex))
            {
                if (requestChanged)
                {
                    WriteStatus("HERO_LOCK_ARMED", ((int)target).ToString(), target.ToString());
                }
                return;
            }
            if (!CanChangeHeroNow(player))
            {
                return;
            }

            HeroType current = HeroController.GetCurrentHeroType(playerIndex);
            if (current == target)
            {
                ReportHeroLockApplied(playerIndex, target);
                return;
            }

            reportedHeroLockPlayer = -1;
            reportedHeroLockType = HeroType.None;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < nextHeroLockAttemptTime)
            {
                return;
            }
            nextHeroLockAttemptTime = now + HeroLockRetrySeconds;

            if (HeroController.GetHeroPrefab(target) == null)
            {
                RejectHeroLock("当前游戏构建中找不到所选人物，人物锁定已关闭");
                return;
            }

            ChangeHeroAtCurrentPosition(player, playerIndex, target);
            if (HeroController.GetCurrentHeroType(playerIndex) == target)
            {
                ReportHeroLockApplied(playerIndex, target);
            }
        }

        internal static void ProcessAllPlayers()
        {
            for (currentPlayer = 0; currentPlayer < LocalPlayerCount; currentPlayer++)
            {
                ProcessPendingAddLife();
                ProcessPendingIronbroAddLife();
                ProcessPendingVillager();
                ProcessPendingCompanion();
                ProcessPendingHeroSwitch();
                ProcessHeroLock();
            }
            currentPlayer = 0;
        }

        internal static void RefreshAllPlayers()
        {
            for (currentPlayer = 0; currentPlayer < LocalPlayerCount; currentPlayer++)
                RefreshLivesSnapshot();
            currentPlayer = 0;
        }

        internal static void MonitorDestroyed(int generation)
        {
            if (generation == currentMonitorGeneration)
            {
                monitorInstalled = false;
                WriteSharedInt64(MonitorReadyOffset, 0);
            }
        }

        internal static void RefreshLivesSnapshot()
        {
            try
            {
                WriteSharedInt64(LivesSnapshotOffset, ReadCurrentLivesSnapshot());
            }
            catch
            {
                // Player objects are rebuilt during death and level transitions.
                WriteSharedInt64(LivesSnapshotOffset, 0);
            }
            ulong unlockedMask;
            if (TryGetUnlockedHeroMask(out unlockedMask))
            {
                WriteSharedInt64(UnlockedHeroMaskOffset, unchecked((long)unlockedMask));
            }
            WriteSharedInt64(
                MonitorHeartbeatOffset,
                unchecked(ReadSharedInt64(MonitorHeartbeatOffset) + 1)
            );
        }

        private static bool TryGetUnlockedHeroMask(out ulong mask)
        {
            mask = 0;
            try
            {
                System.Collections.Generic.List<HeroType> unlocked =
                    HeroUnlockController.GetUnlockedHeroes(true);
                if (unlocked == null)
                {
                    return false;
                }

                int index;
                for (index = 0; index < unlocked.Count; index++)
                {
                    int heroValue = (int)unlocked[index];
                    if (heroValue >= 0 && heroValue < PlayableHeroTypeCount)
                    {
                        mask |= 1UL << heroValue;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ReportHeroLockApplied(int playerIndex, HeroType target)
        {
            if (reportedHeroLockPlayer == playerIndex && reportedHeroLockType == target)
            {
                return;
            }
            reportedHeroLockPlayer = playerIndex;
            reportedHeroLockType = target;
            WriteStatus(
                "HERO_LOCKED",
                playerIndex.ToString(),
                ((int)target).ToString(),
                target.ToString()
            );
        }

        private static void RejectHeroLock(string message)
        {
            WriteSharedInt64(HeroLockTargetOffset, -1);
            lastHeroLockTarget = -1;
            activeHeroLock = HeroType.None;
            reportedHeroLockPlayer = -1;
            reportedHeroLockType = HeroType.None;
            WriteStatus("HERO_LOCK_DENIED", message);
        }

        /// <summary>
        /// High 32 bits = player index + 1, low 32 bits = lives.
        /// Zero means no active local player; -1 means an online room.
        /// </summary>
        private static long ReadCurrentLivesSnapshot()
        {
            if (!Connect.IsOffline)
            {
                return -1;
            }

            Player player;
            int index;
            if (!TryFindCurrentLocalPlayer(out player, out index))
            {
                return 0;
            }

            int lives;
            if (!TryReadDisplayedLives(player, out lives))
            {
                lives = player.Lives;
            }
            if (lives < 0)
            {
                lives = 0;
            }
            return ((long)(index + 1) << 32) | (uint)lives;
        }

        private static void AddOneLifeToCurrentPlayer()
        {
            if (!Connect.IsOffline)
            {
                WriteStatus("DENIED", "仅支持单机或本地游戏，已拒绝在线房间操作");
                return;
            }

            Player player;
            int index;
            if (!TryFindCurrentLocalPlayer(out player, out index))
            {
                WriteStatus("WAITING", "未找到已进入关卡的本地玩家");
                return;
            }

            if (GameModeController.IsHardcoreMode)
            {
                WriteStatus("DENIED", "钢铁兄弟模式不能只修改数字命数，请使用“钢铁兄弟 +1 正常命数”");
                return;
            }

            player.AddLife();
            int displayedLives;
            if (!TryReadDisplayedLives(player, out displayedLives))
            {
                displayedLives = player.Lives;
            }
            WriteStatus("OK", index.ToString(), displayedLives.ToString());
        }

        private static void AddOneNormalIronbroLifeToCurrentPlayer()
        {
            if (!Connect.IsOffline)
            {
                WriteStatus("DENIED", "仅支持单机或本地游戏，已拒绝在线房间操作");
                return;
            }

            if (!GameModeController.IsHardcoreMode)
            {
                WriteStatus("DENIED", "当前不是钢铁兄弟模式，请使用普通 +1 命按钮");
                return;
            }

            Player player;
            int index;
            if (!TryFindCurrentLocalPlayer(out player, out index))
            {
                WriteStatus("WAITING", "未找到钢铁兄弟关卡中的本地玩家");
                return;
            }

            GameState gameState = GameState.Instance;
            if (
                gameState == null ||
                gameState.currentWorldmapSave == null ||
                gameState.currentWorldmapSave.hardcoreModeAvailableBros == null ||
                gameState.currentWorldmapSave.hardcoreModeDeadBros == null
            )
            {
                WriteStatus("WAITING", "钢铁兄弟存档尚未准备完成，请进入关卡后重试");
                return;
            }

            WorldMapProgressSaveSlot save = gameState.currentWorldmapSave;
            int availableBefore = save.hardcoreModeAvailableBros.Count;
            int livesBefore = player.Lives;
            HeroType addedBro;
            string addedBroSource;

            // Death appends the current hero to hardcoreModeDeadBros. Moving
            // the most recently killed hero back to the available roster is
            // the exact inverse needed for a usable revived Ironbro life.
            if (save.hardcoreModeDeadBros.Count > 0)
            {
                int deadIndex = save.hardcoreModeDeadBros.Count - 1;
                addedBro = save.hardcoreModeDeadBros[deadIndex];
                if (save.hardcoreModeAvailableBros.Contains(addedBro))
                {
                    WriteStatus("ERROR", "该死亡角色已存在于可用名单，存档名单不一致，未修改命数");
                    return;
                }

                save.hardcoreModeDeadBros.RemoveAt(deadIndex);
                try
                {
                    save.hardcoreModeAvailableBros.Add(addedBro);
                }
                catch
                {
                    save.hardcoreModeDeadBros.Add(addedBro);
                    throw;
                }
                addedBroSource = "REVIVED";
            }
            else
            {
                if (!HeroUnlockController.AreAnyMoreBrosAvailableToBeSavedInHardcoreMode())
                {
                    WriteStatus("DENIED", "没有死亡角色或尚未加入本局的已解锁角色，无法增加正常命数");
                    return;
                }

                HeroUnlockController.FreeBro();
                addedBro = save.hardcoreModeAvailableBros[save.hardcoreModeAvailableBros.Count - 1];
                addedBroSource = "NEW";
            }

            int availableAfter = save.hardcoreModeAvailableBros.Count;
            if (availableAfter != availableBefore + 1)
            {
                WriteStatus("ERROR", "游戏没有加入新的钢铁兄弟角色，命数未修改");
                return;
            }

            bool repairedPhantomLife = livesBefore > availableBefore;
            if (!repairedPhantomLife)
            {
                player.AddLife();
            }

            int displayedLives;
            if (!TryReadDisplayedLives(player, out displayedLives))
            {
                displayedLives = player.Lives;
            }
            WriteStatus(
                repairedPhantomLife ? "IRONBRO_REPAIRED" : "IRONBRO_OK",
                index.ToString(),
                displayedLives.ToString(),
                availableAfter.ToString(),
                addedBroSource,
                addedBro.ToString()
            );
        }

        private static bool TryFindCurrentLocalPlayer(out Player selectedPlayer, out int selectedIndex)
        {
            selectedPlayer = null;
            selectedIndex = currentPlayer;
            Player[] players = HeroController.players;
            if (players == null || currentPlayer < 0 || currentPlayer >= players.Length) return false;
            Player player = players[currentPlayer];
            // Never fall back to another slot when the requested player has not joined.
            if (player == null || !HeroController.IsPlaying(currentPlayer) || !player.IsMine) return false;
            selectedPlayer = player;
            return true;
        }

        private static bool TryReadDisplayedLives(Player player, out int lives)
        {
            lives = 0;
            if (player == null || player.hud == null || player.hud.livesText == null)
            {
                return false;
            }

            string text = player.hud.livesText.text;
            if (String.IsNullOrEmpty(text))
            {
                return false;
            }

            int end = text.Length - 1;
            while (end >= 0 && !Char.IsDigit(text[end]))
            {
                end--;
            }
            if (end < 0)
            {
                return false;
            }

            int start = end;
            while (start > 0 && Char.IsDigit(text[start - 1]))
            {
                start--;
            }
            return Int32.TryParse(text.Substring(start, end - start + 1), out lives);
        }

        private static int PlayerOffset(int offset)
        {
            if (offset == LivesSnapshotOffset || offset == AddLifeSequenceOffset ||
                offset == IronbroAddLifeSequenceOffset || offset == HeroLockTargetOffset ||
                offset == SpawnVillagerSequenceOffset || offset == HeroSwitchCommandOffset ||
                offset == VillagerAiDisabledOffset || offset == SpawnCompanionSequenceOffset)
                return offset + currentPlayer * PlayerStride;
            return offset;
        }

        private static long ReadSharedInt64(int offset)
        {
            return Marshal.ReadInt64(new IntPtr(sharedMemoryView.ToInt64() + PlayerOffset(offset)));
        }

        private static void WriteSharedInt64(int offset, long value)
        {
            Marshal.WriteInt64(new IntPtr(sharedMemoryView.ToInt64() + PlayerOffset(offset)), value);
        }

        private static void SignalReady()
        {
            if (readyEvent == IntPtr.Zero)
            {
                readyEvent = CreateEvent(
                    IntPtr.Zero,
                    true,
                    false,
                    "Local\\BroforceLifeTrainer_ReadyV13_" + CurrentPid
                );
            }
            if (readyEvent != IntPtr.Zero)
            {
                SetEvent(readyEvent);
            }
        }

        private static void WriteStatus(string state, params string[] details)
        {
            try
            {
                Directory.CreateDirectory(AppDataDirectory);
                StringBuilder builder = new StringBuilder();
                builder.Append(state);
                builder.Append('|');
                builder.Append(CurrentPid);
                int index;
                for (index = 0; index < details.Length; index++)
                {
                    builder.Append('|');
                    builder.Append((details[index] ?? String.Empty).Replace('|', '/').Replace('\r', ' ').Replace('\n', ' '));
                }
                builder.Append("|PLAYER=").Append(currentPlayer);
                File.WriteAllText(StatusPath, builder.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Status reporting must never destabilize the game.
            }
        }
    }

    public sealed class LivesMonitorBehaviour : UnityEngine.MonoBehaviour
    {
        public int Generation;
        private float nextSampleTime;

        private void Awake()
        {
            Bootstrap.RefreshAllPlayers();
            nextSampleTime = UnityEngine.Time.realtimeSinceStartup + 0.25f;
        }

        private void Update()
        {
            Bootstrap.ProcessAllPlayers();

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now >= nextSampleTime)
            {
                nextSampleTime = now + 0.25f;
                Bootstrap.RefreshAllPlayers();
            }
        }

        private void OnDestroy()
        {
            Bootstrap.MonitorDestroyed(Generation);
        }
    }
}
