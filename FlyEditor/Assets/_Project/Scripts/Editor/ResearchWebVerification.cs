using System;
using FlyEditor.Flight;
using UnityEditor;
using UnityEngine;

namespace FlyEditor.Editor
{
    public static class ResearchWebVerification
    {
        [MenuItem("Tools/FlyEditor/Preview Complete Research Web (Play Mode)", priority = 24)]
        public static void Preview()
        {
            var controller = UnityEngine.Object.FindAnyObjectByType<PaperFlightController>();
            if (controller == null || !controller.SetResearchPreview(true)) Debug.LogWarning("Start Play in InGame while not flying.");
        }
        [MenuItem("Tools/FlyEditor/Preview Complete Research Web (Play Mode)", true)]
        public static bool CanPreview() => EditorApplication.isPlaying;
        public static void VerifyAsset()
        {
            var skin = AssetDatabase.LoadAssetAtPath<ResearchWebAssets>("Assets/_Project/Data/ResearchWeb.asset");
            Require(skin != null && skin.nodes.Length == 98 && skin.families.Length == 5, "Web data missing.");
            Require(skin.background != null && skin.panel != null && skin.root != null && skin.currency != null && skin.backButton != null, "Web art missing.");
            int active = 0, abilities = 0;
            var ids = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < skin.nodes.Length; i++)
            {
                var n = skin.nodes[i];
                Require(ids.Add(n.id) && n.costs.Length == 1 && n.costs[0] > 0, "Node must have exactly one purchase.");
                Require(n.family >= 0 && n.family < 5, "Invalid family.");
                Require(n.normal.Length == 2 && n.selected.Length == 2, "Expected unowned and owned states only.");
                Require(n.icon != null && n.icon.width == n.icon.height, "Icon aspect must be 1:1.");
                for (int r = 0; r < 2; r++)
                    Require(n.normal[r] != null && n.selected[r] != null && n.normal[r] != n.selected[r]
                        && n.normal[r].width == n.normal[r].height && n.selected[r].width == n.selected[r].height, "Square state art missing.");
                Require(n.parent < skin.nodes.Length && n.parent != i, "Invalid parent.");
                if (n.runtimeIndex >= 0) active++;
                if (n.unlocksAbility) abilities++;
                foreach (int p in n.prerequisiteIndices) Require(p >= 0 && p < i, "Prerequisites must be earlier, acyclic nodes.");
                Require(n.parent == -1 ? n.prerequisiteIndices.Length == 0 :
                    n.prerequisiteIndices.Length >= 1 && n.prerequisiteIndices[0] == n.parent && n.anyPrerequisite,
                    "Drawn connection must match the unlock prerequisite.");
            }
            Require(active == 98 && abilities == 16, "Every node must have a flight effect.");
            foreach (var f in skin.families)
                Require(f.ready != null && f.pressed != null && f.success != null && f.maximum != null && f.panel != null
                    && f.feedback.Length == 3 && Array.TrueForAll(f.feedback, t => t != null), "Family art missing.");
            VerifyLegacyMapping(skin); VerifyNavigation(); VerifyFlightAbilities(); Research98Verification.Run(skin);
            Debug.Log("[ResearchWeb] ASSET CHECKS PASSED: all 98 nodes, five families, reference geometry and square art.");
        }
        private sealed class MemoryStore : IResearchStore
        {
            private ResearchSave data;
            public MemoryStore(ResearchSave value) { data = value; }
            public ResearchSave Load() => data.Copy();
            public void Save(ResearchSave value) { data = value.Copy(); }
        }
        private static int Find(ResearchWebAssets skin, string id) => Array.FindIndex(skin.nodes, n => n.id == id);
        private static void VerifyLegacyMapping(ResearchWebAssets skin)
        {
            for (int oldRank = 0; oldRank <= 3; oldRank++)
            {
                var research = new FlightResearch(new MemoryStore(new ResearchSave { completedFlights = 1, points = 1000, a1 = oldRank, g1 = oldRank, c1 = 2 }));
                foreach (string key in new[] { "Launch", "Glide" })
                    for (int stage = 1; stage <= 3; stage++)
                    {
                        int i = Find(skin, key + "_" + stage);
                        Require(ResearchWebAssets.IsOwned(skin.nodes[i], research) == (stage <= oldRank), "Legacy rank mapping lost ownership.");
                    }
                Require(research.Rank(ResearchNode.Control) == 2 && research.Points == 1000, "Mapping altered legacy progress.");
            }
            foreach (string first in new[] { "Launch_2", "Drag_1", "Boost_1" })
            {
                var store = new MemoryStore(new ResearchSave { completedFlights = 1, points = 1000 });
                var r = new FlightResearch(store);
                Require(!r.TryBuy(skin, Find(skin, first)), "Locked child purchased.");
                Require(r.TryBuy(skin, Find(skin, "Launch_1")), "Root purchase failed.");
                foreach (string id in new[] { "Launch_2", "Drag_1", "Boost_1" })
                    Require(skin.CanBuy(Find(skin, id), r), "Sibling did not open immediately.");
                int firstIndex = Find(skin, first);
                Require(r.TryBuy(skin, firstIndex) && !r.TryBuy(skin, firstIndex), "One-time purchase failed.");
                r = new FlightResearch(store);
                foreach (string id in new[] { "Launch_2", "Drag_1", "Boost_1" })
                {
                    Require(r.Owns(id) == (id == first), "Reload gave ownership of an unchosen sibling.");
                    Require(skin.CanBuy(Find(skin, id), r) == (id != first), "Other branches disappeared.");
                }
                Require(r.Points == 970 - skin.nodes[firstIndex].costs[0], "Charged incorrect node cost.");
            }
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flyeditor-branch-" + Guid.NewGuid() + ".json");
            try
            {
                var disk = new ResearchFileStore(path);
                var fresh = new FlightResearch(disk);
                Require(fresh.Points == 0 && !fresh.Unlocked && Array.TrueForAll(skin.nodes, n => !fresh.Owns(n.id)),
                    "Missing save must start with zero purchased nodes.");
                disk.Save(new ResearchSave { completedFlights = 2, points = 600, g1 = 1, a1 = 1, c1 = 2, bestDistance = 42f });
                var r = new FlightResearch(disk);
                Require(r.TryBuy(skin, Find(skin, "Climb_1")) && r.TryBuy(skin, Find(skin, "Drag_1")),
                    "Reference layout branch purchases failed.");
                var reloaded = new FlightResearch(disk);
                Require(reloaded.Owns("Climb_1") && !reloaded.Owns("Climb_2") &&
                    reloaded.Owns("Drag_1") && !reloaded.Owns("Drag_2") && reloaded.Points == 540,
                    "File reload changed ownership or funds.");
                var baseline = new FlightTuning(); var tuning = reloaded.Apply(baseline);
                Require(Mathf.Approximately(tuning.drag, baseline.drag * .94f)
                    && Mathf.Approximately(tuning.climbDrag, baseline.climbDrag * .90f)
                    && reloaded.BestDistance == 42f && reloaded.Rank(ResearchNode.Control) == 2,
                    "Selected effects or legacy progress lost.");
                Require(!skin.IsVisible(Find(skin, "Shield_1"), reloaded), "Missing prerequisite exposed shield.");
                var earlierLayout = disk.Load();
                earlierLayout.ownedNodeIds = new[] { "Launch_1", "Glide_1", "Climb_2", "Drag_2" };
                disk.Save(earlierLayout);
                var restored = new FlightResearch(disk);
                Require(restored.Owns("Drag_2") && !restored.Owns("Drag_1") && restored.Owns("Climb_2")
                    && !restored.Owns("Climb_1") && restored.Points == 540, "Previous graph purchases were lost.");
            }
            finally
            {
                foreach (string suffix in new[] { "", ".bak", ".tmp" }) System.IO.File.Delete(path + suffix);
            }
            Debug.Log("[ResearchWeb] REFERENCE CHECKS PASSED: empty new game, reference branches, previous-layout purchases, flight effects.");
        }
        private static FlightModel LaunchWith(string[] ids, float height = 25)
        {
            var research = new FlightResearch(new MemoryStore(new ResearchSave { version = 2, ownedNodeIds = ids }));
            var model = new FlightModel(research.Apply(new FlightTuning { launchHeight = height }), 0);
            model.Tick(true, false, false); model.Tick(false, false, false);
            return model;
        }
        private static void VerifyFlightAbilities()
        {
            string[] groups = { "Boost", "Dive", "Recover", "Durability", "Impact", "Shield", "Magnet" };
            var ids = new System.Collections.Generic.List<string>();
            foreach (string group in groups)
                for (int rank = 1; rank <= 3; rank++) ids.Add(group + "_" + rank);
            var full = LaunchWith(ids.ToArray());
            float speed = full.Speed; float energy = full.Energy; full.CollectBoost();
            Require(Mathf.Approximately(full.ForwardBoost, 2.9f) && full.Speed == speed && full.Energy == energy, "Boost pickup strength incorrect.");
            Require(full.Durability == 130 && full.ShieldUses == 2 && full.RecoveryUses == 2, "Per-flight loadout incorrect.");
            speed = full.Speed; full.StrikeObstacle(55);
            Require(full.Durability == 130 && full.ShieldUses == 1
                && Mathf.Approximately(full.Speed, speed * (1 - .28f * .64f * .5f)), "Shield or impact speed retention failed.");
            Require(full.TryRecoverPosture() && !full.TryRecoverPosture(), "Recovery re-entry or consumption failed.");
            for (int i = 0; i < 7; i++) full.FixedTick(.02f);
            Require(full.Posture == FlightModel.Attitude.Normal && full.RecoveryUses == 1, "Faster posture recovery failed.");
            full.StrikeObstacle(55); full.StrikeObstacle(55);
            Require(full.ShieldUses == 0 && full.Durability == 75 && full.TryRecoverPosture() && full.RecoveryUses == 0,
                "Shield charges, HP or second recovery failed.");
            full.StrikeObstacle(100); Require(full.State == FlightModel.Phase.Landed, "HP depletion failed.");
            CheckDive(); CheckCourse(ids.ToArray());
            Debug.Log("[ResearchWeb] FLIGHT ABILITIES PASSED: boost, dive, recovery, HP, collision loss, shield and magnet.");
        }
        private static void CheckDive()
        {
            var plain = LaunchWith(Array.Empty<string>()); var first = LaunchWith(new[] { "Dive_1" });
            var upgraded = LaunchWith(new[] { "Dive_1", "Dive_2", "Dive_3" }); bool early = false;
            for (int i = 0; i < 140; i++)
            {
                float energy = upgraded.Energy;
                plain.FixedTick(.02f); first.FixedTick(.02f); upgraded.FixedTick(.02f);
                early |= upgraded.DiveBoostActive && !first.DiveBoostActive;
                Require(upgraded.Energy <= energy + .0001f, "Dive injected free energy.");
            }
            Require(early && upgraded.DiveBoostActive && upgraded.Speed > plain.Speed, "Descent benefit or onset failed.");
            first.Tick(false, true, false); upgraded.Tick(false, true, false);
            first.FixedTick(.02f); upgraded.FixedTick(.02f);
            Require(!first.DiveBoostActive && upgraded.DiveBoostActive, "Dive carry failed.");
            for (int i = 0; i < 70; i++) upgraded.FixedTick(.02f);
            Require(!upgraded.DiveBoostActive, "Dive carry never expired.");
        }
        private static void CheckCourse(string[] ids)
        {
            bool sawBoost = false, sawHit = false, sawPull = false;
            for (int h = 4; h <= 12; h++)
            {
                var model = LaunchWith(ids, h); var course = new FlightCourse();
                for (int i = 0; i < 600 && model.State == FlightModel.Phase.Flying; i++)
                {
                    var from = new Vector2(model.Distance, model.Height);
                    model.FixedTick(.02f); course.Tick(model, from, .02f);
                    sawBoost |= course.PickupsCollected > 0; sawHit |= course.Collisions > 0;
                    sawPull |= course.PickupsCollected == 0 && course.Pickup.x < 15.99f;
                }
            }
            Require(sawBoost && sawHit && sawPull, "Real flight routes did not exercise pickup, hazard and attraction.");
            var weak = LaunchWith(new[] { "Magnet_1" });
            var strong = LaunchWith(new[] { "Magnet_1","Magnet_2","Magnet_3" });
            Require(weak.Effects.magnetRadius == 3 && strong.Effects.magnetRadius == 5
                && strong.Effects.magnetPull > weak.Effects.magnetPull, "Magnet upgrades did not affect reach and speed.");
        }
        private static void VerifyNavigation()
        {
            var nav = new ResearchWebNavigation(); var size = new Vector2(1408, 790); var cursor = new Vector2(430, 240);
            Vector2 world = nav.ViewToWorld(cursor, size);
            nav.ZoomAt(.5f, cursor, size);
            Require(Vector2.Distance(nav.WorldToView(world, size), cursor) < .001f, "Zoom anchor drifted.");
            nav.ZoomAt(0, cursor, size); Require(nav.Zoom == ResearchWebNavigation.MinimumZoom, "Zoom-out limit failed.");
            nav.ZoomAt(99, cursor, size); Require(nav.Zoom == ResearchWebNavigation.MaximumZoom, "Zoom-in limit failed.");
            Vector2 before = nav.Pan; nav.Drag(Vector2.one); Require(nav.Pan == before, "Idle drag changed pan.");
            nav.BeginPan(); nav.Drag(new Vector2(100, 20)); Require(nav.Pan == before + new Vector2(100, 20), "Panning failed.");
            nav.Cancel(); Require(nav.State == ResearchWebNavigation.Phase.Idle, "Panning did not exit.");
            Rect bounds = new Rect(-880, -730, 1700, 1460); nav.Fit(bounds, size);
            Vector2 min = nav.WorldToView(bounds.min, size), max = nav.WorldToView(bounds.max, size);
            Require(min.x >= 31 && min.y >= 31 && max.x <= size.x - 31 && max.y <= size.y - 31, "Fit clipped graph.");
            nav.Reset(); Require(nav.Zoom == 1 && nav.Pan == Vector2.zero, "Reset did not restore view.");
            Debug.Log("[ResearchWeb] NAVIGATION CHECKS PASSED: cursor anchor, limits, pan FSM, fit, reset.");
        }
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("[ResearchWeb] " + message); }
    }
}
