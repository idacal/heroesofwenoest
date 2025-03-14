using Unity.Netcode;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using PlayerAbilities;
using System;

// Base class for all heroes
public class Hero : NetworkBehaviour
{
    [Header("Hero Information")]
    public string heroName = "Default Hero";
    public HeroClass heroClass;
    
    [Header("Visual")]
    [SerializeField] private GameObject heroModel;
    
    // Network variables
    private NetworkVariable<int> heroLevel = new NetworkVariable<int>(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Component references
    protected PlayerNetwork playerNetwork;
    protected PlayerStats playerStats;
    protected PlayerAbilityManager abilityManager;
    protected PlayerCombat playerCombat;
    
    protected virtual void Awake()
    {
        // Get references to components
        playerNetwork = GetComponent<PlayerNetwork>();
        playerStats = GetComponent<PlayerStats>();
        abilityManager = GetComponent<PlayerAbilityManager>();
        playerCombat = GetComponent<PlayerCombat>();
        
        if (abilityManager == null)
        {
            Debug.LogWarning($"[Hero] No PlayerAbilityManager found on {gameObject.name}. Some functionality may be unavailable.");
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        Debug.Log($"[Hero] OnNetworkSpawn for {heroName} - IsOwner: {IsOwner}, IsServer: {IsServer}");
        
        // Initialize hero state
        if (IsServer)
        {
            heroLevel.Value = 1;
        }
        
        // Setup visual elements
        SetupHeroVisuals();
        
        // Initialize abilities after a short delay
        StartCoroutine(DelayedAbilityInitialization());
    }
    
    private IEnumerator DelayedAbilityInitialization()
    {
        // Wait a moment for all components to be ready
        yield return new WaitForSeconds(0.5f);
        
        // Initialize abilities
        if (IsServer || IsOwner)
        {
            InitializeHeroAbilities();
        }
    }
    
    protected virtual void SetupHeroVisuals()
    {
        // Activate the hero model if it exists
        if (heroModel != null)
        {
            heroModel.SetActive(true);
        }
        
        // Override in derived classes for hero-specific visual setup
    }
    
    // Ability initialization - can be overridden in derived hero classes
    public virtual void InitializeHeroAbilities()
    {
        Debug.Log($"[Hero] Initializing abilities for {heroName}");
        
        if (abilityManager == null)
        {
            abilityManager = GetComponent<PlayerAbilityManager>();
        }
        
        if (abilityManager == null)
        {
            Debug.LogError("[Hero] No PlayerAbilityManager found!");
            return;
        }
        
        // Clear existing abilities
        abilityManager.RemoveAllAbilities();
        
        // Look for a matching HeroDefinition
        HeroDefinition myDefinition = FindHeroDefinition();
        
        if (myDefinition != null && myDefinition.abilities.Count > 0)
        {
            // Add abilities from definition
            AddAbilitiesFromDefinition(myDefinition);
        }
        else
        {
            Debug.LogWarning($"[Hero] No HeroDefinition found for {heroName} or no abilities defined");
            AddDefaultAbilities();
        }
    }
    
    // Helper method to find a matching hero definition
    private HeroDefinition FindHeroDefinition()
    {
        HeroDefinition[] allDefinitions = Resources.FindObjectsOfTypeAll<HeroDefinition>();
        foreach (var def in allDefinitions)
        {
            if (def.heroName == heroName)
            {
                return def;
            }
        }
        return null;
    }
    
    // Add abilities from a hero definition
    private void AddAbilitiesFromDefinition(HeroDefinition definition)
    {
        Debug.Log($"[Hero] Adding abilities from HeroDefinition for {heroName}");
        
        // Add each ability
        for (int i = 0; i < definition.abilities.Count; i++)
        {
            var abilityDef = definition.abilities[i];
            if (string.IsNullOrEmpty(abilityDef.abilityType))
                continue;
                
            try
            {
                Type abilityType = Type.GetType(abilityDef.abilityType);
                if (abilityType != null && typeof(BaseAbility).IsAssignableFrom(abilityType))
                {
                    // Add the ability
                    BaseAbility ability = abilityManager.AddAbilityByType(abilityType, i);
                    
                    // Configure ability properties if needed
                    if (ability != null)
                    {
                        ability.abilityName = abilityDef.abilityName;
                        ability.activationKey = abilityDef.activationKey;
                        ability.manaCost = abilityDef.manaCost;
                        ability.cooldown = abilityDef.cooldown;
                        ability.icon = abilityDef.icon;
                        
                        Debug.Log($"[Hero] Added ability: {abilityDef.abilityName} to slot {i}");
                    }
                }
                else
                {
                    Debug.LogError($"[Hero] Invalid ability type: {abilityDef.abilityType}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Hero] Error adding ability {abilityDef.abilityName}: {e.Message}");
            }
        }
    }
    
    // Add default abilities when no definition is found
    protected virtual void AddDefaultAbilities()
    {
        Debug.Log($"[Hero] Adding default abilities for {heroName}");
        
        // This can be overridden in derived classes to add specific abilities
        // Default implementation does nothing
    }
    
    // Hero update logic - can be overridden in derived classes
    protected virtual void Update()
    {
        // Base update logic for all heroes
        // Derived classes can override for hero-specific behavior
    }
    
    // Method to level up the hero
    public virtual void LevelUp()
    {
        if (!IsServer)
        {
            LevelUpServerRpc();
            return;
        }
        
        heroLevel.Value++;
        
        // Update stats based on new level
        if (playerStats != null)
        {
            float newMaxHealth = CalculateLevelHealth(heroLevel.Value);
            float newMaxMana = CalculateLevelMana(heroLevel.Value);
            
            playerStats.SetMaxHealth(newMaxHealth);
            playerStats.SetMaxMana(newMaxMana);
            
            // Heal on level up
            playerStats.Heal(newMaxHealth);
            playerStats.RestoreMana(newMaxMana);
        }
        
        // Notify clients
        OnLevelUpClientRpc(heroLevel.Value);
    }
    
    // Calculate health based on level (can be overridden)
    protected virtual float CalculateLevelHealth(int level)
    {
        // Default formula
        return 100f + (level * 50f);
    }
    
    // Calculate mana based on level (can be overridden)
    protected virtual float CalculateLevelMana(int level)
    {
        // Default formula
        return 50f + (level * 25f);
    }
    
    [ServerRpc]
    private void LevelUpServerRpc()
    {
        LevelUp();
    }
    
    [ClientRpc]
    private void OnLevelUpClientRpc(int newLevel)
    {
        Debug.Log($"{heroName} leveled up to level {newLevel}!");
        PlayLevelUpEffects();
    }
    
    protected virtual void PlayLevelUpEffects()
    {
        // Play visual/audio effects for level up
        // Default implementation just shows a log message for the owner
        if (IsOwner)
        {
            Debug.Log($"<color=yellow>Level Up!</color> Your {heroName} is now level {heroLevel.Value}");
        }
    }
    
    // Public method to activate all hero abilities
    public virtual void ActivateHeroAbilities()
    {
        // Enable abilities in derived classes
        Debug.Log($"[Hero] Activating abilities for {heroName}");
    }
    
    // Process hero-specific movement
    public virtual void ProcessMovement()
    {
        // Default: use normal PlayerNetwork movement
        // Override in derived classes for hero-specific movement
    }
    
    // Get hero level
    public int GetHeroLevel()
    {
        return heroLevel.Value;
    }
    
    // Get team color
    protected Color GetTeamColor()
    {
        int teamId = GetTeamId();
        
        // Default team colors
        if (teamId == 1)
            return Color.blue;
        else if (teamId == 2)
            return Color.red;
        else
            return Color.white;
    }
    
    // Get team ID
    protected int GetTeamId()
    {
        if (playerNetwork != null)
        {
            // Simple way to get team ID based on client ID
            // In a real implementation, you'd get this from your team system
            return (int)(OwnerClientId % 2) + 1;
        }
        return 1; // Default
    }
}