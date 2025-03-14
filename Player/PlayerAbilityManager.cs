using Unity.Netcode;
using UnityEngine;
using System;
using System.Collections.Generic;
using PlayerAbilities;
using System.Collections;
using System.Linq;

public class PlayerAbilityManager : NetworkBehaviour
{
    [Header("Configuration")]
    [SerializeField] public GameObject clickIndicatorPrefab;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugMessages = true;
    [SerializeField] private float syncInterval = 5f; // Seconds between full syncs
    
    // Active abilities list
    private List<BaseAbility> abilities = new List<BaseAbility>();
    
    // Slot to ability mapping (for UI)
    private Dictionary<int, BaseAbility> abilitySlots = new Dictionary<int, BaseAbility>();
    
    // Component references
    private PlayerStats playerStats;
    private PlayerNetwork playerNetwork;
    
    // State tracking
    public bool isInImpactPause { get; private set; } = false;
    
    // For compatibility with legacy system
    private PlayerAbility legacyAbilityComponent;
    
    private void Awake()
    {
        // Get component references
        playerStats = GetComponent<PlayerStats>();
        playerNetwork = GetComponent<PlayerNetwork>();
        legacyAbilityComponent = GetComponent<PlayerAbility>();
        
        if (playerStats == null)
        {
            Debug.LogWarning("[PlayerAbilityManager] PlayerStats component not found");
        }
        
        if (playerNetwork == null)
        {
            Debug.LogWarning("[PlayerAbilityManager] PlayerNetwork component not found");
        }
        
        // Check for legacy component
        if (legacyAbilityComponent != null && showDebugMessages)
        {
            Debug.Log("[PlayerAbilityManager] Legacy PlayerAbility component found. Will maintain for compatibility.");
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        Debug.Log($"[PlayerAbilityManager] OnNetworkSpawn - IsOwner: {IsOwner}, IsServer: {IsServer}");
        
        // Enable input processing for owner only
        if (IsOwner)
        {
            enabled = true;
            
            // Start client-side sync requests
            if (!IsServer)
            {
                StartCoroutine(RequestSyncCoroutine());
            }
        }
        else if (!IsServer)
        {
            // Disable if not owner or server
            enabled = false;
        }
        
        // Start server-side sync
        if (IsServer)
        {
            StartCoroutine(PeriodicSyncCoroutine());
        }
        
        // Let Hero component handle ability initialization
        Hero hero = GetComponent<Hero>();
        if (hero != null)
        {
            if (showDebugMessages)
            {
                Debug.Log("[PlayerAbilityManager] Deferring ability initialization to Hero component");
            }
        }
        else
        {
            // No Hero component, initialize default abilities later
            StartCoroutine(DelayedDefaultInitialization());
        }
    }
    
    private IEnumerator DelayedDefaultInitialization()
    {
        // Wait for everything to be ready
        yield return new WaitForSeconds(1.0f);
        
        // Only initialize default abilities if none exist
        if (abilities.Count == 0)
        {
            Debug.Log("[PlayerAbilityManager] No abilities found, initializing defaults");
            InitializeDefaultAbilities();
        }
    }
    
    private void InitializeDefaultAbilities()
    {
        if (showDebugMessages)
        {
            Debug.Log("[PlayerAbilityManager] Initializing default abilities");
        }
        
        // Clear any existing abilities
        RemoveAllAbilities();
        
        try
        {
            // Add basic abilities
            DashAbility dashAbility = AddAbility<DashAbility>(0);
            StrongJumpAbility jumpAbility = AddAbility<StrongJumpAbility>(1);
            
            if (dashAbility != null && jumpAbility != null)
            {
                Debug.Log("[PlayerAbilityManager] Default abilities initialized successfully");
            }
            else
            {
                Debug.LogWarning("[PlayerAbilityManager] Could not create default abilities");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[PlayerAbilityManager] Error initializing default abilities: {e.Message}");
        }
    }
    
    private void Update()
    {
        if (!IsOwner) return;
        
        // Process ability activations
        foreach (var ability in abilities)
        {
            if (ability == null) continue;
            
            // Check for activation key press
            if (Input.GetKeyDown(ability.activationKey))
            {
                TryActivateAbility(ability);
            }
            
            // Update ability logic
            try
            {
                ability.UpdateAbility();
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlayerAbilityManager] Error in UpdateAbility for {ability.abilityName}: {e.Message}");
            }
        }
    }
    
    private void TryActivateAbility(BaseAbility ability)
    {
        if (ability == null) return;
        
        try
        {
            if (ability.CanActivate())
            {
                // Send activation request to server
                ActivateAbilityServerRpc(abilities.IndexOf(ability));
            }
            else if (showDebugMessages)
            {
                // Show feedback about why ability can't be used
                if (!ability.isReady)
                {
                    Debug.Log($"Ability {ability.abilityName} on cooldown: {ability.GetRemainingCooldown():F1}s");
                }
                else if (playerStats != null && playerStats.CurrentMana < ability.manaCost)
                {
                    Debug.Log($"Not enough mana for {ability.abilityName}. Need {ability.manaCost}, have {playerStats.CurrentMana:F1}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[PlayerAbilityManager] Error activating {ability.abilityName}: {e.Message}");
        }
    }
    
    [ServerRpc]
    private void ActivateAbilityServerRpc(int abilityIndex)
    {
        if (abilityIndex < 0 || abilityIndex >= abilities.Count)
        {
            Debug.LogError($"[PlayerAbilityManager] Invalid ability index: {abilityIndex}");
            return;
        }
        
        BaseAbility ability = abilities[abilityIndex];
        if (ability == null) return;
        
        // Check mana cost
        if (playerStats != null && ability.manaCost > 0)
        {
            if (playerStats.UseMana(ability.manaCost))
            {
                // Activate on all clients
                ActivateAbilityClientRpc(abilityIndex);
            }
            else if (showDebugMessages)
            {
                Debug.Log($"[PlayerAbilityManager] Not enough mana for {ability.abilityName}");
            }
        }
        else
        {
            // No mana cost or no player stats, just activate
            ActivateAbilityClientRpc(abilityIndex);
        }
    }
    
    [ClientRpc]
    private void ActivateAbilityClientRpc(int abilityIndex)
    {
        if (abilityIndex < 0 || abilityIndex >= abilities.Count)
        {
            Debug.LogError($"[PlayerAbilityManager] ClientRpc: Invalid ability index: {abilityIndex}");
            return;
        }
        
        BaseAbility ability = abilities[abilityIndex];
        if (ability == null) return;
        
        // Activate through networked method
        ability.ActivateNetworked();
        
        // Sync with legacy system if it exists
        SyncLegacySystem();
    }
    
    // Add ability with optional slot assignment
    public T AddAbility<T>(int slot = -1) where T : BaseAbility
    {
        Debug.Log($"[PlayerAbilityManager] Adding ability of type {typeof(T).Name} to slot {slot}");
        
        try
        {
            // Check if this ability type already exists
            foreach (var ability in abilities)
            {
                if (ability is T existingAbility)
                {
                    // Already exists, just update slot if needed
                    if (slot >= 0)
                    {
                        abilitySlots[slot] = ability;
                        Debug.Log($"[PlayerAbilityManager] Updated existing {typeof(T).Name} to slot {slot}");
                    }
                    
                    return (T)ability;
                }
            }
            
            // Create new ability
            T newAbility = gameObject.AddComponent<T>();
            if (newAbility == null)
            {
                Debug.LogError($"[PlayerAbilityManager] Failed to add component of type {typeof(T).Name}");
                return null;
            }
            
            // Initialize
            newAbility.Initialize(this);
            
            // Add to list
            abilities.Add(newAbility);
            
            // Assign to slot if specified
            if (slot >= 0)
            {
                if (abilitySlots.ContainsKey(slot))
                {
                    abilitySlots[slot] = newAbility;
                }
                else
                {
                    abilitySlots.Add(slot, newAbility);
                }
                Debug.Log($"[PlayerAbilityManager] Assigned {typeof(T).Name} to slot {slot}");
            }
            
            // Sync with legacy system
            if (legacyAbilityComponent != null)
            {
                legacyAbilityComponent.RegisterPowerUpAbility(newAbility, slot);
            }
            
            return newAbility;
        }
        catch (Exception e)
        {
            Debug.LogError($"[PlayerAbilityManager] Error adding ability {typeof(T).Name}: {e.Message}");
            return null;
        }
    }
    
    // Add ability by type (for reflection-based usage)
    public BaseAbility AddAbilityByType(Type abilityType, int slot = -1)
    {
        if (abilityType == null || !typeof(BaseAbility).IsAssignableFrom(abilityType))
        {
            Debug.LogError($"[PlayerAbilityManager] Invalid ability type: {abilityType?.Name ?? "null"}");
            return null;
        }
        
        Debug.Log($"[PlayerAbilityManager] Adding ability of type {abilityType.Name} to slot {slot}");
        
        try
        {
            // Check if already exists
            foreach (var ability in abilities)
            {
                if (ability.GetType() == abilityType)
                {
                    // Already exists, just update slot
                    if (slot >= 0)
                    {
                        abilitySlots[slot] = ability;
                        Debug.Log($"[PlayerAbilityManager] Updated existing {abilityType.Name} to slot {slot}");
                    }
                    
                    return ability;
                }
            }
            
            // Create new ability
            BaseAbility newAbility = gameObject.AddComponent(abilityType) as BaseAbility;
            if (newAbility == null)
            {
                Debug.LogError($"[PlayerAbilityManager] Failed to add component of type {abilityType.Name}");
                return null;
            }
            
            // Initialize
            newAbility.Initialize(this);
            
            // Add to list
            abilities.Add(newAbility);
            
            // Assign to slot if specified
            if (slot >= 0)
            {
                if (abilitySlots.ContainsKey(slot))
                {
                    abilitySlots[slot] = newAbility;
                }
                else
                {
                    abilitySlots.Add(slot, newAbility);
                }
                Debug.Log($"[PlayerAbilityManager] Assigned {abilityType.Name} to slot {slot}");
            }
            
            // Sync with legacy system
            if (legacyAbilityComponent != null && slot >= 0 && slot < 4)
            {
                legacyAbilityComponent.RegisterPowerUpAbility(newAbility, slot);
            }
            
            return newAbility;
        }
        catch (Exception e)
        {
            Debug.LogError($"[PlayerAbilityManager] Error adding ability {abilityType.Name}: {e.Message}");
            return null;
        }
    }
    
    // Remove all abilities
    public void RemoveAllAbilities()
    {
        Debug.Log("[PlayerAbilityManager] Removing all abilities");
        
        // Create a copy of the list to avoid modification during iteration
        BaseAbility[] abilitiesToRemove = abilities.ToArray();
        
        foreach (var ability in abilitiesToRemove)
        {
            if (ability != null)
            {
                ability.Cleanup();
                Destroy(ability);
            }
        }
        
        // Clear collections
        abilities.Clear();
        abilitySlots.Clear();
        
        // Clear legacy system if it exists
        if (legacyAbilityComponent != null)
        {
            for (int i = 0; i < 4; i++)
            {
                legacyAbilityComponent.UnregisterPowerUpAbility(i);
            }
        }
    }
    
    // Remove a specific ability by type
    public bool RemoveAbility<T>() where T : BaseAbility
    {
        for (int i = 0; i < abilities.Count; i++)
        {
            if (abilities[i] is T)
            {
                BaseAbility ability = abilities[i];
                abilities.RemoveAt(i);
                
                // Remove from slots
                List<int> slotsToRemove = new List<int>();
                foreach (var pair in abilitySlots)
                {
                    if (pair.Value == ability)
                    {
                        slotsToRemove.Add(pair.Key);
                    }
                }
                
                foreach (int slot in slotsToRemove)
                {
                    abilitySlots.Remove(slot);
                    
                    // Unregister from legacy system
                    if (legacyAbilityComponent != null && slot >= 0 && slot < 4)
                    {
                        legacyAbilityComponent.UnregisterPowerUpAbility(slot);
                    }
                }
                
                // Clean up
                ability.Cleanup();
                Destroy(ability);
                
                return true;
            }
        }
        
        return false;
    }
    
    // Sync with legacy system
    private void SyncLegacySystem()
    {
        if (legacyAbilityComponent == null) return;
        
        // Map abilities to legacy slots
        foreach (var pair in abilitySlots)
        {
            int slot = pair.Key;
            BaseAbility ability = pair.Value;
            
            // Only sync slots 0-3 (legacy system limit)
            if (slot >= 0 && slot < 4 && ability != null)
            {
                legacyAbilityComponent.RegisterPowerUpAbility(ability, slot);
            }
        }
    }
    
    // Set impact pause state
    public void SetInImpactPause(bool pause)
    {
        isInImpactPause = pause;
    }
    
    // Get ability count
    public int GetAbilityCount() => abilities.Count;
    
    // Get ability by index
    public BaseAbility GetAbility(int index)
    {
        if (index >= 0 && index < abilities.Count)
            return abilities[index];
        return null;
    }
    
    // Get ability by slot
    public BaseAbility GetAbilityBySlot(int slot)
    {
        if (abilitySlots.TryGetValue(slot, out BaseAbility ability))
        {
            return ability;
        }
        
        // Fallback: try to map slot to index
        if (slot >= 0 && slot < abilities.Count)
        {
            return abilities[slot];
        }
        
        return null;
    }
    
    // Get ability by type
    public T GetAbilityOfType<T>() where T : BaseAbility
    {
        foreach (var ability in abilities)
        {
            if (ability is T typedAbility)
                return typedAbility;
        }
        return null;
    }
    
    // Check if player has a specific ability
    public bool HasAbility<T>() where T : BaseAbility
    {
        foreach (var ability in abilities)
        {
            if (ability is T)
                return true;
        }
        return false;
    }
    
    // Helper methods for compatibility with PlayerNetwork
    public bool IsMoving()
    {
        if (playerNetwork != null)
        {
            return playerNetwork.IsMoving();
        }
        return false;
    }
    
    public Vector3 GetTargetPosition()
    {
        if (playerNetwork != null)
        {
            return playerNetwork.GetTargetPosition();
        }
        return transform.position;
    }
    
    // Server-side sync coroutine
    private IEnumerator PeriodicSyncCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(syncInterval);
            
            if (IsServer)
            {
                SyncAbilityStates();
            }
        }
    }
    
    // Client-side sync request coroutine
    private IEnumerator RequestSyncCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(syncInterval * 1.5f);
            
            if (IsOwner && !IsServer)
            {
                RequestSyncServerRpc();
            }
        }
    }
    
    // Sync ability states from server to clients
    public void SyncAbilityStates()
    {
        if (!IsServer) return;
        
        // Prepare data for sync
        List<int> indices = new List<int>();
        List<bool> readyStates = new List<bool>();
        List<float> cooldowns = new List<float>();
        
        for (int i = 0; i < abilities.Count; i++)
        {
            BaseAbility ability = abilities[i];
            if (ability != null)
            {
                indices.Add(i);
                readyStates.Add(ability.isReady);
                cooldowns.Add(ability.GetRemainingCooldown());
            }
        }
        
        // Send to clients
        if (indices.Count > 0)
        {
            SyncAbilityStatesClientRpc(indices.ToArray(), readyStates.ToArray(), cooldowns.ToArray());
        }
    }
    
    [ClientRpc]
    private void SyncAbilityStatesClientRpc(int[] indices, bool[] readyStates, float[] cooldowns)
    {
        if (IsServer) return; // Server already has the data
        
        // Apply cooldown states
        for (int i = 0; i < indices.Length; i++)
        {
            int index = indices[i];
            float remainingCooldown = cooldowns[i];
            
            if (index >= 0 && index < abilities.Count)
            {
                BaseAbility ability = abilities[index];
                if (ability != null)
                {
                    ability.SyncCooldown(remainingCooldown);
                }
            }
        }
        
        // Sync with legacy system
        SyncLegacySystem();
    }
    
    [ServerRpc]
    private void RequestSyncServerRpc()
    {
        SyncAbilityStates();
    }
    
    // Reset all abilities
    public void ResetAllAbilities()
    {
        foreach (var ability in abilities)
        {
            if (ability != null)
            {
                ability.Reset();
            }
        }
        
        if (IsServer)
        {
            ResetAllAbilitiesClientRpc();
        }
    }
    
    [ClientRpc]
    private void ResetAllAbilitiesClientRpc()
    {
        if (IsServer) return; // Server already reset abilities
        
        foreach (var ability in abilities)
        {
            if (ability != null)
            {
                ability.Reset();
            }
        }
        
        // Sync with legacy system
        SyncLegacySystem();
    }
    
    // Initialize abilities from ability definitions
    public void InitializeAbilities(List<AbilityDefinition> abilityDefinitions)
    {
        // Remove existing abilities
        RemoveAllAbilities();
        
        if (abilityDefinitions == null || abilityDefinitions.Count == 0)
        {
            Debug.LogWarning("[PlayerAbilityManager] No ability definitions provided");
            return;
        }
        
        // Add each ability from definitions
        for (int i = 0; i < abilityDefinitions.Count; i++)
        {
            var def = abilityDefinitions[i];
            if (def == null || string.IsNullOrEmpty(def.abilityType))
            {
                Debug.LogWarning($"[PlayerAbilityManager] Invalid ability definition at index {i}");
                continue;
            }
            
            // Get ability type
            Type abilityType = Type.GetType(def.abilityType);
            if (abilityType == null || !typeof(BaseAbility).IsAssignableFrom(abilityType))
            {
                Debug.LogError($"[PlayerAbilityManager] Invalid ability type: {def.abilityType}");
                continue;
            }
            
            // Add ability
            BaseAbility ability = AddAbilityByType(abilityType, i);
            if (ability != null)
            {
                // Configure properties
                ability.abilityName = def.abilityName;
                ability.activationKey = def.activationKey;
                ability.manaCost = def.manaCost;
                ability.cooldown = def.cooldown;
                ability.icon = def.icon;
                
                Debug.Log($"[PlayerAbilityManager] Added ability: {ability.abilityName} to slot {i}");
            }
        }
        
        // Sync with legacy system
        SyncLegacySystem();
    }
}