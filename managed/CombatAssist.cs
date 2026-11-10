using System;

namespace BroforceLifeTrainer
{
    // A short native effect expires on its own; never overwrite a longer game effect.
    internal static class CombatAssist
    {
        internal const float ProtectionSeconds = 0.75f;

        // 0 off, 1 active, 2 waiting for hero, 3 online denied, 4 error.
        internal static int MaintainInvincibility(bool requested, bool offline,
            bool ready, TestVanDammeAnim hero)
        {
            if (!requested) return 0;
            if (!offline) return 3;
            if (!ready || hero == null || hero.health <= 0) return 2;
            hero.SetInvulnerable(ProtectionSeconds, false, false);
            return 1;
        }

        // 1 refilled, 2 unavailable, 3 online denied, 4 already full,
        // 5 no refillable special, 6 failure, 7 disconnected.
        internal static int Refill(bool offline, bool ready, TestVanDammeAnim hero)
        {
            if (!offline) return 3;
            if (!ready || hero == null || hero.health <= 0) return 2;
            int maximum = hero.originalSpecialAmmo;
            if (maximum <= 0) return 5;
            if (hero.SpecialAmmo >= maximum) return 4;
            // Virtual native method also refreshes the character's HUD.
            hero.ResetSpecialAmmo();
            return hero.SpecialAmmo >= maximum ? 1 : 6;
        }
    }

    internal sealed class CombatClientLease
    {
        private long lastHeartbeat;
        private float lastChange;
        private bool observed;

        internal bool IsAlive(long heartbeat, float now)
        {
            if (heartbeat == 0) { observed = false; lastHeartbeat = 0; return false; }
            if (!observed || heartbeat != lastHeartbeat)
            {
                observed = true;
                lastHeartbeat = heartbeat;
                lastChange = now;
            }
            return now >= lastChange && now - lastChange < 3f;
        }
    }
}
