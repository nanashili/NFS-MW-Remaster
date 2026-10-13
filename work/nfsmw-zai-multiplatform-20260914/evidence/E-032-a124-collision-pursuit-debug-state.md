# E-032: A124 collision-triggered pursuit debug state

The SLES `AIPerpVehicle::OnCausedCollision` instruction stream writes `2` to
the historical `_9DebugMenu$mCurPursuitState` global immediately after an
intentional collision causes an AI target to acquire the perpetrator. The
later source path omitted this A124-only instrumentation state.

Results:

- SLES `OnCausedCollision` improved from 84.906% to 85.076%.
- SLES zAI `.text` improved from 89.96335% to 89.96457%.
- The restored write is guarded by `EA_BUILD_A124`.

Artifact: `/private/tmp/sles-zai-layout-v46.json`.
