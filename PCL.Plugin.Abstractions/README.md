# PCL Plugin Abstractions

Plugin SDK contract assembly for Plain Craft Launcher.

This repository contains only public plugin contracts and shared DTOs. It should stay independent from `PCL.Core`; runtime implementations live in the launcher host.

## Build

```powershell
dotnet build .\PCL.Plugin.Abstractions.csproj --configuration Debug --property:Platform=AnyCPU
```

## Pack

The project is packable. When you are ready to distribute the SDK as a package:

```powershell
dotnet pack .\PCL.Plugin.Abstractions.csproj --configuration Release --property:Platform=AnyCPU
```

## Local Development

The launcher and in-repository plugins reference this project directly via repository-relative project references.
External plugin repositories can either reference this project during development or switch to a `PackageReference` once the SDK is published.
