using System;

namespace BroforceLifeTrainer
{
    // World units, not grid cells. Kept free of Unity for executable policy tests.
    public static class VillagerTactics
    {
        public const float SightRange = 288f;
        public static bool ShouldCatchUp(float dx, float dy, bool previous)
        {
            return System.Math.Abs(dx) + System.Math.Abs(dy) > (previous ? 112f : 256f);
        }

        public static bool InFiringLane(float muzzleY, float targetY, float height)
        {
            // Match Map.GeLivingtUnit(player, xRange, 1, x, muzzleY).
            // Unit.height is not a feet-to-head bound: normal mooks serialize height=6.
            float extent = System.Math.Min(height, 4f) + height * 0.5f;
            float center = targetY + extent * 0.5f + 5f;
            return System.Math.Abs(center - muzzleY) < extent + 1f;
        }

        public static float TargetScore(float dx, float dy, bool current)
        {
            return System.Math.Abs(dx) + System.Math.Abs(dy) * 2f - (current ? 12f : 0f);
        }

        public static bool ShouldRepath(bool wantsMove, float moved, float seconds)
        {
            return wantsMove && moved < 4f && seconds >= 1.8f;
        }
    }
}
