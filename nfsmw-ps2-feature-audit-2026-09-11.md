# PS2 and Xbox 360 implementation audit

Checked 11 September 2026 against commit [3bb4ab2](https://github.com/dbalatoni13/nfsmw/commit/3bb4ab24df54840a290310fe6d249bc13abe8235). Remote HEAD was rechecked and still resolves to this commit.

## Selected target: PS2 Black Edition USA (SLUS-21351)

PS2 is ahead of Xbox 360 by the published overall decompilation measure. Black Edition is marginally ahead of PS2 Alpha 124 overall and is the retail PS2 build represented here. The Xbox 360 target is an October 2005 prototype.

| Target | Overall reported decompilation |
|---|---:|
| [PS2 Black Edition / SLUS-21351](https://decomp.dev/dbalatoni13/nfsmw/SLUS-21351) | 14.33% |
| [PS2 Alpha 124 / SLES-53558-A124](https://decomp.dev/dbalatoni13/nfsmw/SLES-53558-A124) | 14.02% |
| [Xbox 360 prototype / EUROPEGERMILESTONE](https://decomp.dev/dbalatoni13/nfsmw/EUROPEGERMILESTONE) | 0.63% |

Overall progress does not imply that Black Edition leads every subsystem. Alpha 124 has higher code similarity for several subsystems. Individual function reconstruction, object code similarity, matching data, linkage and playable feature completion are different measures.

## Fully completed features

No complete player-facing feature can be certified from the checked evidence for either PS2 or Xbox 360. All configured main game units remain NonMatching. Every Matching/MatchingFor declaration found in configure.py is confined to GameCube or explicitly gated to GOWE69; the 128 GameCube matching objects in the earlier audit must not be attributed to PS2 or Xbox 360.

The Black Edition and Xbox 360 code treemaps contain no units with 100% code similarity. This does not mean they have no individually matching functions. Alpha 124 has one 100% unit: scheckpo, a small audio helper that clamps playback timemult to 0x800–0x2000. It remains unlinked and is not a completed gameplay feature.

## All configured game units: code similarity comparison

The figures below are each published report's fuzzy_match_percent, rounded to two decimal places. They measure compiled code similarity, not percent of a feature implemented or tested. A dash means the unit is absent from that target's code treemap.

| Unit | PS2 Black Edition | PS2 Alpha 124 | Xbox 360 prototype |
|---|---:|---:|---:|
| [zAI](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zAI.cpp) | 85.12% | 85.19% | 15.90% |
| [zAnim](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zAnim.cpp) | 84.24% | 89.63% | 5.80% |
| [zAttribSys](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zAttribSys.cpp) | 88.88% | 93.44% | 20.84% |
| [zBWare](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zBWare.cpp) | 69.04% | 73.81% | 15.33% |
| [zCamera](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zCamera.cpp) | 2.80% | 2.76% | 0.69% |
| [zComms](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zComms.cpp) | — | 0.00% | — |
| [zDebug](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zDebug.cpp) | — | 0.00% | 0.00% |
| [zDynamics](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zDynamics.cpp) | 0.00% | 0.00% | 0.00% |
| [zEagl4Anim](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zEagl4Anim.cpp) | 31.06% | 32.01% | — |
| [zEAXSound](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zEAXSound.cpp) | 65.78% | 73.85% | 8.54% |
| [zEAXSound2](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zEAXSound2.cpp) | 81.69% | 85.09% | 18.06% |
| [zEcstasy](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zEcstasy.cpp) | 19.19% | 19.88% | 5.63% |
| [zFe](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zFe.cpp) | 0.00% | 0.00% | 0.00% |
| [zFe2](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zFe2.cpp) | 0.00% | 0.00% | 0.00% |
| [zFEng](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zFEng.cpp) | 0.00% | 0.00% | 0.00% |
| [zFoundation](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zFoundation.cpp) | 6.66% | 6.71% | 3.17% |
| [zGameModes](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zGameModes.cpp) | 0.00% | 0.00% | — |
| [zGameplay](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zGameplay.cpp) | 0.00% | 0.00% | 0.00% |
| [zLua](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zLua.cpp) | 19.65% | 20.38% | 58.16% |
| [zMain](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zMain.cpp) | 10.51% | 10.87% | 24.95% |
| [zMisc](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zMisc.cpp) | 61.28% | 51.88% | 12.21% |
| [zMiscSmall](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zMiscSmall.cpp) | 7.19% | 94.05% | — |
| [zMission](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zMission.cpp) | 0.00% | 0.00% | — |
| [zPhysics](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zPhysics.cpp) | 2.14% | 2.12% | 0.24% |
| [zPhysicsBehaviors](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zPhysicsBehaviors.cpp) | 88.71% | 89.02% | 24.05% |
| [zPlatform](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zPlatform.cpp) | 0.00% | 0.00% | 0.00% |
| [zRender](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zRender.cpp) | — | 0.00% | 0.00% |
| [zSim](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zSim.cpp) | 65.77% | 62.49% | 16.07% |
| [zSpeech](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zSpeech.cpp) | 0.00% | 0.00% | — |
| [zTrack](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zTrack.cpp) | 77.47% | 82.15% | 4.01% |
| [zWorld](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zWorld.cpp) | 74.40% | 63.14% | 13.07% |
| [zWorld2](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zWorld2.cpp) | 76.85% | 78.26% | 10.95% |
| [zOnline](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zOnline.cpp) | — | 0.00% | 0.00% |
| [zFeOverlay](https://github.com/dbalatoni13/nfsmw/blob/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/SourceLists/zFeOverlay.cpp) | 0.00% | 0.00% | — |

## What the substantive source covers

- [Vehicle behaviours](https://github.com/dbalatoni13/nfsmw/tree/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/Src/Physics/Behaviors): engine, clutch, transmission, shifting, induction, nitrous, suspension, vehicle damage, reset, effects and related behaviours. Black Edition zPhysicsBehaviors is 88.71%; core zPhysics is only 2.14%. This is not a complete standalone driving implementation.
- [AI](https://github.com/dbalatoni13/nfsmw/tree/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/Src/AI): racing, traffic, police management, pursuit, roadblocks, helicopters, steering and recovery actions. Black Edition zAI is 85.12%; this does not certify a complete playable pursuit or race.
- Attribute classes/collections/database and vault loading are reconstructed, but the Black Edition attribute unit is 88.88% similar and non-matching.
- Animation, world, track streaming and game audio have substantial source; their full unit scores are above. They remain dependent on unfinished game/runtime systems.

## Clear gaps

- [Gameplay](https://github.com/dbalatoni13/nfsmw/tree/3bb4ab24df54840a290310fe6d249bc13abe8235/src/Speed/Indep/Src/Gameplay): all 17 implementation .cpp files are empty, including race status/database, milestones, infractions, speed traps, activities, timers, triggers and managers.
- Both Gamemodes .cpp files are empty. zGameplay, zGameModes and zMission include no implementation .cpp files.
- Black Edition frontend/menu units zFe, zFe2, zFEng and zFeOverlay report 0%. Speech, dynamics and platform units also report 0%.
- Camera code is 2.80% similar and core physics 2.14% for Black Edition.
- The repository requires original game files. Reconstructed code and a passing build would not by themselves demonstrate that the entire game is independently implemented.

## Verification scope

Compared all configured game units against the published reports for the same commit; checked explicit build-completion declarations and relevant source paths. No compiler, emulator, binary-comparison or gameplay test was run locally. No complete feature is claimed solely from a source filename, a TODO scan or a code-similarity percentage.
