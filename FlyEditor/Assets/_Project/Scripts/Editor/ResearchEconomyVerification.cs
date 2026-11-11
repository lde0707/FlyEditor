using System;
using System.Reflection;
using FlyEditor.Flight;
using UnityEditor;
using UnityEngine;

namespace FlyEditor.Editor
{
    public static class ResearchEconomyVerification
    {
        private sealed class Store : IResearchStore
        {
            private ResearchSave data = ResearchSave.NewGame();
            public ResearchSave Load() => data.Copy();
            public void Save(ResearchSave value) { data = value.Copy(); }
        }
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        public static void Run()
        {
            var web = AssetDatabase.LoadAssetAtPath<ResearchWebAssets>("Assets/_Project/Data/ResearchWeb.asset");
            var depth = new int[web.nodes.Length];
            var prices = new[] { 30, 120, 480, 1920, 7680 };
            var funded = new FlightResearch(new Store());
            funded.BeginRun(); funded.CompleteRun(funded.ActiveRunId, 0, out _);
            funded.AdjustDeveloperPoints(1000000);
            int total = 0;
            for (int i = 0; i < web.nodes.Length; i++)
            {
                var n = web.nodes[i];
                depth[i] = n.parent < 0 ? 1 : depth[n.parent] + 1;
                int expected = prices[depth[i] - 1] * (n.unlocksAbility && depth[i] > 1 ? 3 : 2) / 2;
                Check(n.costs[0] == expected && n.costs[0] == ResearchEconomy.NodePrice(depth[i], n.unlocksAbility), "Incorrect price: " + n.id);
                Check(funded.TryBuy(web, i) && !funded.TryBuy(web, i), "Purchase/duplicate failed: " + n.id);
                total += expected;
            }
            Check(total == 337050 && funded.Points == 1000000 - total && funded.OwnedCount == 98, "Full web balance mismatch");
            float[] distances = { 25, 50, 100, 200, 400, 800, 1600 };
            int[] rewards = { 9, 30, 94, 295, 927, 2910, 9133 };
            for (int i = 0; i < distances.Length; i++)
                Check(ResearchEconomy.BaseReward(distances[i]) == rewards[i], "Distance benchmark mismatch");
            Check(ResearchEconomy.BaseReward(float.NaN) == 0 && ResearchEconomy.BaseReward(float.PositiveInfinity) == 0
                && ResearchEconomy.BaseReward(-1) == 0 && ResearchEconomy.BaseReward(float.MaxValue) == int.MaxValue, "Reward boundary failure");
            Check(ResearchEconomy.BonusReward(1600, 100) == 4566 && ResearchEconomy.BonusReward(100, 3) == 14, "Bonus scaling/cap failure");
            var bounded = new FlightResearch(new Store()); bounded.BeginRun(); long run = bounded.ActiveRunId;
            Check(!bounded.CompleteRun(run, float.NaN, out _) && bounded.ActiveRunId == run, "Invalid distance consumed run");
            Check(bounded.CompleteRun(run, float.MaxValue, out int reward, int.MaxValue)
                && reward == int.MaxValue && bounded.Points == int.MaxValue && !bounded.CompleteRun(run, 50, out _), "Overflow/duplicate failure");
            // Reuse transaction, disk reload, session settlement and actual-flight regression checks.
            typeof(FlightVerification).GetMethod("VerifyResearch", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            ResearchWebVerification.VerifyAsset();
            Debug.Log("[ResearchEconomy] PASS: 98 prices/purchases, 337050 total, seven distance benchmarks, bonus cap, overflow, save retry/reload, flight settlement.");
        }
    }
}
