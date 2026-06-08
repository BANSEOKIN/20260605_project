# Environment Check

Project: ARSpaceMemo

## Local Environment

| Item | Status | Notes |
| --- | --- | --- |
| Unity Hub | Installed | Verified by project creation through Unity Hub. |
| Unity Editor | Installed | Project version: `6000.4.10f1`. |
| Unity LTS | Needs manual confirmation | Unity 6000 is the Unity 6 generation. Confirm exact LTS label in Unity Hub before publishing. |
| Project template | Ready | Universal 3D / URP template. |
| IDE | Ready | Visual Studio Code selected as External Script Editor. `.csproj` files were regenerated. |
| Git LFS | Ready | `git-lfs/3.7.1` installed and initialized. |
| Android Build Support | Ready | Installed for Unity `6000.4.10f1`. |
| Android SDK & NDK Tools | Ready | Installed under Unity AndroidPlayer. SDK platforms include Android 34, 35, and 36. |
| OpenJDK | Ready | Installed under Unity AndroidPlayer. |
| AR Foundation package | Ready | Resolved to `6.4.3` after `6.1.1` failed against Unity `6000.4.10f1` / URP `17.4.0`. |
| ARCore XR Plugin package | Ready | Resolved to `6.4.3` to match AR Foundation. |
| XR Plugin Management package | Ready | Package resolved. |
| Android platform switch | Ready | Batch switched/imported Android platform settings. |
| Android APK build | Ready | `Builds/Android/ARSpaceMemo.apk` built successfully. |
| ARCore Android test device | Not verified here | Test on a Google ARCore supported Android device. |
| Unity batch execution | Ready | Full `Library` cache regeneration and batch compile completed successfully. ARCore loader configuration also completed successfully. |

## Privacy Notes

Do not publish local usernames, absolute filesystem paths, account names, Android keystore details, or internal organization identifiers. Use generic examples such as:

```text
~/Projects/ARSpaceMemo
com.example.arspacememo
```

## Next Manual Checks

1. Open Unity and confirm there are no Console compile errors.
2. Confirm `Project Settings > XR Plug-in Management > Android > ARCore` is checked.
3. Confirm `File > Build Settings > Android` is the active platform.
4. Run Project Validation and apply relevant fixes.
5. Install `Builds/Android/ARSpaceMemo.apk` on an ARCore supported Android device.
