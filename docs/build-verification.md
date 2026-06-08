# Build Verification

## Current Status

Status: Editor setup and Android APK build verified. Device AR run not yet verified.

The project files, AR package dependencies, runtime scripts, scene assets, Android module install, script compilation, Android ARCore loader setup, and Android APK build are verified through Unity batchmode. Real device AR behavior still needs verification.

## Editor Verification

| Check | Result | Notes |
| --- | --- | --- |
| Unity project detected | Passed | Project version `6000.4.10f1`. |
| VS Code project files | Passed | `.csproj` files exist after regeneration. |
| Git LFS | Passed | `git-lfs/3.7.1` installed and initialized. |
| Android module | Passed | Android Build Support, SDK, NDK, and OpenJDK are installed for Unity `6000.4.10f1`. |
| AR packages added | Passed | Manifest and lock are set to AR Foundation/ARCore `6.4.3`; PackageCache resolved those versions. |
| Script compile | Passed | Full `Library` cache regeneration and Unity batch compile completed successfully. |
| Scene generation | Passed | `Assets/Scenes/Main.unity`, `Assets/Prefabs/MemoCard.prefab`, and `Assets/Materials/MemoCard.mat` exist. |
| ARCore loader | Passed | `Assets/XR/Loaders/ARCoreLoader.asset`, `Assets/XR/Settings/ARCoreSettings.asset`, and `Assets/XR/XRGeneralSettingsPerBuildTarget.asset` were generated. Batch log reported `ARSpaceMemo Android ARCore loader configured.` |

## Android Build Result

Status: Passed.

Output:

```text
Builds/Android/ARSpaceMemo.apk
```

Verified output size: `42M`.

Build method:

```text
Tools > AR Space Memo > Build Android APK
```

The first build attempt failed because ARCore Required apps using Vulkan require Android API 29 or newer. `ProjectSettings/ProjectSettings.asset` was updated from `AndroidMinSdkVersion: 26` to `AndroidMinSdkVersion: 29`, and the next build completed successfully.

## Device AR Run

Status: Not Run

## Previous Compile Error

Unity Console/Editor log previously reported package-internal compile errors from AR Foundation `6.1.1`:

```text
Library/PackageCache/com.unity.xr.arfoundation.../ARBackgroundRendererFeature.cs:
error CS0115: no suitable method found to override

Library/PackageCache/com.unity.xr.arfoundation.../SimulationCameraTextureReadbackPass.cs:
error CS0115: no suitable method found to override
```

## Suspected Cause

This project is Unity `6000.4.10f1` with URP `17.4.0`. The resolved AR Foundation package is `6.1.1`, whose package metadata targets Unity `6000.0`. URP `17.4.0` changed rendering APIs, so AR Foundation `6.1.1` renderer feature code does not compile against this project.

## Applied Fixes

`Packages/manifest.json` and `Packages/packages-lock.json` were updated to:

```text
com.unity.xr.arfoundation: 6.4.3
com.unity.xr.arcore: 6.4.3
```

The stale Unity `Library` cache was deleted and regenerated. This cleared the old AR Foundation compile path errors.

Android Build Support, SDK, NDK, and OpenJDK were installed through Unity Hub for `6000.4.10f1`.

`Tools > AR Space Memo > Build Main Scene` generated the main AR memo scene, memo prefab, and material.

`Tools > AR Space Memo > Build Android APK` was added for repeatable batch/menu APK builds.

The editor setup tool was updated for Unity 6/XR Management by using `Any(...)` instead of `List.Exists(...)` on `XRManagerSettings.activeLoaders`.

## Previous Batch Blocker

Earlier Unity batch launches stopped after:

```text
Licensing initialization failed
```

This was resolved on the later batch run after restarting Unity licensing and regenerating `Library`.

## Current Warnings

During import, Unity reported:

```text
xcrun: error: unable to find utility "metal"
xcrun: error: unable to find utility "metal-objdump"
```

Script compilation, ARCore setup, and Android APK build still passed. If shader import or Apple platform builds fail later, install or repair full Xcode/Command Line Tools.

Unity also warned that this package asset was unexpectedly altered during ARCore build processing:

```text
Packages/com.unity.xr.arcore/Tests/Editor/Assets/TestReferenceImageLibrary.asset
```

No build failure resulted from this warning.

## Next Checks

1. Open the project from Unity Hub.
2. Confirm the Console has no compile errors.
3. Confirm `XR Plug-in Management > Android > ARCore` is checked.
4. Confirm Android is the active platform.
5. Install `Builds/Android/ARSpaceMemo.apk` on an ARCore supported Android device.
6. Confirm camera permission, plane detection, tap placement, and memo input behavior.

## Failure Log Template

```text
## Build Result

Status: Failed

## Error Summary

...

## Suspected Causes

...

## Next Checks

...
```
