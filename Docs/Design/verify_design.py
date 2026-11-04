"""Checks the proposed design data, not the Unity runtime or real aerodynamics."""
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parent


UPGRADE_DATA = json.loads((ROOT/"upgrade-nodes.json").read_text(encoding="utf-8"))
MODEL = json.loads((ROOT/"flight-model.json").read_text(encoding="utf-8"))
NODES = UPGRADE_DATA["nodes"]
PAPERS = MODEL["materials"]
EXAMPLES = MODEL["syntheticExamples"]


def stats(shape, paper="standard", ranks=None):
    p = PAPERS[paper]
    ranks = ranks or {}
    level = lambda key: ranks.get(key, 0)
    a, r, t, s, b, d, o = [shape[k] for k in ("A", "R", "T", "S", "B", "D", "O")]
    asym = 1-s
    return dict(
        launch=10*(1+.12*t-.08*a)*p["launch"]*(1+.03*level("C1")),
        drag=.006*(1+.35*a+.25*asym-.15*t+.20*o*(1-.30*level("X4")))*p["drag"]*(1-.03*level("M4")),
        sink=.25*(1-.35*a-.15*r+.15*asym)*p["sink"]*(1-.04*level("G1")),
        response=(.25+.20*a+.15*asym+.10*(1-b))*p["response"]/(1+.05*level("C2")),
        sway=max(0., min(5., 4*asym+2*(1-b)-1.5*d)),
        durability=100*p["durability"]*(1+.05*level("M1")),
        wind=p["wind"]*(1+.05*level("G3")+.10*a*level("X3")),
        climbDragMultiplier=1-.04*level("C3"),
        recoverySpeed=3.2,
        lowSpeedSinkMultiplier=1-.20*level("G4"),
    )


def flight(shape, policy, step=.01):
    """Wind-free offline reference, no collision/skills. g=3 is a game coefficient."""
    st = stats(shape)
    v, height, x, time, theta = st["launch"], MODEL["launchHeight"], 0., 0., 0.
    max_residual, max_energy_step = -float("inf"), -float("inf")
    recovery, low_speed_time = False, 0.
    g = MODEL["gravity"]
    for _ in range(int(120/step)):
        held = policy(time)
        # Smooth target transition; shape sway is also a pitch disturbance in the model.
        target = math.radians((MODEL["pitchHeld"] if held else MODEL["pitchReleased"]) + st["sway"]*math.sin(2*math.pi*.6*time))
        low_speed_time = low_speed_time+step if v < st["recoverySpeed"] else 0.
        if low_speed_time >= MODEL["recoveryDelay"]:
            recovery = True
        if recovery and v >= st["recoverySpeed"]+MODEL["recoveryExitMargin"]:
            recovery = False
        if recovery:
            target = math.radians(MODEL["recoveryPitch"])
        theta += (target-theta)*(1-math.exp(-step/st["response"]))
        drag = st["drag"]*(st["climbDragMultiplier"] if theta > 0 else 1)
        dv = -g*math.sin(theta)-drag*v*v
        sink = st["sink"] + .8*st["lowSpeedSinkMultiplier"]*max(0., min(1., (5-v)/3.2))**2
        dh = v*math.sin(theta)-sink
        residual = v*dv+g*dh
        max_residual = max(max_residual, residual)
        old_energy = .5*v*v+g*height
        x += v*math.cos(theta)*step
        height += dh*step
        v = max(0., min(MODEL["speedCap"], v+dv*step))
        new_energy = .5*v*v+g*height
        max_energy_step = max(max_energy_step, new_energy-old_energy)
        time += step
        if height <= 0:
            return dict(seconds=round(time, 2), meters=round(x, 2),
                        maxEnergyDerivative=max_residual, maxEnergyStep=max_energy_step)
    raise AssertionError("Calm-air flight did not terminate within 120 seconds")


def validate():
    by_id = {n["id"]: n for n in NODES}
    assert len(by_id) == len(NODES) == 20
    visited, visiting = set(), set()

    def visit(key):
        assert key not in visiting, "Upgrade graph contains a cycle"
        if key in visited:
            return
        visiting.add(key)
        n = by_id[key]
        assert n["rankCosts"] and all(isinstance(c, int) and c > 0 for c in n["rankCosts"])
        for prerequisite in n["prerequisites"]:
            parent, rank = prerequisite["id"], prerequisite["minRank"]
            assert parent in by_id and 1 <= rank <= len(by_id[parent]["rankCosts"])
            if n["phase"] == "prototype":
                assert by_id[parent]["phase"] == "prototype", "Prototype dependency outside scope"
            visit(parent)
        visiting.remove(key)
        visited.add(key)

    for key in by_id:
        visit(key)
    # Test parameter boundary combinations: positive coefficients and bounded upgrades.
    caps = {n["id"]: len(n["rankCosts"]) for n in NODES}
    count = 0
    for bits in range(128):
        shape = {k: (bits >> i) & 1 for i, k in enumerate(("A", "R", "T", "S", "B", "D", "O"))}
        for paper in PAPERS:
            for ranks in ({}, caps):
                st = stats(shape, paper, ranks)
                assert all(math.isfinite(v) and v > 0 for k, v in st.items() if k != "sway")
                assert 0 <= st["sway"] <= 5
                count += 1
    reports = {}
    policies = {"손을 놓고 활공": lambda t: False, "2초 상승 후 4초 활공 반복": lambda t: t % 6 < 2,
                "계속 누르기": lambda t: True}
    for name, shape in EXAMPLES.items():
        reports[name] = dict(metrics=stats(shape), runs={})
        for policy_name, policy in policies.items():
            run = flight(shape, policy)
            assert run["maxEnergyDerivative"] <= 1e-9
            assert run["maxEnergyStep"] <= 1e-7
            reports[name]["runs"][policy_name] = run
        assert reports[name]["runs"]["2초 상승 후 4초 활공 반복"]["meters"] > reports[name]["runs"]["계속 누르기"]["meters"]
    assert reports["넓은 활공형"]["runs"]["손을 놓고 활공"]["meters"] > reports["좁은 속도형"]["runs"]["손을 놓고 활공"]["meters"]
    assert reports["좁은 속도형"]["runs"]["2초 상승 후 4초 활공 반복"]["meters"] > reports["넓은 활공형"]["runs"]["2초 상승 후 4초 활공 반복"]["meters"]
    # Time-step sensitivity check for the baseline periodic control policy.
    coarse = flight(EXAMPLES["안정 균형형"], policies["2초 상승 후 4초 활공 반복"], .02)
    fine = flight(EXAMPLES["안정 균형형"], policies["2초 상승 후 4초 활공 반복"], .005)
    assert abs(coarse["meters"]-fine["meters"])/fine["meters"] < .02
    return dict(status="passed", upgradeNodeCount=len(NODES),
                prototypeNodeCount=sum(n["phase"] == "prototype" for n in NODES),
                totalResearchCost=sum(sum(n["rankCosts"]) for n in NODES),
                coefficientBoundaryCases=count, calmAirRuns=12,
                timestepDistanceDifferencePercent=round(abs(coarse["meters"]-fine["meters"])/fine["meters"]*100, 4),
                limitations="Synthetic scalar input only; no Unity runtime, folding mesh, collisions, wind, or human playtest verified.",
                examples=reports)


if __name__ == "__main__":
    result = validate()
    (ROOT/"verification-results.json").write_text(json.dumps(result, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({k:v for k,v in result.items() if k != "examples"}, ensure_ascii=False))
    for name, data in result["examples"].items():
        print(name, {k:(v["meters"], v["seconds"]) for k,v in data["runs"].items()})
