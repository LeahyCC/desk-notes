# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.2.0] - 2026-10-27

### Added

- MIT license, contributing guide, code of conduct and security policy.
- CI build and a release workflow that publishes both exes.
- Screenshots in the README.
- An app icon, on the exe and in the tray.
- Ctrl+Alt+N makes a new note at the mouse, and Ctrl+Alt+H shows or hides every note.
- Glass level per note: Clear, Glass, Frosted or Solid.
- Lock position, so a note cannot be dragged or resized.
- A note saved on a monitor that is gone comes back on the main one, stepped apart from any others.
- Crashes are written to `%APPDATA%\DeskNotes\error.log`.

### Changed

- Starting DeskNotes while it is already running shows the notes.

### Fixed

- Note loss and startup bugs found in review.
- Click-through applies as soon as it is turned on.
- Start with Windows turns off when the exe has moved.
- A note file that cannot be read is logged, and the other notes still open.

## [0.1.0] - 2026-10-26

### Added

- Sticky notes with five layers: Stay topmost, Topmost, Normal, Bottom and On desktop.
- Click-through with a live strip at the top.
- Notes saved as markdown with frontmatter in `%APPDATA%\DeskNotes\notes\`.
- Tray icon with new note, show or hide all, start with Windows, open folder and quit.

[Unreleased]: https://github.com/LeahyCC/desk-notes/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/LeahyCC/desk-notes/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/LeahyCC/desk-notes/releases/tag/v0.1.0
