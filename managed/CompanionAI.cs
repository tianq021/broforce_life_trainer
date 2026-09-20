using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using UnityEngine;

namespace BroforceLifeTrainer
{
    // A selected hero prefab with NPC input. Never register it as Player.character.
    public sealed class CompanionAI : PolymorphicAI
    {
        private TestVanDammeAnim actor;
        private PathAgent navigation;
        private Map scene;
        private int owner;
        private Unit target, leader, abandoned;
        private float nextScan, nextPath, sampleTime, sampleX, sampleY, abandonUntil, climbUntil;
        private bool catchingUp, moving, queued;
        private int stalls;
        private float lastRequest;
#if !COMPANION_AI_TEST
        private float nextLog;
#endif
        private static readonly List<CompanionAI> queue = new List<CompanionAI>();
        private static float nextGlobalPath;
        private static int lastPathFrame = -1;
        internal bool Failed;
        internal int OwnerIndex { get { return owner; } }

        // Native Awake only caches unit/path references; retain it for native reactions.
        // Update/LateUpdate do not run the native patrol or network synchronization.
        protected override void Awake() { base.Awake(); }
        protected override void LateUpdate() { }
        protected override void OnDestroy() { CancelPath(); }
        protected override void Update()
        {
            if (actor != null && (!Connect.IsOffline || Map.Instance != scene))
            { CancelPath(); UnityEngine.Object.Destroy(actor.gameObject); }
        }

        internal static CompanionAI Attach(TestVanDammeAnim unit, int player, PathAgent path)
        {
            CompanionAI ai = unit.gameObject.AddComponent<CompanionAI>();
            ai.actor = unit; ai.owner = player; ai.scene = Map.Instance; ai.navigation = path;
            unit.enemyAI = ai; unit.enemyAIOnChildOrParent = ai;
            ai.sampleX = unit.X; ai.sampleY = unit.Y; ai.sampleTime = Time.time;
            return ai;
        }

        public override void GetInput(ref bool left, ref bool right, ref bool up, ref bool down,
            ref bool jump, ref bool fire, ref bool s1, ref bool s2, ref bool s3, ref bool s4, ref bool climb)
        {
            left = right = up = down = jump = fire = s1 = s2 = s3 = s4 = climb = false;
            if (Failed || actor == null || actor.health <= 0) { CancelPath(); return; }
            try
            {
                // Called by native TestVanDammeAnim.CheckInput, so native Start has completed.
                // Team ownership now applies to the original hero projectiles.
                actor.playerNum = owner;
                if (!Connect.IsOffline || Map.Instance != scene || Time.timeScale <= 0f ||
                    GameModeController.IsLevelFinished() || actor.IsBlind())
                { CancelPath(); return; }
                Player player = HeroController.players == null || owner < 0 || owner >= HeroController.players.Length
                    ? null : HeroController.players[owner];
                if (player == null || !player.IsAlive() || player.character == null || player.IsPendingRespawnOrResuingInProgress())
                { target = null; moving = false; navigation.CurrentPath = null; CancelPath(); return; }
                if (leader != player.character)
                { leader = player.character; navigation.CurrentPath = null; nextPath = 0; }
                float now = Time.time, dx = leader.X - actor.X, dy = leader.Y - actor.Y;
                catchingUp = VillagerTactics.ShouldCatchUp(dx, dy, catchingUp);
                if (now >= nextScan) { target = SelectTarget(); nextScan = now + .15f; }
                bool shootable = Shootable(target);
                bool combat = !catchingUp && shootable && !navigation.IsClimbing && now >= climbUntil;
                bool advance = !catchingUp && Enemy(target) && !shootable;
                Unit goal = advance ? target : leader;
                bool needsMove = advance || System.Math.Abs(dx) > 28f || System.Math.Abs(dy) > 12f;
                if (!combat && needsMove)
                {
                    bool stuck = VillagerTactics.ShouldRepath(moving,
                        System.Math.Abs(actor.X - sampleX) + System.Math.Abs(actor.Y - sampleY), now - sampleTime);
                    if (!moving) { sampleTime = now; sampleX = actor.X; sampleY = actor.Y; }
                    if (now >= nextPath && TakePath(now))
                    {
                        nextPath = now + .65f;
                        navigation.CurrentPath = null;
                        navigation.GetPath(goal.collumn, goal.row, 2f);
                        if (stuck && advance && ++stalls >= 2)
                        { abandoned = target; abandonUntil = now + 4f; target = null; nextScan = 0; stalls = 0; }
                        if (now - sampleTime >= 1.8f)
                        { if (!stuck) stalls = 0; sampleTime = now; sampleX = actor.X; sampleY = actor.Y; }
                    }
                    if (navigation.CurrentPath != null)
                    {
                        navigation.GetMove(ref left, ref right, ref up, ref down, ref jump);
                        if (navigation.IsClimbing) climbUntil = now + .3f;
                        if (now < climbUntil)
                        { up = jump = climb = true; if (actor.Direction > 0) right = true; else left = true; }
                    }
                    moving = true;
                }
                else { CancelPath(); moving = false; navigation.CurrentPath = null; }
                if (Shootable(target) && !navigation.IsClimbing && now >= climbUntil)
                {
                    int direction = target.X >= actor.X ? 1 : -1;
                    bool facing = actor.Direction == direction;
                    if (combat)
                    { if (!facing) { left = direction < 0; right = direction > 0; } else fire = true; }
                    else fire = facing && !(left && direction > 0) && !(right && direction < 0);
                }
                Log("tick fire=" + fire + " catchup=" + catchingUp, false);
            }
            catch (Exception error)
            {
                left = right = up = down = jump = fire = s1 = s2 = s3 = s4 = climb = false;
                Failed = true; CancelPath(); Log("fault " + error, true);
            }
        }

