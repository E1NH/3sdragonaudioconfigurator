# Dragon OS — Audio Configurator

**Spotify into OBS. One button. Done.**

[![Download Latest](https://img.shields.io/github/v/release/E1NH/3sdragonaudioconfigurator?label=Download&style=for-the-badge&color=E94560&logo=windows)](https://github.com/E1NH/3sdragonaudioconfigurator/releases/latest)

A free, open-source Windows tool that routes Spotify's audio into OBS as a clean, isolated track. No system-wide recording, no mic bleed, no digging through four layers of Windows audio settings. Download it, run it, click one button, get back to streaming.

---

## What it actually does

Spotify on Windows is a mess under the hood. It runs as a pile of Chromium processes and routes audio wherever it wants. Getting it into OBS as its own dedicated track has always meant hunting down virtual audio drivers, finding the per-app routing settings buried in Windows, and hoping none of it breaks after a reboot.

This tool handles all of that automatically. It checks whether Spotify is installed, grabs and silently installs VB-Cable (the free virtual audio driver from VB-Audio's own servers), then binds every Spotify process to the virtual endpoint using the same Windows API that the native Settings panel uses internally. It covers all three audio roles so nothing slips through regardless of which one Spotify's active session is using. When it's done, it confirms the virtual device is actually live before calling it a success.

Run it once. If the driver was freshly installed, reboot and run it one more time. That's genuinely it.

---

## Requirements

- Windows 10 (build 1607 or later) or Windows 11
- Spotify desktop client installed
- An internet connection for the VB-Cable download (about 5 MB)
- Administrator rights (the app asks via the standard UAC prompt)

---

## How to use it

Download the latest release from the [Releases](../../releases/latest) page, extract the ZIP, and run `DragonOS.AudioConfigurator.exe`. Accept the UAC prompt, click **Configure Audio**, and let it run through the steps. Once it finishes, go into OBS and add **CABLE Output** as an audio capture source. If VB-Cable was just installed for the first time, reboot once and run the configurator again so Windows properly registers the driver.

Spotify now has its own audio track in OBS, fully separate from everything else on your system.

---

## It's free. No catches.

No license key, no account, no telemetry, no ads. Take it, fork it, share it.

The setup process for this has always been more complicated than it should be. Nobody should lose an afternoon to it. If this saves you that time, great.

---

## Serious about streaming? There's more.

This tool solves one specific problem. If you want to run a proper streaming operation without stitching together five separate apps every time you go live, take a look at what we built on top of it.

### [3sdragon.eu](https://3sdragon.eu)

**3S Dragon** is a full streaming control platform. Schedule streams, manage sources, handle routing, and run your whole broadcast from one place. No copyright strikes, because the platform is built from the ground up to keep your channel safe in ways that manual setups simply can't.

The privacy side is worth talking about directly. Your stream configs, schedules, and settings live on your computer. We store nothing server-side — not because we anonymise it or encrypt it somewhere you can't see, but because we never receive it in the first place. The architecture makes it structurally impossible for us to have your data.

Every piece of infrastructure runs inside Europe. Not "EU-compliant," not "GDPR-certified with exceptions." Every server, every component, every dependency is EU-only, full stop.

👉 **[3sdragon.eu](https://3sdragon.eu)**

---

## Technical notes

The routing uses `IAudioPolicyConfigFactory.SetPersistedDefaultAudioEndpoint`, the undocumented COM interface that backs Windows' own "App volume and device preferences" panel. The tool handles both the Windows 10 variant (vtable offset 11) and the Windows 11 variant (vtable offset 25 via `IInspectable` base) automatically. It routes across all three endpoint roles (`eConsole`, `eMultimedia`, `eCommunications`) to make sure Spotify's Chromium cluster is fully covered no matter which role the active audio session lands on.

The virtual cable driver is [VB-Cable by VB-Audio](https://vb-audio.com/Cable/). It's free, it's been the standard for virtual audio routing on Windows for years, and all credit for it goes to them. This tool just installs it for you.

The UI is built in WPF on .NET 10 and follows the Dragon OS visual language — void-black palette, hex-grid texture, chamfered panel corners, Rajdhani typography.

---

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Windows.

```powershell
git clone https://github.com/3sdragon/audio-configurator
cd audio-configurator/AudioConfigurator

# Development run
dotnet run --project src\WPF\DragonOS.AudioConfigurator.WPF.csproj

# Release build
dotnet build DragonOS.AudioConfigurator.sln -c Release --nologo
```

### Signed release builds

Official releases are signed via **[SignPath](https://signpath.io)** to avoid Windows SmartScreen warnings. Community builds are unsigned — Windows will show a SmartScreen prompt on first run, which is normal for unsigned executables.

---

## Contributing

PRs are welcome. If a future Windows build shifts the audio API and the vtable layout changes, the best references for updated GUIDs and offsets are [EarTrumpet](https://github.com/File-New-Project/EarTrumpet) and [SoundSwitch](https://github.com/Belphemur/SoundSwitch).

---

*Dragon OS Audio Configurator is an independent open-source project, not affiliated with Spotify, VB-Audio, or OBS Project.*
