using System;
using System.Linq;
using FlyEditor.Flight;
using UnityEngine;

namespace FlyEditor.Editor
{
    public static class Research98Verification
    {
        private sealed class Store : IResearchStore
        {
            public ResearchSave Data;
            public bool Fail;
            public ResearchSave Load() => Data.Copy();
            public void Save(ResearchSave data)
            {
                if (Fail) throw new System.IO.IOException("Simulated write failure");
                Data = data.Copy();
            }
        }
        private static FlightResearch Research(params string[] ids) => new FlightResearch(new Store
        { Data = new ResearchSave { version = 2, completedFlights = 1, points = 20000, ownedNodeIds = ids } });
        private static FlightModel Launch(float height, params string[] ids)
        {
            var plane = new FlightModel(Research(ids).Apply(new FlightTuning { launchHeight = height }));
            plane.Tick(true, false, false); plane.Tick(false, false, false);
            return plane;
        }
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("[Research98] " + message); }
        public static void Run(ResearchWebAssets web)
        {
            Check(web.rootFrameSize > web.nodes.Max(n => n.frameSize), "Start must be largest.");
            Check(web.rootOutline.Length == 8, "Expected octagonal neutral start.");
            int edges = 0, shared = 0, extraNodes = 0;
            foreach (var node in web.nodes)
            {
                Check(node.outline.Length == (node.unlocksAbility ? 6 : 5) && node.selection != null, "Incorrect node silhouette.");
                Check(node.frameSize == (node.unlocksAbility ? 144 : node.parent < 0 ? 112 : 96), "Size hierarchy.");
                if (node.prerequisiteIndices.Length > 1) shared++;
                foreach (int parent in node.prerequisiteIndices)
                {
                    var bought = Research(web.nodes[parent].id);
                    int index = Array.IndexOf(web.nodes, node);
                    Check(web.CanBuy(index, bought), "Single incoming purchase must open " + node.id);
                }
                int[] parentIndices = node.prerequisiteIndices.Length > 0 ? node.prerequisiteIndices : new[] { -1 };
                foreach (int parent in parentIndices)
                {
                    edges++;
                    var before = parent < 0 ? null : web.nodes[parent];
                    Vector2 from = before == null ? Vector2.zero : before.position;
                    float frame = before == null ? web.rootFrameSize : before.frameSize;
                    Vector2[] polygon = before == null ? web.rootOutline : before.outline;
                    VerifyBoundary(from, node.position, frame, polygon);
                    VerifyBoundary(node.position, from, node.frameSize, node.outline);
                }
                if (node.runtimeIndex != 12) continue;
                extraNodes++;
                var stats = Research(node.id).Apply(new FlightTuning()).effects.extra;
                Check(typeof(ExtendedResearchStats).GetFields().Any(f => (float)f.GetValue(stats) > 0),
                    "No configured live effect for " + node.id);
            }
            Check(edges == 144 && shared > 0 && extraNodes == 65, "Incomplete graph.");
            Rect bounds = new Rect(-120, -120, 240, 240);
            foreach (var node in web.nodes)
            {
                float half = ResearchWebGeometry.TextureSize(node.frameSize) * .5f;
                bounds.xMin = Mathf.Min(bounds.xMin,node.position.x-half);
                bounds.xMax = Mathf.Max(bounds.xMax,node.position.x+half);
                bounds.yMin = Mathf.Min(bounds.yMin,node.position.y-half);
                bounds.yMax = Mathf.Max(bounds.yMax,node.position.y+half);
            }
            var navigation = new ResearchWebNavigation();
            var viewport = new Vector2(1408,790);
            navigation.Fit(bounds,viewport);
            var min = navigation.WorldToView(bounds.min,viewport);
            var max = navigation.WorldToView(bounds.max,viewport);
            Check(min.x >= 31 && min.y >= 31 && max.x <= 1377 && max.y <= 759, "Full 98-node fit clipped.");
            var old = new Store { Data = new ResearchSave { version = 2, completedFlights = 1,
                points = 20000, a1 = 3, g1 = 3, c1 = 2, bestDistance = 88,
                ownedNodeIds = web.nodes.Where(n => n.runtimeIndex < 12).Select(n => n.id).ToArray() } };
            var saved = new FlightResearch(old);
            Check(old.Data.ownedNodeIds.All(saved.Owns) && saved.Rank(ResearchNode.Control) == 2 && saved.BestDistance == 88,
                "Old progress lost.");
            int candidate = Array.FindIndex(web.nodes, n => n.runtimeIndex == 12 && web.CanBuy(Array.IndexOf(web.nodes,n),saved));
            old.Fail = true;
            Check(!saved.TryBuy(web,candidate) && saved.Points == 20000 && !saved.Owns(web.nodes[candidate].id),
                "Failed save spent points or granted ownership.");
            old.Fail = false;
            Check(saved.TryBuy(web,candidate), "Retry purchase failed.");
            VerifyBonuses();
            VerifyAbilities();
            VerifyFullFlight(web);
            VerifyProgressionAndPickups(web);
            VerifyGentleDescentBalance(web);
            Debug.Log("[Research98] PASSED: 98 nodes, 144 contour endpoints, OR branches, 65 live modifiers, old save, failed-save retry, rewards and abilities.");
        }
        private static void VerifyGentleDescentBalance(ResearchWebAssets web)
        {
            string[] glideIds = { "Glide_1", "Glide_2", "Glide_3", "Web_LevelFlight", "Web_WingTip",
                "Web_GentleDescent", "Web_PitchBrake", "Web_LandingAttitude" };
            float previousSlope = 1f;
            for (int count = 0; count <= glideIds.Length; count++)
            {
                var tuning = Research(glideIds.Take(count).ToArray()).Apply(new FlightTuning());
                float slope = Mathf.Tan(tuning.divePitch*Mathf.Deg2Rad) / Mathf.Tan(-24f*Mathf.Deg2Rad);
                Check(count == 0 || slope < previousSlope, "Each glide upgrade must make descent shallower.");
                Check(tuning.releasePitchDuration == 3f, "Glide upgrades changed launch transition time.");
                previousSlope = slope;
            }
            Check(Mathf.Abs(previousSlope - .7329f) < .00001f, "Full glide slope multiplier.");
            var allIds = web.nodes.Select(n => n.id).ToArray();
            var store = new Store { Data = new ResearchSave { version = 2, completedFlights = 1, points = 123, ownedNodeIds = allIds } };
            var loaded = new FlightResearch(store);
            Check(allIds.All(loaded.Owns) && loaded.Points == 123, "Slope rebalance lost saved purchases.");
            foreach (float dt in new[] { .01f, .02f, .04f })
            {
                foreach (var research in new[] { Research(), loaded })
                {
                    var tuning = research.Apply(new FlightTuning { launchHeight = 200 });
                    var plane = new FlightModel(tuning);
                    plane.Tick(true, false, false); plane.Tick(false, false, false);
                    for (int i = 0; i < Mathf.RoundToInt(1f / dt); i++)
                    { plane.Tick(false, true, false); plane.FixedTick(dt); }
                    float startPitch = plane.Pitch, firstDown = -1f;
                    for (int i = 1; i <= Mathf.CeilToInt(3.4f / dt); i++)
                    {
                        float energy = plane.Energy;
                        plane.Tick(false, false, false); plane.FixedTick(dt);
                        Check(plane.Energy <= energy + .002f, "Release generated energy.");
                        if (plane.Velocity.y < 0 && firstDown < 0) firstDown = i*dt;
                        if (i == 1) Check(plane.Pitch < startPitch, "Release response was delayed.");
                    }
                    Check(firstDown >= 1f && firstDown <= 2.2f, "Release must follow the original gradual throw curve.");
                    Check(Mathf.Abs(plane.FlightPathPitch-tuning.divePitch)<.05f, "Glide did not settle on upgraded slope.");
                    for (int i = 0; i < Mathf.RoundToInt(.8f / dt); i++)
                    { plane.Tick(false, true, false); plane.FixedTick(dt); }
                    Check(plane.Velocity.y > 0, "Pressing again must resume climb.");
                    plane.StrikeObstacle(.1f);
                    for (int i = 0; i < Mathf.RoundToInt(2f / dt); i++)
                    { plane.Tick(false, false, false); plane.FixedTick(dt); }
                    Check(plane.Posture == FlightModel.Attitude.Normal && plane.Velocity.y < 0,
                        "Collision recovery must return to a descending glide.");
                    Debug.Log($"[GlideSlope] release dt={dt} launch={tuning.launchSpeed:F2} descent={firstDown:F2}s slope={tuning.divePitch:F2}");
                }
                // Same launch speed, drag, altitude and shape: isolate the value of glide upgrades.
                float lastDistance = 0;
                foreach (int count in new[] { 0, 3, 8 })
                {
                    var tuning = Research(glideIds.Take(count).ToArray()).Apply(new FlightTuning { launchHeight = 30 });
                    tuning.launchSpeed = 13; tuning.speedCap = 25; tuning.drag = .012f; tuning.climbDrag = .014f;
                    var plane = new FlightModel(tuning);
                    plane.Tick(true, false, false); plane.Tick(false, false, false);
                    for (int i = 0; i < 30000 && plane.State == FlightModel.Phase.Flying; i++) plane.FixedTick(dt);
                    Check(plane.State == FlightModel.Phase.Landed && plane.Distance > lastDistance,
                        "Glide upgrades must increase actual passive range and still land.");
                    lastDistance = plane.Distance;
                    Debug.Log($"[GlideSlope] passive dt={dt} nodes={count} distance={plane.Distance:F2}");
                }
            }
            VerifyGravityConversion();
            VerifyProgressiveDescent();
            Debug.Log("[GlideSlope] PASSED: throw-style response, shallow upgrades, passive range, energy, re-climb, collision, saved ownership.");
        }
        private static void VerifyGravityConversion()
        {
            foreach (float dt in new[] { .01f, .02f, .04f })
            {
                float[] measured = new float[2];
                for (int version=0; version<2; version++)
                {
                    var tuning = new FlightTuning { gravity=version==0 ? 2.6f : 2.86f, launchHeight=100,
                        launchSpeed=10, speedCap=100, drag=0, climbDrag=0, sink=0 };
                    var plane = new FlightModel(tuning);
                    plane.SetLaunchAngle(0); plane.Tick(true,false,false); plane.Tick(false,false,false);
                    for(int i=0;i<Mathf.RoundToInt(4f/dt);i++) plane.FixedTick(dt);
                    float initialHeight=plane.Height, initialSpeed=plane.Speed, initialEnergy=plane.Energy;
                    plane.FixedTick(dt);
                    measured[version]=(plane.Speed*plane.Speed-initialSpeed*initialSpeed)/(2f*(initialHeight-plane.Height));
                    Check(plane.Speed>initialSpeed && plane.Energy<=initialEnergy+.002f,
                        "Descent must convert height into speed without new energy.");
                    for(int i=0;i<Mathf.RoundToInt(1f/dt);i++) {plane.Tick(false,true,false);plane.FixedTick(dt);}
                    float climbingSpeed=plane.Speed;
                    plane.FixedTick(dt);
                    Check(plane.Climbing && plane.Speed<climbingSpeed,"Holding must slow the plane while climbing.");
                }
                Check(Mathf.Abs(measured[1]/measured[0]-1.1f)<.003f,"Gravity-driven descent gain must increase by 10 percent.");
                Debug.Log($"[GravityGain] dt={dt} old={measured[0]:F3} new={measured[1]:F3} ratio={measured[1]/measured[0]:F3}");
            }
        }
        private static void VerifyProgressiveDescent()
        {
            foreach (float dt in new[] { .01f, .02f, .04f })
            {
                var plane = new FlightModel(new FlightTuning { launchHeight = 1000, launchSpeed = 6, speedCap = 100 });
                plane.Tick(true,false,false); plane.Tick(false,false,false);
                float earlierAcceleration = float.NegativeInfinity, previousSpeed = plane.Speed;
                for (int i=1;i<=Mathf.RoundToInt(40f/dt);i++)
                {
                    float energy = plane.Energy;
                    plane.FixedTick(dt);
                    float acceleration = (plane.Speed-previousSpeed)/dt;
                    Check(plane.Energy<=energy+.001f,"Progressive descent generated energy.");
                    if(i==Mathf.RoundToInt(.5f/dt) || i==Mathf.RoundToInt(1f/dt)
                        || i==Mathf.RoundToInt(1.5f/dt) || i==Mathf.RoundToInt(2f/dt))
                    {
                        Check(acceleration>earlierAcceleration,"Steepening descent must progressively increase net acceleration.");
                        earlierAcceleration=acceleration;
                        Debug.Log($"[ProgressiveDescent] dt={dt} time={i*dt:F2} acceleration={acceleration:F3} speed={plane.Speed:F3}");
                    }
                    if(i==Mathf.RoundToInt(3f/dt)) Check(acceleration>0,"Developed descent should gain speed.");
                    if(i==Mathf.RoundToInt(40f/dt))
                    {
                        Check(Mathf.Abs(acceleration)<.01f && plane.Speed<20,
                            "Air resistance must limit speed well below the safety cap.");
                        Debug.Log($"[ProgressiveDescent] terminal dt={dt} speed={plane.Speed:F3} acceleration={acceleration:F4}");
                    }
                    previousSpeed=plane.Speed;
                }
                plane.Tick(false,false,true);
                Check(plane.Distance==0 && !plane.DiveBoostActive,"Reset retained descent acceleration state.");
            }
            Debug.Log("[ProgressiveDescent] PASSED: increasing early acceleration, quadratic drag equilibrium, energy and reset.");
        }
        private static void VerifyBoundary(Vector2 from, Vector2 to, float size, Vector2[] polygon)
        {
            Vector2 end = ResearchWebGeometry.Attachment(from,to,size,polygon);
            Vector2 local = (end-from)/ResearchWebGeometry.TextureSize(size);
            Vector2 delta = (to-from).normalized * .001f;
            Check(ResearchWebGeometry.Contains(polygon,local-delta), "Edge endpoint detached.");
            Check(!ResearchWebGeometry.Contains(polygon,local+delta), "Endpoint extends past contour.");
        }
        private static void VerifyBonuses()
        {
            var empty = new ExtendedResearchStats(Research());
            Check(empty.Reward(100,0,4,2,3,true) == 0,"Unowned bonus.");
            string[] resourceIds = { "Web_FlightLog", "Web_RouteMarks", "Web_PersonalRecord", "Web_RouteQuest",
                "Web_MailPickup", "Web_BundleDelivery", "Web_AirMail", "Web_PickupChain" };
            var bonus = new ExtendedResearchStats(Research(resourceIds));
            Check(bonus.Reward(100,90,4,2,3,false) == 47, "Research reward cap.");
            var record = new ExtendedResearchStats(Research("Web_PersonalRecord"));
            Check(record.Reward(100,90,0,0,0,false) == 14 && record.Reward(100,91,0,0,0,false) == 0, "Record threshold.");
            var mail = new ExtendedResearchStats(Research("Web_AirMail"));
            Check(mail.Reward(100,0,4,0,0,false)==0 && mail.Reward(100,0,4,3,0,false)==18,"High pickup limit.");
            var disk = new Store { Data = ResearchSave.NewGame() };
            var research = new FlightResearch(disk);
            Check(research.BeginRun(), "Begin settlement.");
            long id = research.ActiveRunId;
            disk.Fail = true;
            Check(!research.CompleteRun(id,100,out _,1000) && research.Points==0,"Failed bonus settlement.");
            disk.Fail = false;
            Check(research.CompleteRun(id,100,out int total,1000) && total==141
                && !research.CompleteRun(id,100,out _,1000), "Bonus double paid or cap bypassed.");
        }
        private static void VerifyAbilities()
        {
            var repair = Launch(5,"Web_RepairTape");
            repair.StrikeObstacle(40);
            repair.CollectBoost(); repair.CollectBoost();
            Check(repair.Durability==60,"Early repair.");
            repair.CollectBoost(); repair.CollectBoost();
            Check(repair.Durability==60 && repair.HighPickups==4,"Item repaired durability or lost pickup credit.");
            var low = Launch(2,"Web_LowDodge");
            low.StrikeObstacle(40); Check(low.Durability==100,"Low guard absent.");
            low.StrikeObstacle(40); Check(low.Durability==60,"Low guard repeated.");
            var high = Launch(5,"Web_LowDodge"); high.StrikeObstacle(40);
            Check(high.Durability==60,"Low guard at high altitude.");
            var barrier = Launch(5,"Web_EmergencyBarrier");
            barrier.StrikeObstacle(500); Check(barrier.Durability==100,"Critical guard absent.");
            barrier.StrikeObstacle(500); Check(barrier.State==FlightModel.Phase.Landed,"Critical guard repeated.");
            var kit = Launch(5,"Web_RepairKit");
            kit.StrikeObstacle(70); Check(kit.Durability==45,"Critical repair absent.");
            kit.StrikeObstacle(20); Check(kit.Durability==25,"Critical repair repeated.");
            var auto = Launch(5,"Web_EmergencyAlign");
            auto.StrikeObstacle(10); Check(auto.Posture==FlightModel.Attitude.Restoring,"Automatic restore absent.");
            for(int i=0;i<11;i++) auto.FixedTick(.02f);
            Check(auto.Posture==FlightModel.Attitude.Normal,"Automatic restore stuck.");
            auto.StrikeObstacle(10); Check(auto.Posture==FlightModel.Attitude.Disturbed,"Automatic restore repeated.");
            var plain = Launch(10); var express = Launch(10,"Web_ExpressRoute");
            plain.CollectBoost(); express.CollectBoost();
            Check(plain.Speed==express.Speed,"Express started early.");
            plain.CollectBoost(); express.CollectBoost();
            Check(Mathf.Approximately(express.ForwardBoost-plain.ForwardBoost,1),"Express not applied.");
            foreach(string id in new[]{"Web_WindBreak","Web_LowSprint","Web_CloudStep"})
            {
                float height=id=="Web_LowSprint"?2:10;
                var normal=Launch(height); var upgraded=Launch(height,id);
                normal.Tick(false,true,false); upgraded.Tick(false,true,false);
                normal.FixedTick(.02f); upgraded.FixedTick(.02f);
                Check(upgraded.Energy>normal.Energy, "Conditional drag has no effect " + id);
            }
        }
        private static void VerifyProgressionAndPickups(ResearchWebAssets web)
        {
            float previousTime = float.PositiveInfinity, previousPeak = 0;
            foreach (int count in new[] {0, 8, 22, 42, 68, 90, 98})
            {
                var research = Research(web.nodes.Take(count).Select(n => n.id).ToArray());
                var plane = new FlightModel(research.Apply(new FlightTuning()));
                var course = new FlightCourse();
                float launchSpeed = plane.Speed, peak = plane.Height, at20 = 0;
                plane.Tick(true,false,false);
                for (int i=0; i<40000 && plane.State==FlightModel.Phase.Flying; i++)
                {
                    plane.Tick(false,i>0,false);
                    var from = new Vector2(plane.Distance,plane.Height);
                    plane.FixedTick(.02f);
                    course.Tick(plane,from,.02f);
                    peak = Mathf.Max(peak,plane.Height);
                    if (at20==0 && plane.Distance>=20)
                        at20 = plane.Elapsed - .02f * (plane.Distance-20) / (plane.Distance-from.x);
                }
                Debug.Log($"[Growth] nodes={count} launch={launchSpeed:F1}m/s t20={at20:F2}s peak={peak:F1}m range={plane.Distance:F1}m");
                Check(plane.State==FlightModel.Phase.Landed && at20>0, "Course did not complete.");
                if (count != 98) Check(at20<previousTime && peak>previousPeak,"Growth milestone has no practical benefit.");
                if (count==90) Check(at20 < .4f && peak>80,"Late game must cross opening rapidly and reach the high sky.");
                previousTime=at20; previousPeak=peak;
            }
            foreach (float dt in new[]{.01f,.02f,.04f})
            {
                var boosted = Launch(15,"Web_BoostReserve","Web_ExpressRoute");
                var plain = Launch(15,"Web_BoostReserve","Web_ExpressRoute");
                int fed=0;
                for (int i=0;i<15000 && plain.State==FlightModel.Phase.Flying;i++)
                {
                    if (plain.ClimbReserve<.18f)
                    {
                        float e=boosted.Energy, reserve=boosted.ClimbReserve;
                        boosted.CollectBoost(); boosted.CollectBoost(); fed++;
                        Check(boosted.Energy==e && boosted.ClimbReserve==reserve && boosted.ForwardBoost<=3.21f,
                            "Pickup replenished reserves or stacked speed.");
                    }
                    plain.Tick(false,true,false); boosted.Tick(false,true,false);
                    plain.FixedTick(dt); boosted.FixedTick(dt);
                    Check(boosted.State==plain.State && boosted.Height==plain.Height && boosted.Energy==plain.Energy,
                        "Item changed altitude, energy, or time aloft.");
                }
                Check(fed>0 && plain.State==FlightModel.Phase.Landed && boosted.Distance>plain.Distance
                    && boosted.ForwardBoost==0, "Continuous pickups should change range only.");
            }
            var expiry = Launch(60);
            expiry.CollectBoost();
            for(int i=0;i<41;i++) expiry.FixedTick(.05f);
            Check(expiry.State==FlightModel.Phase.Flying && expiry.ForwardBoost==0,"Speed boost never expired.");
            var repaired = Launch(100,"Web_BodyRepair","Web_RepairTape");
            repaired.StrikeObstacle(30);
            for(int i=0;i<2000 && repaired.Distance<121 && repaired.State==FlightModel.Phase.Flying;i++)
                repaired.FixedTick(.02f);
            Check(repaired.Distance >= 120 && repaired.Durability==86,"Distance repair thresholds failed.");
            Check(web.nodes.All(n=>!string.IsNullOrWhiteSpace(n.benefit))
                && web.nodes.Select(n=>n.title).Distinct().Count()==98,"Missing player-facing node copy.");
            Debug.Log("[Growth] PASS: milestones, airborne time parity under repeated low-reserve pickups, expiry and distance repair.");
        }
        private static void VerifyFullFlight(ResearchWebAssets web)
        {
            var data = new ResearchSave { version=2, completedFlights=1, a1=3, g1=3,
                ownedNodeIds=web.nodes.Select(n=>n.id).ToArray() };
            foreach(float dt in new[]{.01f,.02f,.04f})
            {
                var model=new FlightModel(new FlightResearch(new Store{Data=data}).Apply(new FlightTuning()));
                model.Tick(true,false,false);
                for(int i=0;i<30000 && model.State==FlightModel.Phase.Flying;i++)
                {
                    model.Tick(false,i%150<60,false);
                    float energy=model.Energy;
                    model.FixedTick(dt);
                    Check(!float.IsNaN(model.Energy) && model.Energy<=energy+.0001f,"Full build creates energy.");
                }
                Check(model.State==FlightModel.Phase.Landed,"Full build never lands.");
            }
        }
    }
}
