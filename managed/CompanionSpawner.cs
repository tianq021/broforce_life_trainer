using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BroforceLifeTrainer
{
    internal static class CompanionSpawner
    {
        private static readonly List<CompanionAI> companions = new List<CompanionAI>();
        private static readonly float[] nextSpawn = new float[4];
        private static Map scene;
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        internal static string Maintain(int owner)
        {
            string error = null;
            for (int i = companions.Count - 1; i >= 0; i--)
            {
                CompanionAI ai = companions[i];
                if (ai == null) { companions.RemoveAt(i); continue; }
                if (ai.Failed && ai.OwnerIndex != owner && Map.Instance == scene && Connect.IsOffline) continue;
                TestVanDammeAnim unit = ai.GetComponent<TestVanDammeAnim>();
                if (Map.Instance != scene || !Connect.IsOffline || unit == null || unit.health <= 0 || ai.Failed)
                {
                    if (ai.Failed && ai.OwnerIndex == owner) error = "电脑队友 AI 异常，已移除；详情见 companion_ai_v12.log";
                    // Deactivate immediately: no stale inputs or player-death callbacks on cleanup.
                    ai.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(ai.gameObject);
                    companions.RemoveAt(i);
                }
            }
            if (scene != Map.Instance)
            { scene = Map.Instance; Array.Clear(nextSpawn, 0, nextSpawn.Length); }
            return error;
        }

        internal static string Spawn(Player player, int owner, HeroType heroType)
        {
            if (!Connect.IsOffline || GameModeController.IsHardcoreMode || GameModeController.IsDeathMatchMode)
                throw new InvalidOperationException("电脑队友仅支持普通离线单人 / 本地合作关卡");
            if (owner < 0 || owner >= 4 || Map.Instance == null || player == null || !player.IsAlive() ||
                player.character == null || player.IsPendingRespawnOrResuingInProgress() ||
                Time.timeScale <= 0f || GameModeController.IsLevelFinished())
                throw new InvalidOperationException("请在所选玩家存活且关卡正常运行时生成电脑队友");
            if (Time.realtimeSinceStartup < nextSpawn[owner]) throw new InvalidOperationException("请稍后再生成电脑队友");
            if (companions.Count >= 4) throw new InvalidOperationException("本关存活电脑队友已达 4 名");
            TestVanDammeAnim prefab = HeroController.GetHeroPrefab(heroType);
            if (prefab == null) throw new InvalidOperationException("未找到所选人物模板");
            FieldInfo heroFlag = typeof(NetworkedUnit).GetField("isHero", Fields);
            FieldInfo enemyFlag = typeof(NetworkedUnit).GetField("isEnemy", Fields);
            FieldInfo reducedLife = typeof(TestVanDammeAnim).GetField("reducedLife", Fields);
            if (heroFlag == null || enemyFlag == null || reducedLife == null)
                throw new InvalidOperationException("当前游戏版本不兼容电脑队友身份隔离");
            Vector3 pos = player.GetCharacterPosition();
            bool found = false;
            foreach (float offset in new float[] { 24f, -24f, 40f, -40f })
            {
                float x = pos.x + offset, ground = Map.GetGroundHeight(x, pos.y + 8f);
                if (x <= 0f || ground < 0f || System.Math.Abs(ground - pos.y) > 24f) continue;
                if (Physics.CheckSphere(new Vector3(x, ground + 10f, pos.z), 8f, Map.groundLayer)) continue;
                pos = new Vector3(x, ground + 2f, pos.z); found = true; break;
            }
            if (!found) throw new InvalidOperationException("身旁没有足够空间，请到开阔地面生成电脑队友");
            GameObject staging = new GameObject("Companion staging");
            staging.SetActive(false);
            GameObject clone = null;
            try
            {
                // Inactive parent prevents Awake/Start from seeing a player-controlled hero.
                clone = UnityEngine.Object.Instantiate(prefab.gameObject, staging.transform, false) as GameObject;
                clone.SetActive(false);
                TestVanDammeAnim unit = clone.GetComponent<TestVanDammeAnim>();
                heroFlag.SetValue(unit, false);
                enemyFlag.SetValue(unit, (bool?)false);
                reducedLife.SetValue(unit, true);
                // Native Start writes grenade HUD counts unconditionally for player 0-3.
                // Use the NPC index until the first native CheckInput (after Start).
                unit.playerNum = -1;
                unit.player = player;
                unit.heroType = heroType;
                unit.deathNotificationSent = true;
                unit.SyncDestroy = false;
                clone.name = "电脑队友 " + heroType + " P" + (owner + 1);
                clone.transform.position = pos;
                PathAgent path = clone.GetComponent<PathAgent>();
                if (path == null) path = clone.AddComponent<PathAgent>();
                path.agentSize = 9f;
                path.capabilities = new PathAgentCapabilities();
                path.capabilities.canJump = true;
                path.capabilities.canUseLadders = true;
                path.capabilities.canWallClimb = true;
                path.capabilities.jumpHeight = 3;
                path.capabilities.maxDropDistance = 3;
                CompanionAI ai = CompanionAI.Attach(unit, owner, path);
                clone.transform.SetParent(null, true);
                clone.SetActive(true);
                unit.SetXY(pos.x, pos.y);
                unit.SetSpawnState(0f, 0f, false, false, false, false);
                // Verify identity after native Awake. Do not accept a clone that became a player.
                if (unit.IsHero || player.character == unit || unit.enemyAI != ai)
                    throw new InvalidOperationException("电脑队友初始化未通过身份检查");
                companions.Add(ai);
                nextSpawn[owner] = Time.realtimeSinceStartup + .75f;
                return "已生成电脑队友：" + heroType + "（跟随玩家 " + (owner + 1) + "，本关 " + companions.Count + "/4）";
            }
            catch
            {
                if (clone != null) { clone.SetActive(false); UnityEngine.Object.Destroy(clone); }
                throw;
            }
            finally { UnityEngine.Object.Destroy(staging); }
        }
    }
}
