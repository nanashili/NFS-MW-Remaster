# NFS MW Remaster

An open-source, work-in-progress remaster of **Need for Speed: Most Wanted (2005)**, built from the ground up in Unity using the **High Definition Render Pipeline (HDRP)**.

The project aims to recreate and modernize the experience of the original game while exploring its systems through reverse engineering, research, and independent implementation. Development currently focuses on areas such as vehicle handling, racing, police pursuits, traffic, environments, weather, audio, UI, progression, and development tooling.

> [!IMPORTANT]
> This project is an independent, community-driven project and is not affiliated with, endorsed by, or sponsored by Electronic Arts.
>
> Need for Speed and Need for Speed: Most Wanted are trademarks of Electronic Arts Inc. All respective trademarks and intellectual property remain the property of their respective owners.

## Project Status

NFS MW Remaster is under **active development** and should currently be considered experimental.

Systems, APIs, assets, scenes, and tooling may change significantly as development progresses. Bugs, incomplete features, placeholder content, and breaking changes should be expected.

The project is being developed openly so that its progress, technical research, tooling, and implementation can be explored and contributed to by the community.

## Why Unity?

Development currently takes place on a base **Mac mini M4**, as I do not currently have access to a dedicated gaming PC.

Unity provides a practical development environment for the hardware available to me while still supporting a modern rendering pipeline through HDRP and allowing the project's gameplay systems and development tools to be built and tested.

The longer-term goal is to evaluate migrating the project to **Unreal Engine** once development funding allows me to build a suitable gaming PC.

Until then, Unity remains the project's primary development platform.

## Getting Started

### Requirements

Before cloning the project, install:

* **Git**
* **Git LFS**
* **Unity 6000.6.0f1**
* **Unity Hub**

The required Unity version is also recorded in:

```text
ProjectSettings/ProjectVersion.txt
```

Using a different Unity version may introduce asset, package, serialization, or rendering compatibility issues.

### Clone the Repository

Clone the repository and initialize Git LFS:

```bash
git clone <repository-url>
cd <repository-directory>

git lfs install
git lfs pull
```

Large binary files tracked through Git LFS will be downloaded during this process.

### Open the Project

1. Open **Unity Hub**.
2. Select **Add project from disk**.
3. Select the cloned repository.
4. Open it using **Unity 6000.6.0f1**.
5. Allow Unity to restore packages, compile scripts, and complete its initial asset import.

The first import may take some time.

### Run the Project

Once Unity has finished importing and compiling the project, open:

```text
Assets/NfsMw/Content/Frontend/UI/Scenes/Boot.unity
```

Enter **Play Mode** to start the current development build.

## Project Structure

```text
NFS-MW-Remaster/
├── Assets/
│   └── NfsMw/
│       ├── Runtime/
│       ├── Editor/
│       ├── Tests/
│       ├── Content/
│       └── ...
│
├── Art/
├── Packages/
├── ProjectSettings/
├── Tools/
└── README.md
```

### `Assets/NfsMw/`

Contains the primary Unity project content, including:

* Runtime systems
* Editor tooling
* Automated tests
* Scenes
* UI
* Game content
* Supporting assets

### `Art/`

Contains source artwork, models, and supporting authoring data used during content development.

### `Packages/`

Contains the project's Unity package dependencies and package configuration.

### `ProjectSettings/`

Contains the shared Unity project configuration.

### `Tools/`

Contains supporting development utilities for areas such as:

* Asset processing
* Reverse-engineering workflows
* Diagnostics
* Validation
* Development automation

See [`Tools/PROJECT_LAYOUT.md`](Tools/PROJECT_LAYOUT.md) for more information about the repository's content organization.

## Contributing

Contributions, technical research, bug reports, testing, documentation improvements, and development discussions are welcome.

When working with Unity assets, always commit the corresponding `.meta` files alongside the assets they belong to. Unity uses these files to maintain stable GUID-based references between assets.

Large binary assets are managed using **Git LFS**.

Generated content such as Unity caches, local editor settings, builds, temporary files, and validation output should not be committed unless explicitly required by the project.

Reference fixtures required by automated tests are retained in version control.

## Support Development

NFS MW Remaster is **free and open source**. Access to the project, its source code, and public builds is not dependent on financial contributions.

If you enjoy the project and would like to help support its continued development, voluntary contributions are greatly appreciated.

Contributions help support:

* Development hardware
* Testing hardware
* Development and research time
* Build and development infrastructure
* Tooling
* Project hosting and related services

Financial contributions are **entirely optional**.

Contributing does not unlock exclusive builds, features, game content, early access, or other gameplay benefits.

**[❤️ Support Development](https://github.com/sponsors/nanashili)**

## Disclaimer

This is an independent, non-commercial fan and research project.

The project is not affiliated with, authorized by, endorsed by, or sponsored by **Electronic Arts Inc.**

**Need for Speed**, **Need for Speed: Most Wanted**, and related names, trademarks, characters, artwork, and other intellectual property belong to their respective owners.

The purpose of this project is technical research, preservation, experimentation, education, and open-source development.

Nothing in this repository should be interpreted as claiming ownership of Electronic Arts' intellectual property.
