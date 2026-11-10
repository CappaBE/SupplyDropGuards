# Supply Drop Guards

A Rust server plugin for the **Oxide / uMod** framework. This plugin enhances server event gameplay by spawning armed Scientist NPCs to guard supply drops (airdrops) immediately upon landing. The supply drop remains securely locked until all guarding NPCs are defeated.

## Features

- **Automated Event Detection:** Listens to the `OnSupplyDropLanded` hook to activate immediately when an airdrop hits the ground.
- **Dynamic Guard Spawning:** Spawns a customizable number of scientists in a calculated radius around the exact landing position.
- **Secure Event Locking:** Locks the supply drop container. Players attempting to loot early will receive a private warning notification.
- **Global Chat Announcements:** Sends server-wide global chat broadcasts when a supply drop lands (locked status) and when it is successfully cleared (unlocked status).
- **Graceful Unloading:** Automatically unlocks any active supply drops if the plugin is unloaded or reloaded, ensuring player loot is never permanently inaccessible.
- **Full Configuration:** Generates a dynamic JSON configuration file to adjust values without recompiling.

## Installation

1. Make sure your Rust server has [uMod / Oxide](https://umod.org/) installed.
2. Download or copy the `SupplyDropGuards.cs` file.
3. Drop the file into your server's `oxide/plugins/` directory.
4. The plugin will compile automatically.

## Configuration

After the initial load, a configuration file will be generated at `oxide/config/SupplyDropGuards.json`. You can modify the text, colors, NPC prefabs, and difficulty metrics here:

```json
{
  "GuardCount": 4,
  "SpawnRadius": 5.0,
  "ScientistPrefab": "assets/prefabs/npc/scientist/scientist_full_any.prefab",
  "ChatPrefix": "<color=#orange>[Airdrop Security]</color> ",
  "NotificationMessage": "<color=#red>A supply drop has landed and is locked!</color> Defeat the scientists to claim the loot.",
  "UnlockedMessage": "<color=#green>All guards have been defeated!</color> The supply drop is now unlocked.",
  "LockedWarningMessage": "You must defeat all scientists guarding the crate first!"
}
```

## Requirements

- **Oxide or uMod Framework**
- Navmesh must be enabled on your Rust server for the default AI to navigate and attack properly.

## License

This project is open-source and free to use.
