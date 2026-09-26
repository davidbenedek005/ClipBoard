# Packaging

Phase 5 produces a Windows installer and a signed Android APK. Both are for your own machines. The Android build is sideloaded. It is not a Play Store package.

## Windows: self-contained executable

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File windows-app\publish-release.ps1
```

That runs:

```powershell
dotnet publish windows-app\ClipboardSync.App\ClipboardSync.App.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false
```

The executable is:

`windows-app\ClipboardSync.App\bin\Release\net10.0-windows\win-x64\publish\ClipBoard.exe`

`--self-contained true` copies the .NET runtime into that folder, so the other PC does not need a .NET install. `-r win-x64` is the 64-bit Windows build. Quit any ClipBoard tray icon before you publish, or the file copy can fail because the exe is locked.

You can copy that `publish` folder onto another Windows 11 PC and run `ClipBoard.exe`. The installer below is the same bits with a Start menu shortcut and an optional sign-in startup entry.

## Windows: Inno Setup installer

1. Install [Inno Setup 6](https://jrsoftware.org/isdl.php).
2. Run `publish-release.ps1` first, so the publish folder exists.
3. Open `windows-app\installer\ClipBoard.iss` and choose **Build > Compile** (or run `ISCC.exe` on that file).
4. The installer is `windows-app\installer\output\ClipBoard-Setup.exe`.

The script installs per user under `%LOCALAPPDATA%\Programs\ClipBoard`. It does not ask for administrator rights. On the last page, **Start ClipBoard when I sign in** is optional. Leave it unchecked and the app only starts from the Start menu shortcut.

The first time the app listens on port 53211, Windows Firewall may ask for access. Allow it on private networks.

## Android: signed release APK

R8 is off for release. `isMinifyEnabled` is `false` in `android-app/app/build.gradle.kts`. OkHttp and the clipboard JSON use `org.json`, not kotlinx.serialization, so there is no shrinker step that can strip those classes. `proguard-rules.pro` is only there if you later turn minify on. Leave it off for this sideload build.

Do not commit the keystore or its passwords.

1. Open the `android-app` folder in Android Studio (not the repository root).
2. **Build > Generate Signed App Bundle or APK**.
3. Choose **APK**, then **Next**. An Android App Bundle is for the Play Store. This install uses an APK.
4. **Create new...** under Key store path.
   - Save the file outside the repo, for example `C:\Users\david\clipboardsync-release.jks`.
   - Set a keystore password, a key alias (for example `clipboardsync`), and a key password.
   - Fill in the certificate name. The other certificate fields can stay empty.
   - **OK**.
5. Back on the signing page, confirm the store path, both passwords, and the alias. **Next**.
6. Select **release**. Leave **Signature Versions** with **V2 (Full APK Signature)** enabled. V1 can stay on as well. **Create**.
7. When the build finishes, **locate** opens the folder. The file is `app-release.apk` under `android-app\app\release\`.

Later releases reuse the same keystore. **Choose existing...** instead of creating a new one. Bump `versionCode` and `versionName` in `app/build.gradle.kts` before you build the next APK. Android will refuse an update whose `versionCode` is not higher.

## Install the APK on the phone

1. Copy `app-release.apk` to the phone (USB, or a shared folder on your LAN).
2. Open the file on the phone. If Android blocks it, allow installs from that file manager.
3. Install. If a debug build is already installed and the signatures differ, uninstall that debug build first. A release signature cannot update a debug-signed app.
4. Open ClipBoard, allow notifications, turn the accessibility service back on, and pair again. The phone camera QR link and **Manual connection** both still work.

## Check after install

- Windows tray icon is present. Left-click it, then **Pairing** shows the QR, address, port, and token.
- Phone notification says connected after pairing.
- Copy text on the PC and paste on the phone.
- Copy text on the phone, open ClipBoard, and paste on the PC.
- Copy an image each way with **Sync images** on.
- Optional: reboot the PC with the startup task enabled and confirm the tray icon returns.
