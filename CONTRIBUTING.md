# Contributing

Thanks for contributing.

## Development Setup

1. Install .NET 9 SDK.
2. Clone this repository.
3. Run:

   dotnet build -c Release

4. Publish for plugin deployment:

   dotnet publish -c Release -o publish

## Coding Guidelines

- Keep changes focused and minimal.
- Preserve existing plugin API routes unless intentionally versioning.
- Avoid unrelated formatting changes in touched files.
- Prefer explicit, readable logic over clever one-liners.

## Pull Requests

- Describe what changed and why.
- Include Jellyfin version tested.
- Include before/after behavior for UI or API changes.
- Update CHANGELOG.md for user-visible changes.

## Bug Reports

Please include:

- Jellyfin version
- Plugin version
- Repro steps
- Expected behavior
- Actual behavior
- Relevant logs