        private bool Enemy(Unit value)
        {
            return value != null && value != actor && value.health > 0 && value.gameObject.activeInHierarchy &&
                !value.IsInStealthMode() && !(value is Villager) && !(value is Animal) &&
                !(value.playerNum >= 0 && value.playerNum < 4) &&
                GameModeController.DoesPlayerNumDamage(actor.playerNum, value.playerNum);
        }
        internal static float AttackRange(int hero)
        {
            // Blade, Brochete, Bronan, Lee Broxmas, The Brode, Broveheart,
            // The Brolander, Bro Lee, Xebro and Scorpion Bro need melee distance.
            switch (hero)
            {
                case 4: case 17: case 18: case 25: case 31:
                case 37: case 41: case 42: case 45: case 46: return 20f;
                case 2: return 72f; // flamethrower
                case 40: return 80f; // lightning
                default: return 288f;
            }
        }

        private bool Shootable(Unit value)
        {
            if (!Enemy(value) || value.invulnerable) return false;
            float dx = value.X - actor.X, muzzle = actor.Y + 10f;
            if (System.Math.Abs(dx) > AttackRange((int)actor.heroType) || !VillagerTactics.InFiringLane(muzzle, value.Y, value.height)) return false;
            int layer = LayerMask.NameToLayer("Terrain");
            return !Physics.Raycast(new Ray(new Vector3(actor.X, muzzle, actor.transform.position.z),
                new Vector3(dx, 0, 0)), System.Math.Abs(dx), layer < 0 ? (int)Map.groundLayerStrict : 1 << layer);
        }
        private Unit SelectTarget()
        {
            Unit best = null; float score = Single.MaxValue;
            if (Map.units == null) return null;
            foreach (Unit value in Map.units)
            {
                if (!Enemy(value) || (value == abandoned && Time.time < abandonUntil) ||
                    System.Math.Abs(value.X - actor.X) > 448f || System.Math.Abs(value.Y - actor.Y) > 192f ||
                    System.Math.Abs(value.X - leader.X) > 384f || System.Math.Abs(value.Y - leader.Y) > 192f) continue;
                float candidate = VillagerTactics.TargetScore(value.X - actor.X, value.Y - actor.Y, value == target)
                    + (Shootable(value) ? -160f : 0f) + (value.invulnerable ? 160f : 0f);
                if (candidate < score) { best = value; score = candidate; }
            }
            return best;
        }
        private void CancelPath() { if (queued) queue.Remove(this); queued = false; }
        private bool TakePath(float now)
        {
            lastRequest = now;
            if (!queued) { queue.Add(this); queued = true; }
            while (queue.Count > 0)
            {
                CompanionAI head = queue[0];
                if (head != null && !head.Failed && head.actor != null && head.actor.health > 0 &&
                    head.scene == Map.Instance && now - head.lastRequest < 1f) break;
                queue.RemoveAt(0); if (head != null) head.queued = false;
            }
            if (queue.Count == 0 || queue[0] != this || now < nextGlobalPath || lastPathFrame == Time.frameCount) return false;
            queue.RemoveAt(0); queued = false; nextGlobalPath = now + .05f; lastPathFrame = Time.frameCount;
            return true;
        }
        private void Log(string message, bool force)
        {
#if COMPANION_AI_TEST
            return;
#else
            if (!force && Time.time < nextLog) return;
            nextLog = Time.time + 3f;
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BroforceLifeTrainer");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "companion_ai_v12.log");
                if (File.Exists(file) && new FileInfo(file).Length > 1024 * 1024) File.WriteAllText(file, "");
                File.AppendAllText(file, DateTime.Now.ToString("s") + " player=" + (owner + 1) + " " + message +
                    " actor=" + actor.X + "," + actor.Y + " target=" +
                    (target == null ? "none" : target.X + "," + target.Y) + Environment.NewLine);
            }
            catch { }
#endif
        }
    }
}
