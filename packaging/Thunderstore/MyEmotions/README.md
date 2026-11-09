# MyEmotions

Your Viking reacts to weather, danger and everyday adventures with situational overhead dialogue. The default phrases are in Russian and contain strong language.

## Features

- Reactions to rain, fog, cold, wetness, sailing, equipment, cooking, food, combat, portals and other supported situations.
- Phrases appear above your local character without filling the normal chat history.
- Customizable phrases and per-situation cooldowns, plus a global phrase cooldown.
- Context placeholders, such as `{enemy}`, in supported situations.
- Avoids immediately repeating the same phrase when alternatives are available.

## Installation

Install with Gale or another Thunderstore-compatible manager. BepInExPack Valheim is the only required mod dependency; the JSON runtime libraries are included.

For manual installation, install BepInEx for Valheim first, then copy the archive's `plugins` directory into `BepInEx`. The root metadata files do not need to be copied.

MyEmotions works independently of Weaponbreaker. Do not keep a second manually installed copy of `MyEmotions.dll` when switching to a mod-manager installation.

## Configuration

Launch the game once. Default files are created automatically:

- `BepInEx/config/emotionsConfigs.json`: initial phrase template and settings.
- `BepInEx/config/salatrek.MyEmotions.cfg`: BepInEx settings, including editable phrases and cooldowns.

For an in-game settings menu, optionally install a compatible BepInEx Configuration Manager. It is not required for the mod to run.

The default global phrase cooldown is 7 seconds; individual situations have their own additional cooldowns. Customize the text and cooldown for each situation to suit your character.

## Language and content

Default dialogue is Russian and includes profanity. Replace the phrases in the settings to change the language or remove strong language. This release does not include an English phrase pack.

## Compatibility and testing

Prepared for the Valheim 1.0.21 / BepInEx 5.4.23.6 environment used during development. Tested in the author's existing Windows modded profile. Installation from this release ZIP on a fresh profile has not yet been tested in-game. No claim of dedicated-server or cross-platform testing is made.

Only this mod and its runtime libraries are shipped. Personal profile settings and game assemblies are not included. Library licenses are provided under the plugin's `licenses` directory.

## Credits

Created by Salatrek with AI-assisted development. The package icon was generated with AI.
