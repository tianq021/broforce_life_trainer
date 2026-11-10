using System;
using BroforceLifeTrainer;

// Models the inspected game's native API, including Max(existing, requested).
public class TestVanDammeAnim
{
    public int health = 1, originalSpecialAmmo = 3, SpecialAmmo = 0;
    public int protections, refills;
    public float time;
    public bool brokenRefill;
    public void SetInvulnerable(float duration, bool bubble, bool halo)
    {
        if (bubble || halo) throw new Exception("unwanted HUD effects");
        time = Math.Max(time, duration);
        protections++;
    }
    public void ResetSpecialAmmo() { refills++; if (!brokenRefill) SpecialAmmo = originalSpecialAmmo; }
}

class CombatAssistSmoke
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Main()
    {
        TestVanDammeAnim first = new TestVanDammeAnim();
        TestVanDammeAnim replacement = new TestVanDammeAnim();
        Check(CombatAssist.MaintainInvincibility(false, true, true, first) == 0 && first.protections == 0, "off must not mutate");
        Check(CombatAssist.MaintainInvincibility(true, false, true, first) == 3 && first.protections == 0, "online denied");
        Check(CombatAssist.MaintainInvincibility(true, true, false, first) == 2 && first.protections == 0, "loading or paused");
        Check(CombatAssist.MaintainInvincibility(true, true, true, null) == 2, "absent player");
        first.time = 5;
        Check(CombatAssist.MaintainInvincibility(true, true, true, first) == 1 && first.time == 5, "preserve native longer effect");
        first.time = 0.1f;
        CombatAssist.MaintainInvincibility(true, true, true, first);
        Check(first.time == 0.75f, "short protection refreshed");
        CombatAssist.MaintainInvincibility(true, true, true, replacement);
        Check(replacement.protections == 1 && first.protections == 2, "new hero receives protection");
        first.health = 0;
        Check(CombatAssist.MaintainInvincibility(true, true, true, first) == 2, "no resurrecting dead body");
        Check(CombatAssist.Refill(true, true, first) == 2 && first.refills == 0, "no dead hero refill");
        first.health = 1;
        Check(CombatAssist.Refill(false, true, first) == 3 && first.refills == 0, "no online refill");
        Check(CombatAssist.Refill(true, false, first) == 2 && first.refills == 0, "no deferred paused refill");
        Check(CombatAssist.Refill(true, true, first) == 1 && first.SpecialAmmo == 3 && first.refills == 1, "native refill");
        first.SpecialAmmo = 6;
        Check(CombatAssist.Refill(true, true, first) == 4 && first.SpecialAmmo == 6 && first.refills == 1, "over-cap ammo not reduced");
        first.originalSpecialAmmo = 0;
        Check(CombatAssist.Refill(true, true, first) == 5 && first.refills == 1, "no special capacity");
        replacement.brokenRefill = true;
        Check(CombatAssist.Refill(true, true, replacement) == 6, "failed native refill reported");
        CombatClientLease lease = new CombatClientLease();
        Check(!lease.IsAlive(0, 0), "no client yet");
        Check(lease.IsAlive(1, 1), "client connected");
        Check(lease.IsAlive(1, 3.9f), "short UI stall tolerated");
        Check(!lease.IsAlive(1, 4), "crashed client expires");
        Check(!lease.IsAlive(1, 100), "stale heartbeat must stay expired");
        Check(lease.IsAlive(2, 101), "new heartbeat resumes lease");
        Check(!lease.IsAlive(0, 101), "normal disconnect is immediate");
        Check(lease.IsAlive(1, 102), "reconnect counter may restart");
        Console.WriteLine("PASS: " + checks + " combat assist behavior checks");
    }
}
