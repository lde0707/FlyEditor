# Coverage and execution

## Scope

Techniques, complete models and input freedom are separate dimensions. Start with atomic tests, then chained operations, then verified model sequences. Hard models expose combinations; they do not replace atomic testing. Keep wet-folding/material changes and curved deformation visible as unverified scope rather than treating a polygon rotation engine as already supporting them.

The catalog's variation axes define a test space, not generated executable cases. Use pairwise samples first, then targeted boundary combinations and seeded random sequences. Record random seeds and shrink failures to the smallest recipe. Property-preserving random sequences do not prove a desired model can be folded.

## Case requirements

Each durable case records: technique IDs, source recipe and hash, sheet shape and thickness, camera, visible/contact face and material coordinates, control events, expected moving/fixed material regions, angle path, collision expectations, target landmarks, replay/undo checks, evidence and reproduction command. An expected failure must name the physical reason. A technically valid operation rejected by the UI is a failure, not an expected limitation.

Capture every completed model step from the actual Unity renderer. Store actual input events alongside recipes. A screenshot alone cannot establish collision correctness; a geometry check alone cannot establish usability. Complete-model PASS requires every step reachable through current player controls and a verified final shape, all from one eligible sheet. Do not prebuild the final mesh or replace a blocked step with a diagram-specific shortcut.

## Results

Use PASS / FAIL / UNVERIFIED / BLOCKED_ENVIRONMENT. Track case evidence separately from aggregate suite success. Keep the original failure immutable and save newly minimized regressions under a new ID. Rerun after an engine change; historical evidence is not a current pass.

The starter runner executes a baseline only. It does not yet generate all variation combinations, minimize failures, replay arbitrary input traces or certify full advanced models. These gaps are deliberate visible backlog, not implicit passes.
