# AR Foundation Setup

## Package Choices

The project uses:

```text
com.unity.xr.arfoundation
com.unity.xr.arcore
com.unity.xr.management
```

AR Foundation provides the Unity-facing AR API. Google ARCore XR Plugin provides the Android implementation. XR Plugin Management controls which XR provider is enabled per platform.

## Android Settings

Recommended Android settings:

```text
Package Name: com.example.arspacememo
Minimum API Level: Android 10.0/API 29 or newer
Target API Level: Highest installed
Scripting Backend: IL2CPP
Target Architectures: ARM64
XR Plug-in Management: ARCore enabled for Android
```

API 29 is required for this project configuration because ARCore Required apps using Vulkan fail Unity's Android build validation below that level.

## Scene Components

The generated scene should include:

```text
AR Session
XR Origin (AR)
AR Camera
AR Camera Manager
AR Camera Background
AR Plane Manager
AR Raycast Manager
```

`ARPlacementController` reads touch input, asks `ARRaycastManager` for a plane hit, and instantiates `MemoCard.prefab` at the hit pose.

## Editor Limitations

Unity Editor cannot fully validate Android AR behavior without an AR simulation setup. Use Editor checks for compile errors and missing references only. Real AR behavior must be tested on an ARCore supported Android device.

## Project Validation

After enabling ARCore, open Project Validation in Unity and apply relevant fixes. Record any changed settings in `docs/build-verification.md`.
