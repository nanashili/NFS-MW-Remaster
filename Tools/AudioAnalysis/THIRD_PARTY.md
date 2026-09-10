# Format references

`MostWantedAudioDatabase.cs` reads the PC VPAK layout using the class, collection,
field, array, pointer and hashing definitions documented by
[NFSTools/VaultLib](https://github.com/NFSTools/VaultLib/tree/c1c9e94044ef0eae9ebc43c5a2b3a70ef5369d86).
Revision: `c1c9e94044ef0eae9ebc43c5a2b3a70ef5369d86`.
The compact reader and Jenkins lookup2 implementation retain the MIT attribution
in [VaultLib-LICENSE.txt](Notices/VaultLib-LICENSE.txt). No VaultLib runtime or
Windows executable is required by Unity.

Audio event and GIN lookup research also uses the
[Most Wanted reconstruction](https://github.com/dbalatoni13/nfsmw/tree/13189413c4c6e447c2225052c66b55d76863b985),
especially `Ginsu/ginsudata.cpp`, `CARSFX_Shifting.cpp`, `CARSFX_Engine.cpp`,
`NFSUG_CarsSFXLoadData.cpp` and the generated CSIS interface declarations.
Those reconstructed GameCube routines provide cross-platform comparison evidence;
they do not by themselves prove PC AEMS compiled-controller or interactive parity.
The observed PC AEMS bank templates are decoded into portable data-flow operations
using the same revision's `snd/9/source/library/cmn/saems.c`, `saemsmbf.c`,
`snd/9/extern/aemsdef.h`, and CSIS interface layouts as references. Original x86
instructions are never executed. The installed PC banks and database provide the
actual sample references and parameters; Unity supplies playback and telemetry.
No game binary or reconstructed game source is distributed here.
