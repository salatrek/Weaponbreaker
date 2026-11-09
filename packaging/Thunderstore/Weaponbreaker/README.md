# Weaponbreaker

Turn a well-timed parry into a chance to knock an enemy's weapon to the ground. Enemies keep fighting unarmed, return for their weapon after a cooldown, and resume armed combat when they retrieve it.

## Features

- Disarm supported melee weapon variants of Skeletons, Draugr, Fulings, Charred and Dvergr.
- Pick up enemy weapons manually; automatic pickup is disabled for disarmed drops.
- Enemies remember their own dropped weapon and can return for it during combat.
- If you take their weapon, they keep fighting unarmed. They do not retrieve your dropped weapons.
- Unarmed fallback attacks retain the original attack behavior with 15% damage; retrieving the weapon restores the original combat parameters and full damage.
- Optional reverse disarming: supported enemies can knock your active weapon from your hands when they reduce your health.
- A successful shield block or parry protects the weapon in your other hand, even if some damage gets through. A failed shield block does not. Blocking or parrying with a weapon still allows disarming if you take damage.
- Per-enemy chances, cooldown settings and host configuration synchronization.

## Chances and skills

The default enemy weapon disarm chance is 30%. Only a successful timed parry in the vanilla 0.25-second window qualifies; holding a regular block does not disarm enemies. The item must support timed parries.

At skill level 25:

| Action | Enemy disarm chance |
|---|---:|
| Shield parry | 7.5% |
| One-handed weapon parry | 30% |
| Two-handed weapon parry | 37.5% |
| Qualifying sneak bow hit | 60% |

Shield parries use Blocking skill; weapons use their respective skill. Offensive skill multipliers range from 0.75 at level 0 to 1 at level 25 and 1.25 at level 100. Higher weapon skill also reduces your own risk of losing that weapon. Final probabilities are limited to 0–100%.

A qualifying bow hit requires sneaking, a bow projectile, actual health loss and a hit carrying the game's backstab bonus. Parrying an arrow does not disarm the archer.

Default cooldowns: 8 seconds before disarming the same enemy again, 8 seconds before an enemy can retrieve its weapon, and 15 seconds between player disarms. A previous combat target is restored after retrieval only if alive and within 30 metres.

## Installation

Install with Gale or another Thunderstore-compatible manager. BepInExPack Valheim is the only required mod dependency; the JSON runtime libraries are included. MyEmotions is not required.

For manual installation, install BepInEx for Valheim first, then copy the archive's `plugins` directory into `BepInEx`. The root metadata files do not need to be copied. Remove any older manually installed duplicate of `Weaponbreaker.dll` before using a mod-manager installation.

## Configuration

After the first launch:

- `BepInEx/config/weaponbreakerConfigs.json`: initial settings template.
- `BepInEx/config/salatrek.Weaponbreaker.cfg`: active BepInEx settings. Saved values take priority over the JSON defaults on startup.

Enable or disable each mechanic, set enemy-specific probabilities, and adjust cooldowns. Optional BepInEx Configuration Manager exposes settings in-game. Natural-attack enemies such as boars and leeches expose only their chance to disarm the player; they have no weapon to drop.

In multiplayer, install the mod on the host/server and participating clients. Host settings are intended to govern all clients. Full multiplayer and ownership-transfer testing remains outstanding.

## Limitations and testing

Prepared for the Valheim 1.0.21 / BepInEx 5.4.23.6 environment used during development. The skeleton disarm, unarmed combat, retrieval and repeat-disarm cycle was tested in the author's existing Windows modded profile. The shield protection change has automated logic/IL checks; its in-game validation and installation from this release ZIP on a fresh profile remain outstanding.

Enemy support uses an explicit weapon whitelist; not every creature or weapon variant is supported. Enemy-only equipment may drop as a mapped player-usable equivalent. Return movement can be obstructed by terrain. Interrupted disarm cycles across saving/reloading and network ownership changes are not guaranteed by this initial release.

Only this mod and its runtime libraries are shipped. Personal profile settings and game assemblies are not included. Library licenses are provided under the plugin's `licenses` directory.

## Credits

Created by Salatrek with AI-assisted development. The package icon was generated with AI.
