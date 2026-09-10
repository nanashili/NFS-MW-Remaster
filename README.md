# NFS MW Remaster

A work-in-progress Need for Speed: Most Wanted remaster project built with Unity and the High Definition Render Pipeline (HDRP).

## Why Unity?

I am developing this project on a base Mac mini M4 because I do not currently have a gaming PC. Unity lets me work on the project with the hardware I have available.

The long-term goal is to move the project to Unreal Engine once I have the funds to build a gaming PC.

## Getting started

1. Install Git LFS and Unity **6000.6.0f1**, the version recorded in `ProjectSettings/ProjectVersion.txt`.
2. Clone the repository and run `git lfs install` followed by `git lfs pull` to download the binary assets.
3. Add the repository folder in Unity Hub and open it with the matching editor version. Allow Unity to restore packages and import assets.
4. Open `Assets/NfsMw/Content/Frontend/UI/Scenes/Boot.unity` and enter Play mode.

The project is under active development. Features, artwork, and tooling are still evolving.

## Project layout

- `Assets/NfsMw/` — runtime code, editor tools, tests, scenes, and game assets.
- `Art/` — source artwork, models, and supporting authoring data.
- `Packages/` and `ProjectSettings/` — shared Unity dependencies and project configuration.
- `Tools/` — asset processing, diagnostics, and validation tools. See [the content layout](Tools/PROJECT_LAYOUT.md) for specific locations.

Commit Unity assets together with their `.meta` files so references remain stable. Large binary assets use Git LFS. Generated Unity caches, builds, local settings, and validation output are excluded from version control; reference fixtures required by tests are retained.

## Support development

This project is free and open source. If you find the project useful and would like to support continued development, testing, tooling and infrastructure, voluntary contributions are welcome.
Contributions do not unlock builds, features, game content, or other benefits.
