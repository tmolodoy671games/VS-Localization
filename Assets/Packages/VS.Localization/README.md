# VS.Localization

## Install via Git URL

Unity → Window → Package Manager → **+ → Install package from Git URL**:

```text
https://github.com/tmolodoy671games/VS-Localization.git?path=Assets/Packages/VS.Localization
```

For editable sources, clone this repository and open the `VS.Localization` Unity
project. The entire package is in `Assets/Packages/VS.Localization`. Edit the
Roslyn generator in `Source~` and rebuild with the commands below.

Unity UPM package and Roslyn incremental source generator for YAML localization.
C# is constructed with Roslyn `SyntaxFactory` nodes (not source string templates)
and emitted via `AddSource`. Generated types are `internal class` types with a locale constructor, string properties,
and strongly typed methods. No runtime dependency, YAML loading, or dictionary lookup.

## Catalog

`Assets/Localization/Strings.yaml`:

```yaml
stringName:
  en: English translation
  ru: Русский перевод
stringName2:
  en: "{arg1:int} items for {arg2}"
  ru: "Для {arg2}: {arg1} предметов"
```

```csharp
var strings = new Strings("ru");
string text = strings.StringName;
string formatted = strings.StringName2(arg1: 5, arg2: "Анна");
// Для Анна: 5 предметов
```

API (simplified):

```csharp
internal class Strings
{
    public Strings(string locale) { /* ... */ }
    public string StringName { get { /* ... */ } }
    public string StringName2(int arg1, string arg2) { /* ... */ }
}
```

`{name}` means `string name`; `{count:int}` declares an `int`. Supported types:
`string`, `int`, `long`, `float`, `double`, `decimal`, `bool`. Declare types in the
fallback translation; other languages can omit them or repeat the same type.
Parameter order follows first occurrence in the fallback translation. Named C#
arguments work normally. Reordering/repeating placeholders is allowed; every
translation must use the same parameter set. Conflicting types are compiler errors.
Numbers use invariant formatting. `null` strings become empty strings.
`{{` and `}}` produce literal braces. Formatting specifiers, plurals and ICU are
not implemented.

`en` is the fallback when present; otherwise the first language is the fallback.
Every entry needs that translation. Locale matching ignores case, accepts `_`, and
tries regional parents (`ru-RU` → `ru`) before fallback. Missing translations use
the fallback. `null`, empty and unknown locales also use fallback. The resolved
locale stays private. Create a new instance to change locale. Generated translation
properties and methods use switch expressions (C# 8 or newer).

## Install in Unity

Package Manager → Install package from Git URL:

```
https://github.com/tmolodoy671games/VS-Localization.git?path=Assets/Packages/VS.Localization
```

Or install `Assets/Packages/VS.Localization/package.json` from disk.
The project folder is `VS.Localization`. Tested with Unity 7000.0.0a12; the
compiler plugin targets .NET Standard 2.0 and Roslyn 4.3 (Unity 2022.3+).

Unity recognizes `Strings.yaml` and `*.loc.yaml` in Assets. `Menu.loc.yaml`
generates `Menu`; `welcome_back` generates `WelcomeBack`. Type/member collisions
are reported as errors. Generated classes live in the global namespace and are
internal to the assembly containing the YAML. Put catalogs beside scripts using
them, under the same asmdef (or in Assembly-CSharp).

The package includes `VS.Localization.Generator.dll` at its root with the
RoslynAnalyzer label and disabled runtime compatibility. The same DLL works
in an editable Assets folder or when installed by UPM. It automatically adds the original YAML
paths to a managed block in the owning assembly's `csc.rsp` (`-additionalfile`).
There are **no `.additionalfile` assets or YAML copies**. Existing compiler options
outside that block are preserved. A content hash in a compilation symbol ensures
YAML-only edits rebuild the assembly, including Unity versions that don't track
these files as dependencies. On deletion/move, obsolete entries are removed.
Assembly-binding symbols keep catalogs isolated even when Unity applies a root response file globally. Use an asmdef for Editor/firstpass catalogs. Version the analyzer and `csc.rsp`
with their meta files so CI/IDE compilation has the same inputs.
Manual repair: **Tools → VS.Localization → Refresh Compiler Inputs**.

Roslyn calls `AddSource("Strings.g.cs", ...)`. This is a virtual compiler source;
no `Strings.g.cs` is written in Assets. An IDE can display that virtual file.
Invalid catalogs report `VSL001`, duplicate types `VSL002`. Invalid inputs never
silently retain old generated C#. A build with stale bridge inputs is stopped so
you can wait for recompilation and retry.

The previous Fast Localization importer and its physical `Strings.g.cs` have
been removed in this project. When migrating another project, remove that old
package and its generated C# before installing this one.

## YAML syntax

The parser supports the two-level mapping shown above, with exactly two spaces
before locale keys. Strings can be unquoted, single-quoted (`''` escapes an
apostrophe), or double-quoted (`\n`, `\r`, `\t`, `\\`, `\"`). Empty text must be
`""`. Comments and UTF-8 text are supported. Keys/parameter names are ASCII C#
identifiers; use quotes for values containing YAML punctuation. Unsupported YAML
features (block scalars, anchors, tags, sequences, multiline scalars) produce an
error instead of being interpreted silently. Use `\n` for multiline text.

Optional YAML Language Server schema:

```yaml
# yaml-language-server: $schema=../Packages/VS.Localization/Schema/strings.schema.json
```

This project also includes `.vscode/settings.json` with schema associations for
`Strings.yaml` and `*.loc.yaml` (requires the YAML extension). Rider can use the
schema header or a JSON Schema mapping. For Git-installed packages use the
schema URL instead of a local relative path:
`https://raw.githubusercontent.com/tmolodoy671games/VS-Localization/main/Assets/Packages/VS.Localization/Schema/strings.schema.json`.

## Plain .NET

Reference the generator DLL as an analyzer and YAML as additional files:

```xml
<ItemGroup>
  <Analyzer Include="path/to/VS.Localization.Generator.dll" />
  <AdditionalFiles Include="Localization/Strings.yaml" />
</ItemGroup>
```

No Unity bridge is needed. All YAML/YML additional files are interpreted as
catalogs; filenames set the generated class name.

## Build and test

Requires .NET 10 SDK for the test runner (the shipped analyzer is netstandard2.0):

```sh
dotnet run --project Assets/Packages/VS.Localization/Tests~/VS.Localization.Generator.Tests -c Release
dotnet build Assets/Packages/VS.Localization/Source~/VS.Localization.Generator -c Release
python3 Assets/Packages/VS.Localization/Tools~/package_generator.py
```

The test runner (no test-framework dependency) executes Roslyn, compiles generated code, and
runs it. It covers named/typed arguments, locale fallback,
escaping, invalid catalogs, duplicate types and incremental YAML edits.
`Source~`, `Tests~`, `Tools~` are excluded from Unity asset import. The UPM package
includes a prebuilt compiler DLL; consumers do not need to build it.

Unity uses a compiler response file to pass the original YAML to Roslyn.
The generator uses Roslyn's
[AdditionalTextsProvider](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.incrementalgeneratorinitializationcontext.additionaltextsprovider).

Unity verification fixtures are kept under `Tests~/Unity` and excluded from normal
asset import. Integration verification covered the editable Assets package, a
UPM file install in a clean temporary project, assembly isolation, and YAML-only
translation changes followed by automatic recompilation.
