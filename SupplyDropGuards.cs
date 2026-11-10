using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Supply Drop Guards", "AI_Assistant", "1.5.0")]
    [Description("Spawns scientists to guard supply drops, blocks looting until they are dead, and sends in-game chat announcements.")]
    class SupplyDropGuards : RustPlugin
    {
        private Configuration config;

        // Locked drops (by net ID) -> remaining guard net IDs
        private readonly Dictionary<ulong, HashSet<ulong>> activeDrops = new Dictionary<ulong, HashSet<ulong>>();

        // Every guard this plugin has spawned (used by the accuracy hook)
        private readonly HashSet<ulong> guardIds = new HashSet<ulong>();

        private class Configuration
        {
            // Read-only notes. Rewritten on every load; the plugin never reads values from here.
            public Dictionary<string, string> Info = new Dictionary<string, string>
            {
                ["GuardCount"] = "Scientists spawned per drop. Default: 4",
                ["SpawnRadius"] = "Meters around the crate where guards spawn. Default: 5",
                ["ScientistPrefab"] = "Prefab used for guards. Default: scientistnpc_roam. If nothing spawns, check the path for your Rust version",
                ["GuardHitChance"] = "0-1, chance a guard shot deals damage up close. Default: 0.5",
                ["GuardHitChanceFar"] = "0-1, chance a guard shot deals damage at FalloffDistance or farther. Default: 0.15",
                ["FalloffDistance"] = "Meters over which the hit chance drops from close to far. Default: 40",
                ["GuardDamageScale"] = "Damage multiplier for shots that hit (0.7 = 70%). Default: 0.7",
                ["ChatPrefix / messages"] = "Chat text. Supports <color=#rrggbb> tags"
            };

            public int GuardCount = 4;
            public float SpawnRadius = 5f;
            public string ScientistPrefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam.prefab";

            // --- Guard accuracy ("anti-aimbot") settings ---
            // Chance (0 to 1) that a shot deals damage when the player is close to the guard
            public float GuardHitChance = 0.5f;
            // Chance (0 to 1) that a shot deals damage when the player is at or beyond FalloffDistance
            public float GuardHitChanceFar = 0.15f;
            // Distance in meters at which the hit chance has fully dropped to GuardHitChanceFar
            public float FalloffDistance = 40f;
            // Damage multiplier for shots that do hit (0.7 = 70% damage)
            public float GuardDamageScale = 0.7f;

            public string ChatPrefix = "<color=#ffa500>[Airdrop Security]</color> ";
            public string NotificationMessage = "<color=#ff4444>A supply drop has landed and is locked!</color> Defeat the scientists to claim the loot.";
            public string UnlockedMessage = "<color=#55ff55>All guards have been defeated!</color> The supply drop is now unlocked.";
            public string LockedWarningMessage = "You must defeat all scientists guarding the crate first!";
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) LoadDefaultConfig();
            }
            catch
            {
                PrintError("Configuration file is corrupt, loading default values.");
                LoadDefaultConfig();
            }
            config.Info = new Configuration().Info; // keep the notes current
            SaveConfig();
        }

        protected override void LoadDefaultConfig() => config = new Configuration();
        protected override void SaveConfig() => Config.WriteObject(config);

        // --- Core Logic ---

        private void OnSupplyDropLanded(SupplyDrop supplyDrop)
        {
            if (supplyDrop == null || supplyDrop.net == null) return;

            HashSet<ulong> spawned = SpawnGuards(supplyDrop.transform.position);
            if (spawned.Count == 0) return; // nothing spawned, so don't lock the crate

            activeDrops[supplyDrop.net.ID.Value] = spawned;
            Server.Broadcast($"{config.ChatPrefix}{config.NotificationMessage}");
        }

        private HashSet<ulong> SpawnGuards(Vector3 position)
        {
            var spawned = new HashSet<ulong>();

            for (int i = 0; i < config.GuardCount; i++)
            {
                Vector2 circle = UnityEngine.Random.insideUnitCircle * config.SpawnRadius;
                Vector3 spawnPos = new Vector3(position.x + circle.x, position.y, position.z + circle.y);
                spawnPos.y = TerrainMeta.HeightMap.GetHeight(spawnPos);

                BaseEntity entity = GameManager.server.CreateEntity(config.ScientistPrefab, spawnPos, Quaternion.identity);
                if (entity == null) continue;

                entity.Spawn();
                if (entity.net != null)
                {
                    spawned.Add(entity.net.ID.Value);
                    guardIds.Add(entity.net.ID.Value);
                }
            }

            return spawned;
        }

        // Blocks looting while the drop is in activeDrops
        private object CanLootEntity(BasePlayer player, SupplyDrop supplyDrop)
        {
            if (player == null || supplyDrop == null || supplyDrop.net == null) return null;

            if (activeDrops.ContainsKey(supplyDrop.net.ID.Value))
            {
                player.ChatMessage($"{config.ChatPrefix}{config.LockedWarningMessage}");
                return false;
            }
            return null;
        }

        // Makes guards miss more often at range and hit softer
        private object OnEntityTakeDamage(BasePlayer victim, HitInfo info)
        {
            if (victim == null || info?.Initiator == null || info.Initiator.net == null) return null;
            if (!guardIds.Contains(info.Initiator.net.ID.Value)) return null;

            // Guards shouldn't be affected when hitting other NPCs
            if (victim.IsNpc) return null;

            float distance = Vector3.Distance(info.Initiator.transform.position, victim.transform.position);
            float t = Mathf.Clamp01(distance / Mathf.Max(config.FalloffDistance, 0.01f));
            float hitChance = Mathf.Lerp(config.GuardHitChance, config.GuardHitChanceFar, t);

            // Miss: cancel the damage entirely
            if (UnityEngine.Random.value > hitChance) return true;

            // Hit: reduce damage
            info.damageTypes.ScaleAll(config.GuardDamageScale);
            return null;
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || entity.net == null) return;

            ulong deadId = entity.net.ID.Value;
            guardIds.Remove(deadId);

            ulong dropToUnlock = 0;

            foreach (var kvp in activeDrops)
            {
                if (kvp.Value.Remove(deadId))
                {
                    if (kvp.Value.Count == 0) dropToUnlock = kvp.Key;
                    break;
                }
            }

            if (dropToUnlock != 0)
            {
                activeDrops.Remove(dropToUnlock);
                Server.Broadcast($"{config.ChatPrefix}{config.UnlockedMessage}");
            }
        }

        // Drop despawned/looted: stop tracking it
        private void OnEntityKill(SupplyDrop supplyDrop)
        {
            if (supplyDrop?.net != null) activeDrops.Remove(supplyDrop.net.ID.Value);
        }

        private void Unload()
        {
            activeDrops.Clear();
            guardIds.Clear();
        }
    }
}