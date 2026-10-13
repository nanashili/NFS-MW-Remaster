# Recovered Most Wanted tire source

`MostWantedTire` and `MostWantedDifferential` are translated from `dbalatoni13/nfsmw`
at commit `3bb4ab24df54840a290310fe6d249bc13abe8235`, specifically
`src/Speed/Indep/Src/Physics/Behaviors/SuspensionRacer.cpp` lines 555-824 and
1697-1717. The upstream repository includes a CC0 1.0 license, copied beside this
note as `nfsmw-LICENSE`.

The source build marks the physics units `NonMatching`; this is recovered source,
not a verified retail-binary match. Its `TARGET_WIN32` lateral-force branches are
comments with no return because the original PC multiplier is DRM protected. The
managed implementation intentionally follows the available console equation and
documents the questionable `GripFactor = 2.5` and `MaxLoad = 0` source constants.
