using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Supply Drop Guards", "AI_Assistant", "1.3.0")]
    [Description("Spawns scientists to guard supply drops, locks the crate, and sends in-game chat announcements.")]
    class SupplyDropGuards : RustPlugin
    {
        private Configuration config;
        
        // Dictionary to track which guards (NPCs) belong to which supply drop
        private Dictionary<SupplyDrop, List<ulong>> activeDrops = new Dictionary<SupplyDrop, List<ulong>>();

        // --- Configuration Structure ---
        private class Configuration
        {
            public int GuardCount = 4;
            public float SpawnRadius = 5f;
            public string ScientistPrefab = "assets/prefabs/npc/scientist/scientist_full_any.prefab";
            public string ChatPrefix = "<color=#orange>[Airdrop Security]</color> ";
            public string NotificationMessage = "<color=#red>A supply drop has landed and is locked!</color> Defeat the scientists to claim the loot.";
            public string UnlockedMessage = "<color=#green>All guards have been defeated!</color> The supply drop is now unlocked.";
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
            SaveConfig();
        }

        protected override void LoadDefaultConfig() => config = new Configuration();
        protected override void SaveConfig() => Config.WriteObject(config);

        // --- Core Logic ---

        // Fires when the supply drop hits the ground
        private void OnSupplyDropLanded(SupplyDrop supplyDrop)
        {
            if (supplyDrop == null) return;

            // Lock the crate to prevent players from opening it right away
            supplyDrop.SetFlag(BaseEntity.Flags.Locked, true);

            Vector3 landPosition = supplyDrop.transform.position;
            List<ulong> guardIds = SpawnGuards(landPosition);

            // Store the drop and its specific guards
            activeDrops[supplyDrop] = guardIds;

            // [ANNOUNCEMENT] Broadcast to the entire server chat
            Server.Broadcast($"{config.ChatPrefix}{config.NotificationMessage}");
        }

        private List<ulong> SpawnGuards(Vector3 position)
        {
            List<ulong> spawnedGuards = new List<ulong>();

            for (int i = 0; i < config.GuardCount; i++)
            {
                Vector2 randomCircle = Random.insideUnitCircle * config.SpawnRadius;
                Vector3 spawnPos = new Vector3(position.x + randomCircle.x, position.y + 0.5f, position.z + randomCircle.y);
                spawnPos.y = TerrainMeta.HeightMap.GetHeight(spawnPos);

                BaseEntity entity = GameManager.server.CreateEntity(config.ScientistPrefab, spawnPos, Quaternion.identity);
                if (entity == null) continue;

                entity.Spawn();
                spawnedGuards.Add(entity.net.ID.Value);
            }

            return spawnedGuards;
        }

        // Blocks looting while the Locked flag is active
        private object CanLootEntity(BasePlayer player, SupplyDrop supplyDrop)
        {
            if (supplyDrop != null && supplyDrop.HasFlag(BaseEntity.Flags.Locked))
            {
                // Send a private chat message to the player attempting to loot
                Player.Message(player, $"{config.ChatPrefix}{config.LockedWarningMessage}", 0, Chat.ChatChannel.Global);
                return false; 
            }
            return null;
        }

        // Fires when an entity dies
        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || entity.net == null) return;

            ulong deadGuardId = entity.net.ID.Value;
            SupplyDrop dropToUnlock = null;

            // Check if the dead NPC was a guard for an active drop
            foreach (var kvp in activeDrops)
            {
                if (kvp.Value.Contains(deadGuardId))
                {
                    kvp.Value.Remove(deadGuardId);
                    
                    if (kvp.Value.Count == 0)
                    {
                        dropToUnlock = kvp.Key;
                    }
                    break;
                }
            }

            if (dropToUnlock != null)
            {
                UnlockSupplyDrop(dropToUnlock);
            }
        }

        private void UnlockSupplyDrop(SupplyDrop supplyDrop)
        {
            if (supplyDrop == null) return;

            // Remove the lock flag
            supplyDrop.SetFlag(BaseEntity.Flags.Locked, false);
            activeDrops.Remove(supplyDrop);

            // [ANNOUNCEMENT] Broadcast the unlock message to the server chat
            Server.Broadcast($"{config.ChatPrefix}{config.UnlockedMessage}");
        }

        // Cleanup on plugin unload/reload
        private void Unload()
        {
            foreach (var supplyDrop in activeDrops.Keys)
            {
                if (supplyDrop != null)
                {
                    supplyDrop.SetFlag(BaseEntity.Flags.Locked, false);
                }
            }
            activeDrops.Clear();
        }
    }
}
