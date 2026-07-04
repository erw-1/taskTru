# Changelog

Notable changes to taskTru. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [2.0.0] - 2025-07-05

taskTru 2.0 is a full rewrite.  
v1 was a single panel that toggled three window styles that needed external PiP browser extensions to be used for video content for instance.
v2 turns it into a window utility built around cropping any live windows application on top of existing features, packed with QoL and polish.

### Added

- **Crop mode.** Select any part of a window with a selection overlay. The result is a live, always-on-top (by default) thumbnail window of
the drawn region, rendered through DWM, with aspect-ratio-locked resizing. Drag it anywhere you want by holding it and resize it to any size
you desire using borders. Several windows can be cropped at once.

  - **Cropping rectangle:** drag to draw a rectangle, drag inside to move, drag edges or corner handles to
  resize, drag outside to draw a new one, hit <kbd>Enter</kbd> or the button to finish, <kbd>Esc</kbd> or right click to cancel. 

  - **Per-crop overlay controls:** Hovering a crop reveals buttons for
  toggling interact, recrop, opacity, click-through, and uncrop (can also use <kbd>Esc</kbd>). All these existing 1.0 features are compatible
  with crop mode.

  - **Interact mode:** A cropped window receives inputs until toggled off: mouse clicks, cursor hovers, typing, and
  video keep rendering live. It restores its native scaling while you are interacting with it and a floating handle can be used to move the
  cropped window, since trying to drag the window in that state would just input mouse clicks on the content.

  - **Recrop:** Adjust an existing crop without losing its window position or
  settings.

  - **Auto video crop:** Passive detection spots video players inside windows
  and offers a one-click crop to the player. A manual "attempt auto video
  crop" action is available per window during crop and by shortcut (<kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>V</kbd> by default) on the currently focused window.

- **User settings** with persisted options, accessible from the new gearwheel button:
  - start with Windows, start minimized to tray,
  - close button minimizes to tray,
  - restore tasks when
  taskTru exits, confirm before exiting with active tasks,
  - auto lock on top when cropping,
  - show opacity percentage,
  - flash newly detected windows / title changes,
  - auto scan for video content, task list auto refresh interval, manual refresh mode,
  - compact task rows,
  - global shortcut toggle and
  editor,
  - favorites and ignore lists.

- **Global keyboard shortcuts** <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>key</kbd> by default, all rebindable in settings.
  - click-through (<kbd>X</kbd>), lock on top (<kbd>T</kbd>), opacity up/down (<kbd>Up</kbd>/<kbd>Down</kbd> arrow keys),  
  - crop/recrop (<kbd>C</kbd>), auto video crop (<kbd>V</kbd>), uncrop (<kbd>U</kbd>), crop interact (<kbd>I</kbd>),  
  - restore all (<kbd>R</kbd>), and showing taskTru (<kbd>S</kbd>)  

- **System tray:** taskTru can start minimized, minimize to tray on close, and exposes per-window actions (crop, opacity, click-through, lock on top, reset) from the tray menu without opening the main window.  

  The tray icon displays a blue circle when a window is affected by taskTru, it displays the number of affected windows in the tray menu, and marks them in the list.

- **Favorites and ignored tasks:** Favorites are pinned to the top of the list and ignored executables are hidden (they can be temporarily shown by using a reveal button or managed from settings). Both separate systems are off by default and can be enabled in settings.

- **Task list upgrades:** Window icons, favorite/ignore buttons, automatic refresh on an adjustable interval,
  per-row reset button, and clicking task names focuses them.

- **State tracking and restore:** taskTru remembers each window's original
  style and placement. Uncropping, per-row reset, "restore everything", and
  exiting the app put every touched window back exactly as it was.

- **Touch and gesture support**, including pinch-to-zoom on the crop
  selection and for scaling crop windows on tablets.

- **arm64 builds** and self-contained `-full` single-file executables that
  bundle the runtime, produced by the new `Build.ps1` release tooling.

- **A logo!** I made by hand in svg code lol

- **A lot of love** to make these new features work in tandem with most task windows, tested on win11 and win10❤️

### Changed

- **Complete UI overhaul**: custom dark theme with rounded controls, themed
  scrollbars, tooltips, dark title bars, and rounded corners,
  replacing the plain checkbox rows and unpolished interface of 1.0.
- The window list refreshes itself by default.
- Per-monitor V2 DPI awareness for correct behavior on mixed-DPI setups.
- Runtime upgraded from .NET 8 to .NET 10.
- Supported architectures are now x64 and arm64 (x86 releases are dropped but feel free to build them from source code).
- Repository restructured (`src/`, `assets/`, `docs/`, `Build.ps1`) with build metadata for reproducible releases.

### Fixed

- UI task state persistence, 1.0 would uncheck the "lock on top" checkbox in the UI after a refresh for instance, even if the task was still locked on top...
- Window changes are no longer one-way: 1.0 could toggle click-through,
  topmost, and opacity but never restored a window's original state; 2.0
  reverses everything it touches, even on exit.
- Self-contained executable are now 75% smaller, packed less unused junk.

## [1.0.0] - 2025-01-16

Initial release.

- Always-on-top panel listing open windows.
- Per-window click-through, lock on top, and opacity slider (0-100%).
- Manual Refresh button to re-enumerate windows.
- Dark-themed WinForms UI on .NET 8, shipped as x86/x64 portable executables + self-contained.

[1.0.0]: https://github.com/erw-1/taskTru/releases/tag/1.0.0
