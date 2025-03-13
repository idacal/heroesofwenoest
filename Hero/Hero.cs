using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

// Base class for all heroes
public class Hero : NetworkBehaviour
{
    [Header("Hero Information")]
    public string heroName = "Default Hero";
    public HeroClass heroClass;
    
    [Header("Visual")]
    [SerializeField] private GameObject heroModel;
    
    // Network variables for hero-specific state
    private NetworkVariable<int> heroLevel = new NetworkVariable<int>(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Referencias de componentes
    protected PlayerNetwork playerNetwork;
    protected PlayerStats playerStats;
    protected PlayerAbilityController abilityController;
    protected PlayerCombat playerCombat;
    
    // Called when the hero is first created
    protected virtual void Awake()
    {
        // Get references to components
        playerNetwork = GetComponent<PlayerNetwork>();
        playerStats = GetComponent<PlayerStats>();
        abilityController = GetComponent<PlayerAbilityController>();
        playerCombat = GetComponent<PlayerCombat>();
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Initialize hero-specific state
        if (IsServer)
        {
            heroLevel.Value = 1;
        }
        
        // Setup hero visuals
        SetupHeroVisuals();
    }
    
    protected virtual void SetupHeroVisuals()
    {
        // Apply hero-specific visual settings
        // This could include setting up materials, animations, etc.
        
        // Enable the correct hero model if we have multiple
        if (heroModel != null)
        {
            heroModel.SetActive(true);
        }
    }
    
    // Level up the hero
    public virtual void LevelUp()
    {
        if (!IsServer) 
        {
            LevelUpServerRpc();
            return;
        }
        
        heroLevel.Value++;
        
        // Increase stats on level up
        if (playerStats != null)
        {
            // Example: Increase max health and mana
            float newMaxHealth = playerStats.MaxHealth + (50 * heroLevel.Value);
            float newMaxMana = playerStats.MaxMana + (25 * heroLevel.Value);
            
            playerStats.SetMaxHealth(newMaxHealth);
            playerStats.SetMaxMana(newMaxMana);
            
            // Heal on level up
            playerStats.Heal(newMaxHealth);
            playerStats.RestoreMana(newMaxMana);
        }
        
        // Notify clients about the level up
        OnLevelUpClientRpc(heroLevel.Value);
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
        
        // Play level up effects
        PlayLevelUpEffects();
    }
    
    protected virtual void PlayLevelUpEffects()
    {
        // Play particle effects, sounds, etc.
        
        // Example: Simple debug message
        if (IsOwner)
        {
            Debug.Log($"<color=yellow>Level Up!</color> Your {heroName} is now level {heroLevel.Value}");
            
            // Additional local effects for the owner could be added here
        }
    }
    
    // Hero-specific update logic
    protected virtual void Update()
    {
        // Base update logic that applies to all heroes
        // Derived classes can override this to add hero-specific behavior
    }
    
    // NUEVO: Método virtual para inicializar habilidades de héroe
    public virtual void InitializeHeroAbilities()
    {
        // La implementación base no hace nada específico
        Debug.Log($"[Hero] Inicialización genérica de habilidades para {heroName}");
        
        // Obtener el controlador de habilidades
        PlayerAbilityController abilityController = GetComponent<PlayerAbilityController>();
        if (abilityController == null) return;
        
        // Limpiar habilidades existentes
        abilityController.RemoveAllAbilities();
        
        // En la clase base, podríamos añadir habilidades comunes a todos los héroes
        // Pero dejamos que las clases derivadas definan sus propias habilidades específicas
    }
    
    // This method can be called when the hero's abilities should be activated based on game state
    public virtual void ActivateHeroAbilities()
    {
        // Enable the hero's abilities and make them ready to use
        // This could be called when a game state changes, like exiting an intro phase
    }
    
    // This method can be called to override default movement options or add hero-specific movement
    public virtual void ProcessMovement()
    {
        // By default, do nothing - use the normal PlayerNetwork movement
        // Override in derived classes to implement hero-specific movement
    }
    
    // Get hero level
    public int GetHeroLevel()
    {
        return heroLevel.Value;
    }
    
    // Helper to access player colors by team
    protected Color GetTeamColor()
    {
        // This assumes there's a method to get team ID from PlayerNetwork
        // and that team colors are defined somewhere (e.g., in GameManager)
        if (playerNetwork != null)
        {
            // Example implementation - would need to be adjusted based on your team system
            int teamId = GetTeamId();
            
            if (teamId == 1)
            {
                return Color.blue; // Team 1 color
            }
            else if (teamId == 2)
            {
                return Color.red; // Team 2 color
            }
        }
        
        return Color.white; // Default
    }
    
    // Get team ID - you'd need to implement this based on your team system
    protected int GetTeamId()
    {
        // Example implementation - would need to be adjusted based on your team system
        if (playerNetwork != null)
        {
            // This assumes you have a way to get team ID from the network component
            // Could be a property in PlayerNetwork or stored in a dictionary in GameManager
            
            // Placeholder implementation
            return (int)(OwnerClientId % 2) + 1; // Simple way to divide into 2 teams
        }
        
        return 1; // Default to team 1
    }
}