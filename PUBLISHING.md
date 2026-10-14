# Publishing to Paradox Mods

Shelve It is published with the official Cities: Skylines II modding toolchain. Without the toolchain the project still builds and deploys locally (see README).

## One-time setup

1. In the game, open Options > Modding and install the modding toolchain. It sets user environment variables, among them `CSII_TOOLPATH`, `CSII_MODPUBLISHERPATH` and `CSII_LOCALMODSPATH`.
2. Restart your terminal (or IDE) so it sees the new variables. `ShelveIt.csproj` imports the toolchain's `Mod.props` and `Mod.targets` only when `CSII_TOOLPATH` points to them.

## Before each release

- Bump the version in `ShelveIt/ShelveIt.csproj` (`<Version>`), `ShelveIt/UI/mod.json`, `ShelveIt/UI/package.json` and `ShelveIt/Properties/PublishConfiguration.xml` (`ModVersion`), and add an entry to `CHANGELOG.md` and the `ChangeLog` element.
- Add real in-game screenshots to `ShelveIt/Properties/` and enable the `Screenshot` entries in `PublishConfiguration.xml`.
- Build the UI first: `cd ShelveIt/UI && npm run build`. The C# build copies `UI/build` into the deploy folder, and that folder is what gets uploaded.

## Publish

From the `ShelveIt` folder, with the game closed:

```
# first release (creates the mod page)
dotnet publish -c Release /p:PublishProfile=PublishNewMod

# later releases
dotnet publish -c Release /p:PublishProfile=PublishNewVersion

# change only the page text, tags or links (no new build)
dotnet publish -c Release /p:PublishProfile=UpdatePublishedConfiguration
```

The ModPublisher asks you to log in with your own Paradox account; the mod is published under that account. Credentials are entered in the ModPublisher, never stored in this repository.

After the first publish, copy the mod id that Paradox Mods assigns into `<ModId Value="" />` in `PublishConfiguration.xml`. `PublishNewVersion` and `UpdatePublishedConfiguration` need it.

Find It (id 77240) is declared as a required dependency, so Paradox Mods installs it together with Shelve It.
