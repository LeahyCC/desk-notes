# Contributing

Thanks for helping. Issues and pull requests are both welcome.

## Before you start

- For a bug fix, go ahead and open a pull request.
- For anything bigger, open an issue first so we can agree on the approach.
- Questions go in [Discussions](https://github.com/LeahyCC/desk-notes/discussions).

## Build

Needs Windows 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```
dotnet run
```

## Checking your change

CI builds the app, runs the tests, and publishes both exes. Run the tests with:

```
dotnet test DeskNotes.Tests
```

Before you open a pull request, try your change by hand too. Depending on what it touches:

- Each of the five layers, including On desktop with Win+D
- Click-through, and turning it off again from the top strip
- Dragging and resizing
- Restarting DeskNotes and checking the note comes back where it was
- A second monitor, if you have one
- Start the exe again while the notes are hidden
- Run a copy from another folder and the first one quits

## Pull requests

- Keep one change per pull request.
- Write commit messages as a short sentence in the imperative: "Fix note loss on startup".
- If the change alters how something works, update the README in the same pull request.
- Add a line to `CHANGELOG.md` under Unreleased.
- Follow `.editorconfig`. Most editors pick it up on their own.

Pull requests are squash-merged into `main`.

## Releases

The git tag is the version. The release workflow reads it from the tag and passes it to the build, so the project file has no version number to bump. Local builds report 1.0.0.

To release:

1. Move the Unreleased entries in `CHANGELOG.md` under the new version, update the compare links at the bottom, and merge that.
2. Tag `main` with the version (`v0.2.0`) and push the tag.

The release workflow then builds both exes and attaches them to a GitHub release. The release text is the changelog section for that version.

## Code of Conduct

By taking part you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
