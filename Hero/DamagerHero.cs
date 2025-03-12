using Unity.Netcode;
using UnityEngine;
using PlayerAbilities;

// Sample implementation of a damager hero
public class DamagerHero : Hero
{
    [Header("Damager Hero Settings")]
    [SerializeField] private float attackDamageBonus = 5f; // Additional damage per level
    [SerializeField] private float criticalChance = 0.1f;  // 10% base critical chance
    [SerializeField] private GameObject specialAttackEffect;
    
    // References to specific abilities
    private DashAbility dashAbility;
    private StrongJumpAbility strongJumpAbility;
    
    // Custom network variable for this hero's special state
    private NetworkVariable<bool> isEnraged = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    protected override void Awake()
    {
        base.Awake();
        
        // Set hero-specific properties
        heroName = "Striker";
        heroClass = HeroClass.Damage;
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Subscribe to network variable changes
        isEnraged.OnValueChanged += OnEnragedStateChanged;
        
        // Initialize any hero-specific logic
        if (IsServer)
        {
            // Server-side initialization
            InitializeHeroServer();
        }
        
        if (IsClient)
        {
            // Client-side initialization
            InitializeHeroClient();
        }
    }
    
    private void InitializeHeroServer()
    {
        // Set up additional server-side aspects of this hero
        if (playerCombat != null)
        {
            // Adjust attack damage based on hero stats
            // Note: You would need to add a method to PlayerCombat to set the attack damage
            SetAttackDamage(15f + (attackDamageBonus * GetHeroLevel()));
        }
    }
    
    private void InitializeHeroClient()
    {
        // Set up client-side aspects of this hero
        // Get references to the specific abilities this hero uses
        GetAbilityReferences();
        
        // Set up visual effects specific to this hero
        SetupHeroEffects();
    }
    
    private void GetAbilityReferences()
    {
        // Get references to the specific abilities we want to use
        if (abilityController != null)
        {
            // Look through all abilities and find the ones we need
            BaseAbility[] abilities = GetComponents<BaseAbility>();
            foreach (var ability in abilities)
            {
                if (ability is DashAbility)
                {
                    dashAbility = (DashAbility)ability;
                }
                else if (ability is StrongJumpAbility)
                {
                    strongJumpAbility = (StrongJumpAbility)ability;
                }
            }
        }
    }
    
    protected override void Update()
    {
        base.Update();
        
        // Hero-specific update logic
        if (IsOwner)
        {
            // Process hero-specific input
            ProcessHeroInput();
        }
        
        // Update visual effects based on hero state
        UpdateHeroEffects();
    }
    
    private void ProcessHeroInput()
    {
        // Example: Special hero ability triggered by a key
        if (Input.GetKeyDown(KeyCode.Space) && CanUseSpecialAbility())
        {
            ActivateSpecialAbilityServerRpc();
        }
    }
    
    private bool CanUseSpecialAbility()
    {
        // Check conditions for using the special ability
        // For example: enough mana, not on cooldown, etc.
        return playerStats != null && playerStats.CurrentMana >= 30f && !isEnraged.Value;
    }
    
    [ServerRpc]
    private void ActivateSpecialAbilityServerRpc()
    {
        // Validate we can use the ability
        if (!CanUseSpecialAbility()) return;
        
        // Consume resources
        playerStats.UseMana(30f);
        
        // Activate the special ability state
        isEnraged.Value = true;
        
        // Start a timer to deactivate it
        StartCoroutine(DeactivateSpecialAbilityAfterDelay(10f));
        
        // Notify clients to show effects
        ActivateSpecialAbilityEffectsClientRpc();
    }
    
    private System.Collections.IEnumerator DeactivateSpecialAbilityAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // Deactivate the special ability
        if (isEnraged.Value)
        {
            isEnraged.Value = false;
        }
    }
    
    [ClientRpc]
    private void ActivateSpecialAbilityEffectsClientRpc()
    {
        // Play activation effects
        if (specialAttackEffect != null)
        {
            GameObject effect = Instantiate(specialAttackEffect, transform.position, Quaternion.identity);
            effect.transform.SetParent(transform);
            Destroy(effect, 10f); // Match the duration of the effect with the ability duration
        }
        
        // Play sound, animation, etc.
        Debug.Log($"{heroName} activates special ability!");
    }
    
    private void OnEnragedStateChanged(bool oldValue, bool newValue)
    {
        // React to the enraged state changing
        if (newValue)
        {
            // Became enraged
            if (IsOwner)
            {
                Debug.Log("<color=red>You are ENRAGED!</color> Your attacks deal more damage!");
            }
            
            // Apply visual changes to indicate enraged state
            ApplyEnragedVisuals(true);
            
            // If we're on the server, apply the gameplay effects
            if (IsServer)
            {
                // For example, increase damage, speed, etc.
                SetAttackDamage(15f + (attackDamageBonus * GetHeroLevel() * 2)); // Double damage bonus
                SetCriticalChance(criticalChance * 2); // Double crit chance
            }
        }
        else
        {
            // No longer enraged
            if (IsOwner)
            {
                Debug.Log("Your rage subsides...");
            }
            
            // Remove visual changes
            ApplyEnragedVisuals(false);
            
            // If we're on the server, revert the gameplay effects
            if (IsServer)
            {
                // Revert to normal stats
                SetAttackDamage(15f + (attackDamageBonus * GetHeroLevel()));
                SetCriticalChance(criticalChance);
            }
        }
    }
    
    private void ApplyEnragedVisuals(bool enraged)
    {
        // Apply visual effects to show enraged state
        // This could include changing materials, playing particles, etc.
        
        // Example: Change the color of the hero's model
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            if (renderer != null)
            {
                // Apply a red tint when enraged
                renderer.material.color = enraged ? 
                    Color.Lerp(renderer.material.color, Color.red, 0.5f) : 
                    GetTeamColor();
            }
        }
    }
    
    private void SetupHeroEffects()
    {
        // Set up any permanent visual effects for this hero
    }
    
    private void UpdateHeroEffects()
    {
        // Update any dynamic effects based on hero state
        if (isEnraged.Value)
        {
            // Example: Add pulsing effect when enraged
            float pulse = Mathf.Sin(Time.time * 5f) * 0.2f + 0.8f;
            transform.localScale = Vector3.one * pulse;
        }
        else
        {
            // Reset scale when not enraged
            transform.localScale = Vector3.one;
        }
    }
    
    // These methods would need to be added to PlayerCombat
    private void SetAttackDamage(float damage)
    {
        if (playerCombat != null)
        {
            // This is a placeholder - you would need to implement this method in PlayerCombat
            // playerCombat.SetAttackDamage(damage);
            Debug.Log($"Setting attack damage to {damage}");
        }
    }
    
    private void SetCriticalChance(float chance)
    {
        if (playerCombat != null)
        {
            // This is a placeholder - you would need to implement this method in PlayerCombat
            // playerCombat.SetCriticalChance(chance);
            Debug.Log($"Setting critical chance to {chance}");
        }
    }
    
    public override void OnNetworkDespawn()
    {
        // Unsubscribe from events
        isEnraged.OnValueChanged -= OnEnragedStateChanged;
        
        base.OnNetworkDespawn();
    }
}