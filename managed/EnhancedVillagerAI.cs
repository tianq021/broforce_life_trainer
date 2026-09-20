using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

namespace BroforceLifeTrainer
{
    // Only attached to trainer-created instances after their native arming succeeds.
    // Swap the public enemyAI reference, retaining the original component for rollback.
    public sealed class EnhancedVillagerAI : VillagerAI
    {
        private Villager actor;
        private VillagerAI original;
        private bool originalEnabled;
        private FollowerMinion follower;
        private PathAgent navigation;
        private int ownerIndex;
        private Map scene;
        private bool active, failed, catchingUp;
        private bool failureReported;
        private Unit target, previousLeader;
        private float nextScan, nextPath, sampleTime, sampleX, sampleY;
#if !VILLAGER_AI_TEST
        private float nextLog;
#endif
        private bool wasMoving, pathStuck;
        private int goalColumn, goalRow;
        private static float nextGlobalPath;
        private static int lastPathFrame = -1;
        private static readonly List<EnhancedVillagerAI> pathQueue = new List<EnhancedVillagerAI>();
        private bool pathQueued;
        private float lastPathRequest;
        private float climbUntil;
        private string attackState = "waiting";
        private bool lastFire;
        private float originalFireRate;
        private int scanned, hostileCount, inRangeCount, shootableCount;
        private int rejectedTeam, rejectedState;
        private Unit abandonedTarget;
        private float abandonUntil;
        private int attackStalls;
        private static readonly FieldInfo LeaderPlayerField = typeof(FollowerMinion).GetField(
            "leaderPlayerNo", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FireCounterField = typeof(Villager).GetField(
            "fireCounter", BindingFlags.Instance | BindingFlags.NonPublic);

        // Native Awake initializes inherited unit/path references. The original AI's
        // unrelated patrol/thought queue must not compete for the shared PathAgent.
        protected override void Update() { }
        protected override void LateUpdate() { }

        internal static EnhancedVillagerAI Attach(Villager villager, int player)
        {
            EnhancedVillagerAI existing = villager.GetComponent<EnhancedVillagerAI>();
            if (existing != null) return existing;
            VillagerAI native = villager.GetComponent<VillagerAI>();
            PathAgent path = villager.GetComponent<PathAgent>();
            FollowerMinion minion = villager.GetComponent<FollowerMinion>();
            if (native == null || path == null || minion == null || LeaderPlayerField == null || villager.enemyAI != native)
                throw new InvalidOperationException("村民 AI 结构不兼容，保留原生行为");
            EnhancedVillagerAI ai = villager.gameObject.AddComponent<EnhancedVillagerAI>();
            ai.actor = villager;
            ai.original = native;
            ai.originalEnabled = native.enabled;
            ai.originalFireRate = villager.fireRate;
            ai.navigation = path;
            ai.follower = minion;
            ai.ownerIndex = player;
            ai.scene = Map.Instance;
            ai.enabled = false;
            return ai;
        }

        internal bool ConsumeFailure()
        {
            if (!failed || failureReported) return false;
            failureReported = true;
            return true;
        }

        internal void SetEnhanced(bool requested)
        {
            bool value = requested && !failed && Connect.IsOffline && actor != null &&
                actor.health > 0 && scene != null && Map.Instance == scene;
            if (value == active) return;
            if (value && actor.enemyAI != original) return; // Respect another mod's ownership.
            active = value;
            CancelPathRequest();
            target = null;
            navigation.CurrentPath = null;
            catchingUp = false;
            previousLeader = null;
            pathStuck = false;
            wasMoving = false;
            climbUntil = 0f;
            nextPath = Time.time + (System.Math.Abs(actor.GetInstanceID() % 10) * 0.035f);
            nextScan = nextPath;
            sampleTime = Time.time;
            sampleX = actor.X; sampleY = actor.Y;
            if (value)
            {
                original.enabled = false;
                actor.fireRate = System.Math.Min(originalFireRate, 0.12f);
                actor.enemyAI = this;
                enabled = true;
            }
            else
            {
                if (actor.enemyAI == this) actor.enemyAI = original;
                original.enabled = originalEnabled;
                actor.fireRate = originalFireRate;
                enabled = false;
            }
            Log(value ? "enabled" : "native restored", true);
        }

        protected override void OnDestroy()
        {
            // This local adapter is never registered as a network object. The retained
            // native AI owns its network lifecycle; don't send a destroy RPC from here.
            CancelPathRequest();
            if (actor != null && original != null && actor.enemyAI == this)
            {
                actor.enemyAI = original;
                original.enabled = originalEnabled;
                actor.fireRate = originalFireRate;
            }
        }

        public override void GetInput(ref bool left, ref bool right, ref bool up,
            ref bool down, ref bool jump, ref bool fire, ref bool special1,
            ref bool special2, ref bool special3, ref bool special4, ref bool climbButton)
        {
            left = right = up = down = jump = fire = false;
            special1 = special2 = special3 = special4 = climbButton = false;
            if (!active || actor == null) return;
            try
            {
                if (!Connect.IsOffline || Map.Instance != scene)
                { SetEnhanced(false); return; }
                if (Time.timeScale <= 0f || actor.health <= 0 || actor.IsBlind())
                { CancelPathRequest(); return; }
                Player player = HeroController.players == null || ownerIndex >= HeroController.players.Length
                    ? null : HeroController.players[ownerIndex];
                if (player == null || !player.IsAlive() || player.character == null ||
                    player.IsPendingRespawnOrResuingInProgress())
                {
                    CancelPathRequest();
                    target = null; wasMoving = false; sampleTime = Time.time;
                    return;
                }
                Unit leader = player.character;
                if (previousLeader != leader)
                {
                    foreach (Player other in HeroController.players)
                        if (other != null && other != player) other.DeRegisterMinion(actor);
                    player.RegisterMinion(actor);
                    previousLeader = leader;
                    follower.leader = leader;
                    LeaderPlayerField.SetValue(follower, ownerIndex);
                    navigation.CurrentPath = null;
                    nextPath = Time.time;
                    Log("leader refreshed", true);
                }
                float now = Time.time;
                float dx = leader.X - actor.X, dy = leader.Y - actor.Y;
                catchingUp = VillagerTactics.ShouldCatchUp(dx, dy, catchingUp);
                if (now >= nextScan)
                {
                    target = SelectTarget();
                    nextScan = now + 0.12f;
                }
                // Check line of fire again every frame: moving terrain/targets invalidate caches.
                bool visibleTarget = IsShootable(target);
                bool combat = !catchingUp && visibleTarget && !navigation.IsClimbing && now >= climbUntil;
                bool advancing = !catchingUp && IsEnemy(target) && !visibleTarget;
                Unit destination = advancing ? target : leader;
                bool needsFollow = advancing || System.Math.Abs(dx) > 28f || System.Math.Abs(dy) > 12f;
                if (!combat && needsFollow)
                {
                    pathStuck |= VillagerTactics.ShouldRepath(wasMoving,
                        System.Math.Abs(actor.X - sampleX) + System.Math.Abs(actor.Y - sampleY), now - sampleTime);
                    if (now - sampleTime >= 1.8f || !wasMoving)
                    { sampleTime = now; sampleX = actor.X; sampleY = actor.Y; }
                    bool goalMoved = System.Math.Abs(destination.collumn - goalColumn) + System.Math.Abs(destination.row - goalRow) >= 2;
                    if (now >= nextPath && (pathStuck || goalMoved || navigation.CurrentPath == null) && TakePathSlot(now))
                    {
                        nextPath = now + 0.65f;
                        goalColumn = destination.collumn; goalRow = destination.row;
                        // Avoid merging a stale path with the new path to a moving leader.
                        navigation.CurrentPath = null;
                        navigation.GetPath(goalColumn, goalRow, 2f); // native API: milliseconds
                        if (pathStuck)
                        {
                            if (advancing && ++attackStalls >= 2)
                            {
                                abandonedTarget = target; abandonUntil = now + 4f;
                                target = null; nextScan = 0f; attackStalls = 0;
                            }
                            Log("stuck: repath", true);
                        }
                        pathStuck = false;
                    }
                    if (navigation.CurrentPath != null)
                    {
                        navigation.GetMove(ref left, ref right, ref up, ref down, ref jump);
                        if (navigation.IsClimbing) climbUntil = now + 0.3f;
                        if (now < climbUntil)
                        {
                            up = jump = climbButton = true;
                            if (actor.Direction > 0) right = true; else left = true;
                        }
                    }
                    // A stalled PathAgent may return no input at all; it still needs recovery.
                    wasMoving = true;
                }
                else
                {
                    CancelPathRequest();
                    wasMoving = false;
                    sampleTime = now; sampleX = actor.X; sampleY = actor.Y;
                    if (!needsFollow) navigation.CurrentPath = null;
                }
                visibleTarget = IsShootable(target);
                attackState = visibleTarget ? "traversing" : (advancing ? "advance-to-enemy" : "searching");
                if (visibleTarget && !navigation.IsClimbing && now >= climbUntil)
                {
                    int direction = target.X >= actor.X ? 1 : -1;
                    bool facingTarget = (actor.transform.localScale.x > 0f ? 1 : -1) == direction;
                    if (combat)
                    {
                        // Near the leader, stop and turn before firing.
                        if (!facingTarget) { left = direction < 0; right = direction > 0; }
                        else fire = true;
                        attackState = fire ? "firing" : "turning";
                    }
                    else
                    {
                        // While catching up, keep path input and shoot in the travel direction.
                        // Never reverse the path to shoot an enemy behind the squad.
                        bool movingAway = (left && direction > 0) || (right && direction < 0);
                        fire = facingTarget && !movingAway;
                        attackState = fire ? "moving-and-firing" : "catchup-facing-away";
                    }
                }
                lastFire = fire;
                Log("tick", false);
            }
            catch (Exception error)
            {
                left = right = up = down = jump = fire = false;
                failed = true;
                Log("fault: " + error.GetType().Name + " " + error.Message, true);
                SetEnhanced(false);
            }
        }

        private void CancelPathRequest()
        {
            if (pathQueued) pathQueue.Remove(this);
            pathQueued = false;
        }

        private bool TakePathSlot(float now)
        {
            lastPathRequest = now;
            if (!pathQueued) { pathQueue.Add(this); pathQueued = true; }
            // FIFO prevents early-updating villagers from starving the rest of a 20-unit squad.
            while (pathQueue.Count > 0)
            {
                EnhancedVillagerAI head = pathQueue[0];
                if (head != null && head.active && head.actor != null && head.actor.health > 0 &&
                    head.scene == Map.Instance && now - head.lastPathRequest < 1f) break;
                pathQueue.RemoveAt(0);
                if (head != null) head.pathQueued = false;
            }
            if (pathQueue.Count == 0 || pathQueue[0] != this || now < nextGlobalPath ||
                Time.frameCount == lastPathFrame) return false;
            pathQueue.RemoveAt(0); pathQueued = false;
            nextGlobalPath = now + 0.035f; lastPathFrame = Time.frameCount;
            return true;
        }

        private bool IsEnemy(Unit candidate)
        {
            if (candidate == null || candidate == actor || candidate.health <= 0 ||
                !candidate.gameObject.activeInHierarchy || candidate.IsInStealthMode() ||
                candidate is Villager || candidate is Animal || candidate.playerNum == ownerIndex ||
                (candidate.playerNum >= 0 && candidate.playerNum < 4) ||
                !GameModeController.DoesPlayerNumDamage(actor.playerNum, candidate.playerNum)) return false;
            return true;
        }

        private bool IsShootable(Unit candidate)
        {
            if (!IsEnemy(candidate) || candidate.invulnerable) return false;
            float dx = candidate.X - actor.X;
            float muzzleY = actor.Y + 9f;
            if (System.Math.Abs(dx) > VillagerTactics.SightRange ||
                !VillagerTactics.InFiringLane(muzzleY, candidate.Y, candidate.height)) return false;
            Vector3 origin = new Vector3(actor.X, muzzleY, actor.transform.position.z);
            Vector3 end = new Vector3(candidate.X, muzzleY, origin.z);
            // Include the full line starting at the actor, avoiding shots through a nearby wall.
            // LargeObjects includes attackable targets. Do not count their own collider as cover.
            int terrain = LayerMask.NameToLayer("Terrain");
            // Use the same terrain layer as native FollowerMinion.CanSeeEnemy.
            int coverMask = terrain < 0 ? (int)Map.groundLayerStrict : 1 << terrain;
            return !Physics.Raycast(new Ray(origin, end - origin), System.Math.Abs(dx), coverMask);
        }

        private Unit SelectTarget()
        {
            Unit best = null;
            float score = Single.MaxValue;
            scanned = hostileCount = inRangeCount = shootableCount = rejectedTeam = rejectedState = 0;
            if (Map.units == null) return null;
            // Pick in score order before raycasting, so a large map doesn't produce a ray per unit.
            for (int i = 0; i < Map.units.Count; i++)
            {
                Unit candidate = Map.units[i];
                if (candidate == null) continue;
                scanned++;
                if (!IsEnemy(candidate))
                {
                    if (candidate.health <= 0 || !candidate.gameObject.activeInHierarchy || candidate.IsInStealthMode()) rejectedState++;
                    else rejectedTeam++;
                    continue;
                }
                hostileCount++;
                if (candidate == abandonedTarget && Time.time < abandonUntil) continue;
                if (System.Math.Abs(candidate.X - actor.X) > 448f || System.Math.Abs(candidate.Y - actor.Y) > 192f ||
                    (previousLeader != null && (System.Math.Abs(candidate.X - previousLeader.X) > 384f ||
                    System.Math.Abs(candidate.Y - previousLeader.Y) > 192f))) continue;
                inRangeCount++;
                if (catchingUp && previousLeader != null &&
                    (candidate.X - actor.X) * (previousLeader.X - actor.X) < 0f) continue;
                bool canShoot = IsShootable(candidate);
                if (canShoot) shootableCount++;
                float value = VillagerTactics.TargetScore(candidate.X - actor.X, candidate.Y - actor.Y, candidate == target)
                    + (canShoot ? -160f : 0f) + (candidate.invulnerable ? 160f : 0f);
                if (value >= score) continue;
                best = candidate; score = value;
            }
            return best;
        }

        private void Log(string message, bool force)
        {
#if VILLAGER_AI_TEST
            return;
#else
                if (!force && Time.time < nextLog) return;
            nextLog = Time.time + 3f;
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BroforceLifeTrainer");
                Directory.CreateDirectory(directory);
                string file = Path.Combine(directory, "villager_ai_v10.log");
                if (File.Exists(file) && new FileInfo(file).Length > 1024 * 1024) File.WriteAllText(file, "");
                File.AppendAllText(file, DateTime.Now.ToString("s") + " player=" + (ownerIndex + 1) +
                    " unit=" + (actor == null ? 0 : actor.GetInstanceID()) + " " + message +
                    " leader=" + (previousLeader == null ? 0 : previousLeader.GetInstanceID()) +
                    " target=" + (target == null ? 0 : target.GetInstanceID()) + " catchup=" + catchingUp +
                    " path=" + (navigation != null && navigation.CurrentPath != null) +
                    " attack=" + attackState + " fire=" + lastFire +
                    " targetHeight=" + (target == null ? 0f : target.height) +
                    " scanned=" + scanned + " hostile=" + hostileCount + " range=" + inRangeCount +
                    " shootable=" + shootableCount + " rejectedTeam=" + rejectedTeam + " rejectedState=" + rejectedState +
                    " fireRate=" + (actor == null ? 0f : actor.fireRate) +
                    " nativeFireCounter=" + (actor == null || FireCounterField == null ? "unknown" : FireCounterField.GetValue(actor).ToString()) +
                    " actorXY=" + (actor == null ? "none" : actor.X + "," + actor.Y) +
                    " targetXY=" + (target == null ? "none" : target.X + "," + target.Y) + Environment.NewLine);
            }
            catch { } // Diagnostic I/O must never affect gameplay.
#endif
        }
    }
}
