# Project content names

Current authored content and tooling use names describing their purpose:

| Content | Location |
| --- | --- |
| UI images, grouped by function | `Assets/NfsMw/Content/Frontend/UI/Resources/MostWantedUI/Images` |
| Bitmap font atlases and metrics | `Assets/NfsMw/Content/Frontend/UI/Resources/MostWantedUI/Fonts` |
| UI image lookup catalog | `Assets/NfsMw/Content/Frontend/UI/Resources/MostWantedUI/Catalog.json` |
| Shared vehicle textures | `Assets/NfsMw/Content/Vehicles/Shared/Textures` |
| Sky, particles and world effects | `Assets/NfsMw/Content/World/Textures` |
| Showroom reflection textures | `Assets/NfsMw/Content/Frontend/Models/SharedTextures/ShowroomEnvironments` |
| Frontend and HUD runtime scripts | `Assets/NfsMw/Content/Frontend/UI/Runtime` |
| Frontend editor tools | `Assets/NfsMw/Content/Frontend/UI/Editor` |
| Frontend tests and test scenes | `Assets/NfsMw/Content/Frontend/UI/Tests` |
| Frontend settings and boot scene | `Assets/NfsMw/Content/Frontend/UI/Data` and `Assets/NfsMw/Content/Frontend/UI/Scenes` |
| Vehicle recordings and banks | Each vehicle's `Sound/Clips` directory |
| Vehicle model source cache | `Art/Cars/Models` |
| UI source artwork | `Art/UI/Artwork` |
| Frontend room source artwork | `Art/FrontendRooms/Artwork` and `Art/FrontendCourtyard/Artwork` |
| Frontend asset tools | `Tools/FrontendAssets` |
| World authoring tools | `Tools/WorldTools` |
| Warehouse asset tools | `Tools/WarehouseAssets` |
| Audio analysis tools | `Tools/AudioAnalysis` and the Driving module's `Editor/AudioAnalysis` |
| Handling binary reader | `MostWantedHandlingReader` and `Tools/DrivingMechanics/run-reader.sh` |

Runtime resource paths, catalogs, Unity metadata, C# namespaces and types, assembly definitions, Python imports, shell commands, and current documentation follow these names. Original attribution and source hashes remain intact. Historical captures retain their recorded contents; current frontend verification matches those records by content identity through `Tools/FrontendAssets/image-library.json`. Both texture importers use this registry to retain the curated folder layout and existing GUIDs.
