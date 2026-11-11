using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FlyEditor.Flight
{
    public enum ResearchNode { Throw, Glide, Control }

    [Serializable]
    public sealed class ResearchSave
    {
        public int version = 1;
        public int points;
        public int completedFlights;
        public int a1, g1, c1;
        public string[] ownedNodeIds = Array.Empty<string>();
        public FoldStep[] paperFolds = Array.Empty<FoldStep>();
        public long nextRunId, activeRunId, lastPaidRunId;
        public float bestDistance;
        public static ResearchSave NewGame() => new ResearchSave { version = 2, ownedNodeIds = Array.Empty<string>() };
        public ResearchSave Copy()
        {
            var copy = (ResearchSave)MemberwiseClone();
            copy.ownedNodeIds = ownedNodeIds == null ? Array.Empty<string>() : (string[])ownedNodeIds.Clone();
            copy.paperFolds = paperFolds == null ? Array.Empty<FoldStep>() : (FoldStep[])paperFolds.Clone();
            return copy;
        }
    }

    public interface IResearchStore
    {
        ResearchSave Load();
        void Save(ResearchSave data);
    }

    public sealed class ResearchFileStore : IResearchStore
    {
        public string Path { get; }
        public ResearchFileStore(string path) { Path = path; }
        public static ResearchFileStore OpenCurrent()
        {
#if UNITY_EDITOR
            string testPath = Environment.GetEnvironmentVariable("FLYEDITOR_TEST_PROGRESS");
            if (!string.IsNullOrEmpty(testPath)) return new ResearchFileStore(testPath);
#endif
            return new ResearchFileStore(System.IO.Path.Combine(Application.persistentDataPath, "starter-research-v1.json"));
        }
        public ResearchSave Load()
        {
            if (!File.Exists(Path)) return ResearchSave.NewGame();
            var data = JsonUtility.FromJson<ResearchSave>(File.ReadAllText(Path));
            if (data == null || (data.version != 1 && data.version != 2) || data.points < 0 || data.completedFlights < 0
                || data.a1 < 0 || data.a1 > 3 || data.g1 < 0 || data.g1 > 3 || data.c1 < 0 || data.c1 > 3
                || data.nextRunId < 0 || data.activeRunId < 0 || data.activeRunId > data.nextRunId
                || data.lastPaidRunId < 0 || data.lastPaidRunId > data.nextRunId
                || float.IsNaN(data.bestDistance) || float.IsInfinity(data.bestDistance) || data.bestDistance < 0f)
                throw new InvalidDataException("연구 저장 파일이 올바르지 않습니다. 기존 파일을 보존했습니다.");
            if (data.version == 2)
            {
                if (data.ownedNodeIds == null) throw new InvalidDataException("구매 노드 목록이 없습니다.");
                var ids = new HashSet<string>();
                foreach (string id in data.ownedNodeIds)
                    if (string.IsNullOrEmpty(id) || !ids.Add(id)) throw new InvalidDataException("구매 노드 기록이 올바르지 않습니다.");
            }
            return data;
        }
        public void Save(ResearchSave data)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            string temporary = Path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
            if (File.Exists(Path)) File.Replace(temporary, Path, Path + ".bak");
            else File.Move(temporary, Path);
        }
    }

    // Progress changes are committed as one snapshot. A failed write never spends points in memory.
    public sealed class FlightResearch
    {
        private readonly IResearchStore store;
        private ResearchSave data;
        public int Points => data.points;
        public int CompletedFlights => data.completedFlights;
        public bool Unlocked => data.completedFlights > 0;
        public long ActiveRunId => data.activeRunId;
        public float BestDistance => data.bestDistance;
        public FoldStep[] PaperDesign => (FoldStep[])data.paperFolds.Clone();
        public PaperFlightProfile EquippedProfile { get; private set; }
        internal bool SavePaperDesign(PaperSheet sheet)
        {
            var profile=PaperFlightProfile.Measure(sheet);
            var next=data.Copy();next.paperFolds=sheet.Recipe();
            if(!Commit(next))return false;
            EquippedProfile=profile;return true;
        }
        public string LastError { get; private set; } = "";
        private readonly HashSet<string> owned;
        private static readonly int[] GrowthThresholds = { 8, 22, 42, 68, 90 };
        private static readonly float[] GrowthSpeeds = { 1f, 1.45f, 2.45f, 3.8f, 5.2f, 6.5f };
        public int OwnedCount => owned.Count;
        public int GrowthTier
        {
            get
            {
                int tier = 0;
                while (tier < GrowthThresholds.Length && OwnedCount >= GrowthThresholds[tier]) tier++;
                return tier;
            }
        }
        public int NextGrowthCount => GrowthTier < GrowthThresholds.Length ? GrowthThresholds[GrowthTier] : 0;
        public float NextGrowthSpeed => GrowthSpeeds[Mathf.Min(GrowthTier + 1, GrowthSpeeds.Length - 1)];
        private static readonly string[] LaunchIds = { "Launch_1", "Launch_2", "Launch_3" };
        private static readonly string[] GlideIds = { "Glide_1", "Glide_2", "Glide_3" };
        public FlightResearch(IResearchStore storage)
        {
            store = storage;
            data = store.Load().Copy();
            if (data.version == 1)
            {
                var previous = new List<string>();
                for (int i = 0; i < data.a1; i++) previous.Add(LaunchIds[i]);
                for (int i = 0; i < data.g1; i++) previous.Add(GlideIds[i]);
                data.ownedNodeIds = previous.ToArray();
                data.version = 2;
            }
            EquippedProfile=PaperFlightProfile.Measure(PaperSheet.Replay(data.paperFolds));
            owned = new HashSet<string>(data.ownedNodeIds);
        }
        public bool Owns(string id) => owned.Contains(id);
        public int Rank(ResearchNode node)
        {
            switch (node) { case ResearchNode.Throw: return data.a1; case ResearchNode.Glide: return data.g1;
                case ResearchNode.Control: return data.c1; default: return -1; }
        }
        public int Cost(ResearchNode node)
        {
            int slot = NextSlot(node);
            switch (slot) { case 0: return 30; case 1: return 120; case 2: return 480; default: return 0; }
        }
        private int NextSlot(ResearchNode node)
        {
            if (node == ResearchNode.Control) return data.c1;
            if (node != ResearchNode.Throw && node != ResearchNode.Glide) return -1;
            var ids = node == ResearchNode.Throw ? LaunchIds : GlideIds;
            for (int i = 0; i < ids.Length; i++) if (!Owns(ids[i])) return i;
            return -1;
        }
        public bool CanBuy(ResearchNode node) => Unlocked && ActiveRunId == 0 && Rank(node) >= 0
            && Rank(node) < 3 && Points >= Cost(node);

        private bool Commit(ResearchSave candidate)
        {
            try { store.Save(candidate); data = candidate; LastError = ""; return true; }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { LastError = "저장하지 못했습니다. 진행은 변경되지 않았습니다. 다시 시도해 주세요."; return false; }
        }
#if UNITY_EDITOR
        public bool AdjustDeveloperPoints(int delta)
        {
            int balance = (int)Math.Min(int.MaxValue, Math.Max(0L, (long)data.points + delta));
            if (balance == data.points) return true;
            var next = data.Copy();
            next.points = balance;
            return Commit(next);
        }
        public bool ResetDeveloperProgress()
        {
            if (!Commit(ResearchSave.NewGame())) return false;
            owned.Clear();EquippedProfile=PaperFlightProfile.Standard;
            return true;
        }
#endif
        public bool TryBuy(ResearchNode node)
        {
            if (!CanBuy(node)) return false;
            if (node != ResearchNode.Control)
            {
                var ids = node == ResearchNode.Throw ? LaunchIds : GlideIds;
                return BuyId(ids[NextSlot(node)], (int)node, Cost(node));
            }
            var next = data.Copy();
            next.points -= Cost(node);
            next.c1++;
            return Commit(next);
        }
        public bool TryBuy(ResearchWebAssets web, int index)
        {
            if (web == null || !web.CanBuy(index, this)) return false;
            var node = web.nodes[index];
            return BuyId(node.id, node.runtimeIndex, node.costs[0]);
        }
        private bool BuyId(string id, int effect, int price)
        {
            if (Owns(id)) return false;
            var next = data.Copy();
            Array.Resize(ref next.ownedNodeIds, next.ownedNodeIds.Length + 1);
            next.ownedNodeIds[next.ownedNodeIds.Length - 1] = id;
            next.points -= price;
            if (effect == 0) next.a1++;
            if (effect == 1) next.g1++;
            if (!Commit(next)) return false;
            owned.Add(id);
            return true;
        }
        public bool BeginRun()
        {
            if (ActiveRunId != 0 || data.nextRunId == long.MaxValue) return false;
            var next = data.Copy(); next.activeRunId = ++next.nextRunId;
            return Commit(next);
        }
        public bool AbandonRun()
        {
            if (ActiveRunId == 0) return true;
            var next = data.Copy(); next.activeRunId = 0;
            return Commit(next);
        }
        public bool CompleteRun(long runId, float maxForwardDistance, out int reward, int bonus = 0)
        {
            reward = 0;
            if (runId <= 0 || runId != ActiveRunId || runId <= data.lastPaidRunId
                || float.IsNaN(maxForwardDistance) || float.IsInfinity(maxForwardDistance) || maxForwardDistance < 0f)
                return false;
            double baseReward = ResearchEconomy.BaseReward(maxForwardDistance);
            int earned = (int)Math.Min(int.MaxValue, baseReward + Math.Min(Math.Max(0, bonus), Math.Floor(baseReward * .5)));
            var next = data.Copy();
            next.points = (int)Math.Min(int.MaxValue, (long)next.points + earned);
            next.completedFlights = (int)Math.Min(int.MaxValue, (long)next.completedFlights + 1);
            next.bestDistance = Mathf.Max(next.bestDistance, maxForwardDistance);
            next.lastPaidRunId = runId; next.activeRunId = 0;
            if (!Commit(next)) return false;
            reward = earned; return true;
        }
        public FlightTuning Apply(FlightTuning baseline)
        {
            var result = baseline.Copy();
            result.effects = new FlightEffects(this);
            result.launchSpeed *= 1f + Bonus("Launch_1", .12f) + Bonus("Launch_2", .20f) + Bonus("Launch_3", .30f);
            float glideScale = 1f - Bonus("Glide_1", .02f) - Bonus("Glide_2", .05f) - Bonus("Glide_3", .09f);
            result.drag *= 1f - Bonus("Drag_1", .06f) - Bonus("Drag_2", .12f) - Bonus("Drag_3", .18f);
            result.climbDrag *= 1f - Bonus("Climb_1", .10f) - Bonus("Climb_2", .18f) - Bonus("Climb_3", .25f);
            result.pitchResponse *= 1f + 0.05f * data.c1;
            result.flightPathResponse *= 1f + 0.05f * data.c1;
            var extra = result.effects.extra;
            result.launchSpeed *= 1f + extra.launch;
            result.drag *= Mathf.Max(.5f, 1f - extra.drag);
            // Scale metres lost per metre travelled, not the control response time.
            glideScale *= Mathf.Max(.5f, 1f - extra.glide);
            result.sink *= glideScale;
            result.divePitch = Mathf.Atan(Mathf.Tan(result.divePitch * Mathf.Deg2Rad) * glideScale) * Mathf.Rad2Deg;
            result.climbDrag *= Mathf.Max(.5f, 1f - extra.climb);
            result.pitchResponse *= 1f + extra.response;
            result.flightPathResponse *= 1f + extra.path;
            int tier = GrowthTier;
            float speedScale = GrowthSpeeds[tier];
            result.launchSpeed *= speedScale;
            result.speedCap = Mathf.Max(result.speedCap * speedScale, result.launchSpeed);
            // Higher launch speeds lose energy faster to cubic drag. Reduce drag
            // with each milestone so the additional launch energy carries into a climb.
            float dragScale = speedScale * Mathf.Sqrt(speedScale);
            result.drag /= dragScale;
            result.climbDrag /= dragScale;
            result.climbPitch += 2f * tier;
            EquippedProfile.Apply(result);
            result.Sanitize();
            return result;
        }
        private float Bonus(string id, float amount) => Owns(id) ? amount : 0f;
        internal int CountOwned(string first, string second, string third) =>
            (Owns(first) ? 1 : 0) + (Owns(second) ? 1 : 0) + (Owns(third) ? 1 : 0);
    }

    // One official InGame flight: Ready -> Flying -> Landed/settled -> Ready.
    // Aborting or quitting a flight never settles its pending reward.
    public sealed class FlightSession
    {
        private readonly FlightTuning baseline;
        public FlightResearch Research { get; }
        public FlightModel Model { get; private set; }
        public FlightCourse Course { get; private set; }
        public int LastReward { get; private set; }
        public bool SettlementPending => Model.State == FlightModel.Phase.Landed && Research.ActiveRunId != 0;
        public float InitialEnergy { get; private set; }
        private float maxForwardDistance;
        public FlightSession(FlightTuning settings, FlightResearch research)
        {
            baseline = settings.Copy(); Research = research;
            if (!research.AbandonRun()) throw new IOException(research.LastError);
            Prepare();
        }
        private void Prepare()
        {
            Model = new FlightModel(Research.Apply(baseline), Research.BestDistance);
            Course = new FlightCourse();
            InitialEnergy = Model.Energy;
            maxForwardDistance = 0f;
        }
#if UNITY_EDITOR
        public bool ResetDeveloperSession()
        {
            if (!Research.ResetDeveloperProgress()) return false;
            LastReward = 0;
            Prepare();
            return true;
        }
#endif
        public bool TryEquipPaper(PaperSheet sheet)
        {
            if(Model.State!=FlightModel.Phase.Ready || Research.ActiveRunId!=0 || !Research.SavePaperDesign(sheet))return false;
            Prepare();return true;
        }
        public bool TryBuy(ResearchNode node)
        {
            if (Model.State == FlightModel.Phase.Flying || SettlementPending || !Research.TryBuy(node)) return false;
            if (Model.State == FlightModel.Phase.Ready) Prepare();
            return true;
        }
        public bool TryBuy(ResearchWebAssets web, int index)
        {
            if (Model.State == FlightModel.Phase.Flying || SettlementPending || !Research.TryBuy(web, index)) return false;
            if (Model.State == FlightModel.Phase.Ready) Prepare();
            return true;
        }
        public bool TryLaunch(float angle,float releaseHeight)
        {
            if (Model.State != FlightModel.Phase.Ready || !Research.BeginRun()) return false;
            Model.SetLaunchPose(angle,releaseHeight);
            InitialEnergy = Model.Energy;
            LastReward = 0;
            Model.Tick(true,false,false);
            return true;
        }
        public void Tick(bool pressed, bool held, bool restart)
        {
            if (SettlementPending) return;
            if (restart || (Model.State == FlightModel.Phase.Landed && pressed))
            {
                if (Research.AbandonRun()) { Prepare(); LastReward = 0; }
                return;
            }
            if (Model.State == FlightModel.Phase.Ready && pressed)
            {
                TryLaunch(Model.Pitch,Model.Height);
                return;
            }
            Model.Tick(pressed, held, false);
        }
        public void FixedTick(float dt)
        {
            var previous = Model.State;
            Course.Step(Model, dt);
            maxForwardDistance = Mathf.Max(maxForwardDistance, Model.Distance);
            if (previous == FlightModel.Phase.Flying && Model.State == FlightModel.Phase.Landed) TrySettle();
        }
        public bool TrySettle()
        {
            if (!SettlementPending) return false;
            int bonus = Model.Effects.extra.Reward(maxForwardDistance, Research.BestDistance,
                Model.PickupsCollected, Model.HighPickups, Course.LongestChain, Model.Durability <= 0);
            if (!Research.CompleteRun(Research.ActiveRunId, maxForwardDistance, out int reward, bonus)) return false;
            LastReward = reward; return true;
        }
    }
}
