# translationToChinese

`translationToChinese` is an s&box editor localization library.  
It uses Harmony to patch common editor UI text paths at runtime, then resolves translations from JSON language files.

The library is currently focused on:

- s&box editor UI localization
- Inspector / menus / settings / part of the tooltips
- API title, description, and summary localization

## Project layout

```text
translationToChinese/
  Assets/
    3rd/
      harmony/
        0Harmony.dll
  Code/
    translationtochinese.csproj
  Editor/
    SboxChinesePatch.cs
    SboxChineseDictionary.cs
    SboxTranslationFiles.cs
    translationtochinese.editor.csproj
  Localization/
    settings.json
    ui.zh-CN.json
    api.json
    api.full.zh-CN.json
  UnitTests/
  translationtochinese.sbproj
```

### What each part does

- `Assets/3rd/harmony`
  - Harmony dependency used by the editor patch layer.

- `Editor/SboxChinesePatch.cs`
  - Harmony patch entry point.
  - Installs UI hooks for menus, buttons, native Qt text setters, inspector metadata getters, and the `Translation` menu.

- `Editor/SboxChineseDictionary.cs`
  - Runtime translation lookup and missing-text collection.

- `Editor/SboxTranslationFiles.cs`
  - Loads language files.
  - Supports project-root files, `Libraries/translationToChinese`, and output-directory fallbacks.

- `Localization/settings.json`
  - Current language, fallback language, and missing-text collection settings.

- `Localization/ui.zh-CN.json`
  - General UI translations.
  - Best for menus, buttons, panel titles, tool descriptions, and short strings.

- `Localization/api.json`
  - Source API document used as the alignment baseline.

- `Localization/api.full.zh-CN.json`
  - Full translated API document.
  - Best for `Name`, `Title`, `Description`, `Summary`, and other documentation-oriented fields.

## How to use it

## 1. Add it to another project

The safest approach is to copy the whole library folder into the target project:

```text
YourProject/
  Libraries/
    translationToChinese/
```

At minimum, keep:

- `Assets/3rd/harmony`
- `Editor`
- `Localization`
- `translationtochinese.sbproj`

Then make sure the target editor project references:

- `Libraries/translationToChinese/Editor/translationtochinese.editor.csproj`

If you cloned a working template project, this is often already wired up.

## 2. Start the editor and verify

After opening the project in s&box, you should see a:

- `Translation`

menu in the editor menu bar.

Useful commands include:

- `Apply Chinese Patch`
- `Reload Translation Files`
- `Print Missing Texts`
- `Clear Missing Texts`
- `Restart Editor UI`

Suggested first-run validation:

1. Open the project
2. Click `Translation -> Apply Chinese Patch`
3. Click `Translation -> Reload Translation Files`
4. If needed, click `Translation -> Restart Editor UI`

When translation files are loaded successfully, the console usually prints something like:

```text
SboxTranslationFiles Loaded translations. Current=zh-CN, Fallback=en, UI=..., API=...
```

## 3. Translation file search order

To support both per-project overrides and self-contained library defaults, the loader checks translation files in this order:

1. `ProjectRoot/Localization`
2. `ProjectRoot/Libraries/translationToChinese/Localization`
3. `AssemblyDirectory/translationtochinese/Localization`
4. `AssemblyDirectory/.vs/output/translationtochinese/Localization`

Harmony is resolved in a similar way:

1. `ProjectRoot/Assets/3rd/harmony/0Harmony.dll`
2. `ProjectRoot/Libraries/translationToChinese/Assets/3rd/harmony/0Harmony.dll`
3. output-directory and `.vs/output` fallbacks

This is intentional: in library mode, s&box does not always run with the project root as the active base directory.

## Adding or updating another language

The current structure supports multiple languages.

For example, to add Japanese:

1. Update `Localization/settings.json`:

```json
{
  "CurrentLanguage": "ja",
  "FallbackLanguage": "en",
  "CollectMissingTexts": true
}
```

2. Add a UI dictionary:

```text
Localization/ui.ja.json
```

3. Add a translated API file:

```text
Localization/api.full.ja.json
```

4. Keep the original `api.json` as the alignment source

5. In the editor, click:

- `Translation -> Reload Translation Files`

and, if needed:

- `Translation -> Restart Editor UI`

### Recommended split

- `ui.<locale>.json`
  - short UI text
  - menus
  - panel titles
  - common tool descriptions

- `api.full.<locale>.json`
  - API documentation layer
  - property names
  - type display names
  - descriptions, summaries, returns, and parameter documentation

## How to add missing translations

## 1. Collect missing text

In the editor:

1. Click `Translation -> Clear Missing Texts`
2. Open and use the UI you want to cover
3. Click `Translation -> Print Missing Texts`

The console will print entries like:

```text
SboxChinesePatch [missing] Some Text
```

## 2. Decide where to add it

Use this rule of thumb:

- general UI strings -> `ui.zh-CN.json`
- API / Inspector / type metadata / property docs -> `api.full.zh-CN.json`

### Typical cases

Add to `ui.zh-CN.json`:

- buttons
- menus
- panel titles
- tool descriptions
- item labels

Add to `api.full.zh-CN.json`:

- property names
- component names
- `Name`
- `Title`
- `Description`
- `Summary`
- `Return`
- `Params`

## 3. Reload and verify

After changing the JSON files:

1. `Translation -> Reload Translation Files`
2. If needed, `Translation -> Restart Editor UI`

## Harmony usage

This project uses Harmony.

Harmony is used to:

- patch s&box editor text paths at runtime
- localize the editor without modifying engine source code

### Will it affect other projects?

Short answer: **it normally does not modify other project files, but it does affect the current editor process.**

More precisely:

- Harmony patches are process-local, not file modifications
- the library does not rewrite source files or assets on disk
- it does patch methods inside the currently running s&box editor process

That means:

- when the library is active in the current project, editor UI text paths are intercepted
- if another editor plugin in the same process patches the same methods, conflicts are possible
- those conflicts usually show up as:
  - text not changing
  - text being overwritten by another patch
  - duplicate or inconsistent translations

### Risk assessment

Overall risk is manageable, but not zero.

Main risk areas:

1. the patch targets are broad
   - button text
   - menu text
   - native Qt setters
   - metadata getters

2. Harmony works at process scope
   - not at one isolated panel scope

3. s&box updates can change method signatures
   - which can make some patches stop applying

### What this library already does to reduce impact

The current implementation tries to keep that risk contained by:

- using its own Harmony ID
- unpatching its previous patches before reinstalling
- preferring project-local translation files over library defaults
- falling back to the original text when there is no translation

### Practical guidance

If you reuse this library across multiple projects:

1. keep one shared code path rather than multiple drifting forks
2. when text behaves oddly, first check whether another plugin patches the same UI methods
3. after s&box updates, verify:
   - Harmony still loads
   - `Loaded translations...` still appears
   - key menus and inspector paths still translate

## Moving to another machine or project

The editor project currently depends on local s&box installation paths and output paths.  
If you move the library to another machine, verify:

- `Editor/translationtochinese.editor.csproj`

still points at the correct local s&box installation, for example:

- `D:/Game/steams/steamapps/common/sbox/...`

If the s&box install path is different on the new machine, update those references or regenerate the project accordingly.

## Suggested maintenance workflow

The easiest way to keep this library healthy is:

1. improve coverage
   - collect missing strings from real editor usage

2. update language files
   - prefer editing JSON over editing code

3. only patch code when needed
   - add new Harmony hooks only when a text path never reaches the translator

This keeps day-to-day maintenance data-driven and reduces the amount of code you need to carry forward.
