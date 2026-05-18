# Localization (Hyperion Minecraft Launcher)

This folder holds every user-facing string the launcher renders. The canonical
English source is `Strings.resx`; localized cultures land as `Strings.{culture}.resx`
next to it.

## Where new strings go

1. Add the entry to `Strings.resx` with an UpperCamelCase dotted key (see below).
2. Add a one-line property to `Strings.Designer.cs` that wraps `L("Section.Key")`.
3. Reference it from AXAML as `{x:Static loc:Strings.Section_Key}` (the dot in the
   resx key becomes an underscore in the C# property name).
4. From C# code, prefer either `Strings.Section_Key` directly (for top-level static
   reads) or the runtime `ILocalizationService` indexer / `Get` method (for the
   handful of places that need to re-render after a culture switch).

Do NOT bake user-visible text into `.cs` or `.axaml` files anymore. Even a one-word
button label has a slot in `Strings.resx`.

## Naming convention for keys

`Section.Subsection.Name` -> property `Section_Subsection_Name`.

Use the page or dialog name as the first segment so the file groups naturally:

```
MainWindow.Title
Sidebar.Home
Sidebar.Installations
NewInstanceDialog.Title
NewInstanceDialog.CreateButton
Settings.MemoryHeader
Settings.MinimumXmsLabel
Log.SignedInAs            <- ViewModel log lines start with "Log."
Error.NoSession           <- ViewModel error lines start with "Error."
Common.Cancel             <- Shared across dialogs - one entry, reused
```

Format strings use positional placeholders `{0}`, `{1}`, ...; never named
placeholders. `Log.SignedInAs` is `"Signed in as '{0}'."`, called from C# as
`Strings.Log_SignedInAs` formatted through `string.Format(...)` (or, when the call
site has the localization service available, via `loc.Get("Log.SignedInAs", name)`).

## How to add a new culture

1. Copy `Strings.resx` -> `Strings.{culture}.resx` (e.g. `Strings.ru.resx`,
   `Strings.fr-CA.resx`). Use the lowercase BCP 47 tag.
2. Replace every English value with the translation. Keep the `<data name="...">`
   keys identical - any key you leave out automatically falls back to the English
   source at runtime, so partial translations are fine and never crash the UI.
3. Add the new culture tag to the `AvailableCultures` list passed into
   `ResxLocalizationService` (currently a single-line literal in `App.axaml.cs`).
   The Settings page dropdown reads that list and shows one row per culture, using
   the culture's `NativeName` as the row label.
4. Build. The .NET SDK auto-discovers `Strings.*.resx` and packs them into the
   `HyperionMinecraftLauncher.resources.dll` satellite assembly under the
   matching `runtimes/{culture}/` folder; no csproj edits required.

## Switching language at runtime

The launcher exposes an `ILocalizationService` (see
`Core/Localization/ILocalizationService.cs`). The Settings page dropdown calls
`SetCultureAsync`, which fires `LanguageChanged` and `INotifyPropertyChanged` for
the indexer property name `"Item[]"`.

Plain `{x:Static loc:Strings.X}` bindings (the bulk of the launcher) cache the
English value at startup and only refresh on a full launcher restart. Bindings that
need live-switching write `{Binding [Section.Key], Source={x:Static loc:LocalizationService.Default}}`
against an `INotifyPropertyChanged` instance of the service; the Settings page UI
itself can be upgraded to that pattern in a follow-up if live switching becomes a
hard requirement.

The "Use system default" option (the null first entry in the dropdown) maps to
`CultureInfo.CurrentUICulture` at startup - i.e. the OS display language.

## House rule: no em-dashes, no `---` separators

Per the project author's preference, translation files MUST NOT contain em-dashes
(`U+2014`) or triple-hyphen horizontal-rule separators (`---`). These are tells of
machine-generated copy. Use a plain hyphen + space (` - `) or a comma instead.
This rule applies equally to:

- Every `<value>` in every `Strings.*.resx` file.
- Every Markdown file in the project (READMEs, CHANGELOG, this file).
- Every AXAML literal that isn't already extracted into a resource entry.

The rule is enforced by review, not by tooling. If a translator hands you a file
with em-dashes, replace them before merging.

## Strings intentionally left as English

A small set of strings stays as literals in source because they are technical
identifiers, not user copy:

- Mod-loader names: `Fabric`, `Forge`, `Quilt`, `NeoForge`, `Vanilla` (treated as
  brand names; ship as-is in every locale).
- Source brands: `Modrinth`, `CurseForge`.
- JVM flag fragments: `-Xms`, `-Xmx`, `-XX:+UseG1GC`, etc.
- File / path tokens: `.minecraft`, `servers.dat`, `launcher_profiles.json`,
  `%LOCALAPPDATA%\...`, version numbers like `v0.31.0`.
- AXAML control tags / state names: `IsAutoImported`, `None`, etc.
- Format-string fragments used inside Avalonia `StringFormat` bindings (e.g.
  `'{}{0:N0} downloads'`) - these are markup-level format specifiers, not copy.

If you find a string that should be localized but isn't yet, add it to
`Strings.resx`, wire it through `Strings.Designer.cs`, and replace the literal.
