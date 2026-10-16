# Contributing

Thanks for contributing.

This project is maintained by a sole maintainer and is currently not accepting external code contributions. Bug reports, reproduction steps, and suggestions are welcome. The maintainer implements fixes directly and may acknowledge reports in release notes. Reporting an issue does not require repository access.

## Development Setup

1. Install .NET 10 SDK.
2. Clone this repository.
3. Run:

   dotnet build Jellyfin.Plugin.Announcements.csproj -c Release

4. Publish for plugin deployment:

   dotnet publish Jellyfin.Plugin.Announcements.csproj -c Release -o publish

## Coding Guidelines

- Keep changes focused and minimal.
- Preserve existing plugin API routes unless intentionally versioning.
- Avoid unrelated formatting changes in touched files.
- Prefer explicit, readable logic over clever one-liners.

## Pull Requests

Please open a bug report or feature request instead of a code pull request. Existing pull requests may be closed when an independently implemented fix is available; the closing comment will link to the relevant change or release.

## Bug Reports

Please include:

- Jellyfin version
- Plugin version
- Repro steps
- Expected behavior
- Actual behavior
- Relevant logs
