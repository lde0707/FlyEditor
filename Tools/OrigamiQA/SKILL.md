---
name: flyeditor-origami-qa
description: Test and diagnose FlyEditor paper-folding engine and player interactions across folding techniques, layered geometry, and single-sheet model sequences; preserve failing states and run regressions after common-engine fixes.
---

# FlyEditor origami QA

Use this skill for paper-folding engine regressions and usability failures. Read the repository AGENTS.md. Locate the Unity project from ProjectVersion.txt; never assume a historical absolute path.

Use [catalog.json](references/catalog.json) for technique coverage and [workflow.md](references/workflow.md) for execution and evidence rules. Catalog entries are test requirements, not proof of support. Existing test names indicate partial coverage only. Read only relevant test implementations before selecting a suite.

Run `scripts/run_suite.py --catalog` to show gaps. Run `scripts/run_suite.py --project <isolated Unity project> --unity <matching Unity executable> --suite core --out <output folder>` for the installed baseline. Copy assets/OrigamiTechniqueBaseline.cs into the isolated test project Assets/_Project/Scripts/Editor. The target must include the existing test dependencies named by that entrypoint. A copied project's Library/Temp is never a substitute for compiling it.

For a reported failure, capture the original recipe and real player input before changing the engine. Reproduce using generic engine operations; add a durable case with expected material motion and stationary regions. Fix common geometry, topology or interaction behavior, then run the local regression and affected technique families. Do not add diagram/stage/index-specific runtime branches or bypass collision checks.

An engine recipe passing is not a player-input pass or a completed model. Count and report these separately. An unimplemented/missing test is UNVERIFIED, never PASS. Report the first blocked step, expected and observed motion, source/target ply ownership and evidence paths. Stop exhaustive claims: the continuous state space cannot be completely enumerated.

The user's game progress must not be overwritten. This skill does not authorize restoring historical paper designs without a current user request. Use isolated test progress paths. Do not reload scripts in the user's live scene while relying on unsaved paper state. Save and validate a copy first; if unavailable, report the reproduction gap.

Research model-specific diagrams from the author or an authorized source. Verify single-sheet/no-cut/no-glue requirements per model before treating it as an eligible complete-model test. Preserve provenance; link diagrams instead of redistributing them.
