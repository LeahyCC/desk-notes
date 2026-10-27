# DeskNotes

Sticky notes for Windows 11 that live on the desktop the way Rainmeter skins do. Each note picks its own layer.

```
Stay topmost   always above everything, re-asserted every second
Topmost        above normal windows
Normal         ordinary window
Bottom         under every window, above the wallpaper
On desktop     Bottom, and stays visible when Win+D shows the desktop
```

## Run

Needs the .NET 10 SDK.

```
dotnet run
```

Publish a single exe:

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

The exe lands in `bin\Release\net10.0-windows\win-x64\publish\`. The tray menu has "Start with Windows".

## Use

- Right-click a note: layer, colour, text size, click-through, new note, delete.
- Drag by the padding around the text. There is no title bar.
- Resize from any edge.
- Ctrl+mouse wheel changes text size.
- Tray icon: new note, show or hide all, start with Windows, open the notes folder, quit.

A small dot in the top-left corner shows the layer: amber topmost, orange stay topmost, blue bottom, green on desktop. No dot means normal.

Click-through makes the mouse fall through the note to whatever is under it. A faint strip across the top stays live: right-click it to turn click-through off again, or drag it to move the note.

## Files

Notes are plain markdown in `%APPDATA%\DeskNotes\notes\`, one file per note, with position and look in a frontmatter block. Deleted notes move to `%APPDATA%\DeskNotes\trash\`. Frontmatter lines DeskNotes does not use (Obsidian tags, say) are kept when it saves.

```
---
x: 1850
y: 900
w: 260
h: 200
color: #202020
font: 14
z: OnDesktop
clickthrough: true
---
Note text here.
```

## How the layers work

`ZOrder.cs` has the detail. The short version:

- Bottom hooks `WM_WINDOWPOSCHANGING` and rewrites every raise request to `HWND_BOTTOM`.
- On desktop is Bottom plus a watcher. A hidden marker window sits at the bottom of the stack. When Show Desktop lifts the desktop window above the marker, the notes go topmost until it drops back.
- Re-parenting into the desktop window (what wallpaper engines do) was tried first. On Windows 11 24H2 and later nothing parented there renders, so it was dropped.
- Notes swallow `SC_MINIMIZE` and `SC_CLOSE`, so Win+M and Alt+F4 do not take them away.
- Click-through sets `WS_EX_TRANSPARENT`. A 40ms timer clears it while the cursor is over the top strip, because a transparent window gets no mouse messages at all.

## Status

First cut. Not yet checked by hand: the On desktop layer during Win+D, and behaviour on a second monitor. The app is system DPI aware, so on a monitor scaled differently from the main one Windows stretches the notes and the text looks soft.
