# AI Agent Guide

This project is a Unity Android AR Foundation project named ARSpaceMemo.

## Project Rules

- Target platform is Android.
- AR provider is Google ARCore XR Plugin.
- C# runtime scripts go under `Assets/Scripts`.
- Editor-only tooling goes under `Assets/Editor`.
- Do not delete `.meta` files.
- Do not modify Android signing settings unless explicitly requested.
- Do not change `ProjectSettings` casually. Record why a setting changed.
- Record XR Plug-in Management changes and the reason.
- Scene and prefab changes should be reviewable by a human in Unity Editor.
- After script changes, explain which GameObject should receive the component.

## Current Scripts

| Script | Role | Scene Attachment |
| --- | --- | --- |
| `ARPlacementController.cs` | Touch input and AR raycast placement | `ARPlacementController` GameObject |
| `MemoCard.cs` | Sets memo text and faces the camera | `MemoCard.prefab` |
| `MemoInputController.cs` | Reads UI input or default memo text | `MemoInputController` GameObject |
| `MemoManager.cs` | Tracks created memo cards and clears all | `MemoManager` GameObject |

## Manual Unity Work

If package resolution succeeds, run:

```text
Tools > AR Space Memo > Build Main Scene
```

Then inspect:

```text
Assets/Scenes/Main.unity
Assets/Prefabs/MemoCard.prefab
```

Enable ARCore:

```text
Project Settings > XR Plug-in Management > Android > ARCore
```

## Verification Notes

Editor validation:

- Package resolution succeeds.
- Scripts compile.
- `Main.unity` opens.
- `ARPlacementController` has references assigned.
- `MemoCard.prefab` has `MemoCard` and text assigned.

Device validation:

- Android camera permission appears.
- Camera background appears.
- Plane detection works.
- Touching a detected plane creates a memo.
- Multiple memos can be placed.
- Clear button removes all memos.
