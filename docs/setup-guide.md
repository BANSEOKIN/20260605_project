# Setup Guide

## Project Shape

ARSpaceMemo is a Unity Android AR toy project. It uses Unity 6, URP, AR Foundation, and Google ARCore XR Plugin.

Main folders:

```text
Assets/
  Editor/
  Materials/
  Prefabs/
  Scenes/
  Scripts/
  UI/
docs/
Packages/
ProjectSettings/
```

## Unity Basics

- Unity Hub: installs and manages Unity Editor versions and platform modules.
- Unity Editor: the app used to edit scenes, packages, build settings, and assets.
- Scene: a saved Unity world. This project uses `Assets/Scenes/Main.unity`.
- GameObject: an object in a scene.
- Component: behavior or data attached to a GameObject.
- Script: a C# component.
- Prefab: a reusable GameObject asset.
- AR Session: controls the AR lifecycle.
- XR Origin: the root transform for AR camera tracking.
- ARRaycastManager: converts a screen touch into an AR hit on detected planes.
- AR Plane Detection: detects real-world surfaces such as floors and tables.

## Git Rules

Unity generates large local folders that should not be committed:

```text
Library/
Temp/
Obj/
Build/
Builds/
Logs/
.vs/
.idea/
```

Unity `.meta` files must be committed. They store Unity asset GUIDs and keep scene, prefab, material, and script references stable across machines. Deleting or ignoring `.meta` files can break references.

Git LFS is configured through `.gitattributes` for large binary assets such as images, audio, video, models, archives, and Unity packages.

## Main Scene Generation

The main scene has been generated. To regenerate it later, run:

```text
Tools > AR Space Memo > Build Main Scene
```

This creates:

```text
Assets/Scenes/Main.unity
Assets/Prefabs/MemoCard.prefab
Assets/Materials/MemoCard.mat
```

It also adds `Main.unity` to Build Settings.

## Manual Unity Settings

1. `File > Build Settings > Android > Switch Platform` if Android is not active.
2. `Edit > Project Settings > XR Plug-in Management`
3. Android tab: enable `ARCore` if it is not already checked.
4. `Project Settings > Player > Android`
5. Confirm:
   - Package Name: `com.example.arspacememo`
   - Minimum API Level: Android 10.0/API 29 or newer
   - Scripting Backend: IL2CPP
   - Target Architectures: ARM64

If XR Plug-in Management does not show the Android tab, confirm Android Build Support is installed for Unity `6000.4.10f1` in Unity Hub and reopen the project.

## Manual Scene Review

Check the scene includes:

```text
AR Session
XR Origin (AR)
AR Camera
ARPlacementController
MemoManager
MemoInputController
Canvas
EventSystem
```
