
 

<p align="center">
  <img src="assets/tasktru_title.svg" width="440" alt="taskTru logo"><br>
  <a href=https://github.com/erw-1/taskTru/releases/latest><img src="https://img.shields.io/github/downloads/erw-1/tasktru/total.svg"></a>
</p>

# taskTru 2.0

**taskTru** is a Windows utility that lets you make tasks click-through, topmost, transparent and cropped.  
Perfect for videos, images, code or anything you want to overlay over your apps. [Download the 2.0 here!](https://github.com/erw-1/taskTru/releases/latest)



## Core features
- **`Click-through`** - passes mouse input through a selected window
- **`Always on top`** - keeps selected windows above normal windows
- **`Opacity slider`** - adjusts window transparency
- **`Crop`** $^{\color{lightgreen}{\textsf{New!}}}$ - draw a feature-rich, movable, resizable PiP from any window, with auto video detection

> Everything above is stackable and can be set for several windows at a time. (**I'm working on a demo video!!**)
> 
### Also new in 2.0
> [!TIP]
> System tray and tray actions, user settings, automatic refresh, persistent state, global shortcuts,  
> startup options, user favorites / ignored task lists, auto video detection, a logo,
> interact mode for crop,  
> and many niche QoL improvements that you can discover in the [releases changelog](https://github.com/erw-1/taskTru/releases/).

### Shortcuts
> They apply to the focused window.  
> Global shortcuts can be disabled / edited in Settings.

| | |
| ---             | --- |
| `Ctrl+Alt+X`    | Toggle click-through |
| `Ctrl+Alt+T`    | Toggle always on top |
| `Ctrl+Alt+Up`   | Increase opacity by 5% |
| `Ctrl+Alt+Down` | Decrease opacity by 5% |
| `Ctrl+Alt+C`    | Crop the foreground window / recrop |
| `Ctrl+Alt+U`    | Uncrop |
| `Ctrl+Alt+I`    | Interact mode for cropped window |
| `Ctrl+Alt+R`    | Restore every managed window |
| `Ctrl+Alt+S`    | Show taskTru |

## Requirements

- Windows on x64 or arm64
- Small release executables require the .NET 10 Desktop Runtime (they will give you a download link 
for your system's version if you don't have it installed)
- `-full` release executables include the runtime and can start as is
- .NET 10 SDK for development

## They talk about it!
- Awesome ecosyste.ms • 🇬🇧 • [Link](https://awesome.ecosyste.ms/projects/github.com%2Ferw-1%2Ftasktru) • About 1.0 
- Softpedia Article • 🇬🇧 • [Link](https://www.softpedia.com/get/Desktop-Enhancements/Other-Desktop-Enhancements/taskTru.shtml) • About 1.0 
- User recommendation on Reddit • 🇬🇧 • [Link](https://www.reddit.com/r/firefox/comments/1q4bepu/anyway_to_make_pip_click_through/) • About 1.0 
- PCAstuces Article • 🇫🇷 • [Link](https://www.pcastuces.com/logitheque/tasktru.htm) • About 1.0 

## Support and dev stuff

> [!WARNING]
> A few apps behave differently under `Crop`, `Interact`, or `Video detection` (Windows exposes every app a little differently).
> Most I tested work great: if one misbehaves, please [open a compatibility issue](https://github.com/erw-1/taskTru/issues/new?labels=compatibility&template=compatibility.yml). What to expect, and what to include, is below.

<details>
  <summary><b>App compatibility & reporting a problem</b></summary>

  ### What usually works
  - **Crop** works for almost any normal window (it renders a live DWM thumbnail, which Windows provides for nearly everything).
  - **Interact** works for most apps: the cropped app receives real input while its thumbnail stays the visible face.
  - **Video detection** finds players that expose themselves through Windows UI Automation. Chromium browsers (Chrome, Brave, Edge, Vivaldi, Opera) and Firefox web players (YouTube and the like).

  ### Known limits
  - **Native video players** (VLC, MPC-HC, mpv, PotPlayer, Windows Media Player) don't expose a detectable player, so auto video crop won't find them. Draw the crop manually instead, it works fine. Feel free to request something and I'll see what I can do.
  - During **Interact**, an app's own pop-ups / dropdowns can open behind the thumbnail, and you move the crop with the floating handle (its edges are click-through).
  - A few apps relayout their content when moved or maximized, which can shift a crop slightly.

  ### Reporting a compatibility issue
  A good report makes it reproducible fast. Please include:
  - **App name + version**, and which feature broke (`Crop` / `Interact` / `Video detection`).
  - **What you expected vs. what happened**: a screenshot or short GIF helps enormously for visual glitches.
  - Your **monitor setup**: resolution and scaling % for each monitor (mixed-scaling setups matter a lot).
  - **Windows version** (10 or 11), and whether the app was **windowed / maximized / fullscreen**.

  [Open a compatibility issue →](https://github.com/erw-1/taskTru/issues/new?labels=compatibility&template=compatibility.yml)
</details>

<details>
  <summary><b>Project Layout</b></summary>

  ### Structure
  - `src/`: application source and project file
  - `assets/`: application icon and logo
  - `Build.ps1`: release tooling
  
  ### Within `src/`:
  - `Configuration/` contains the settings model, keyboard shortcut definitions, and saved window states.
  - `Cropping/` contains the crop selection overlay, the cropped thumbnail window with interact mode, and video detection.
  - `Interop/` contains the Win32, DWM, and UI Automation interop layers.
  - `UI/` contains forms, controls, and theme helpers.
</details>

<details>
  <summary><b>Contributing</b></summary>
  
  ### Contributions are welcome! Feel free to:
  - [Open an issue](https://github.com/erw-1/taskTru/issues) to report a bug or request a feature.
  - Fork and submit a Pull Request for improvements or new features.
</details>

<details>
  <summary><b>Build</b></summary>

  Debug/development build (also opens fine in Visual Studio with .NET 10 tooling):

  ```powershell
  dotnet build src\taskTru.csproj -c Release
  ```

  Release executables (x64 + arm64, portable + self-contained, published into `dist/`):

  ```powershell
  ./Build.ps1
  ```

  Build intermediates are written to `artifacts/`. Tested on Windows 10 and 11.

</details>
