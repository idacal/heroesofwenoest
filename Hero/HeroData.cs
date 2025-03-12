using UnityEngine;
using System;
using PlayerAbilities;

// ScriptableObject to define a hero's data including abilities and stats
[CreateAssetMenu(fileName = "NewHero", menuName = "MOBA Game/Hero Data")]
public class HeroData : ScriptableObject
{
    [Header("Hero Information")]
    public string heroName = "New Hero";
    [TextArea(3, 10)]
    public string description = "Hero description";
    public Sprite portrait;
    public GameObject heroPrefab; // The prefab for this hero
    
    [Header("Abilities")]
    public AbilityData[] abilities = new AbilityData[4]; // Q, W, E, R abilities
    
    [Header("Base Stats")]
    public float baseHealth = 1000f;
    public float baseMana = 500f;
    public float healthRegen = 5f;
    public float manaRegen = 10f;
    public float moveSpeed = 7f;
    
    [Header("Tags")]
    public HeroClass heroClass;
    public string[] tags; // For filtering/categorization (e.g., "Melee", "Support", "Ranged")
    
    // Method to create a new hero instance based on this data
    public GameObject InstantiateHero(Vector3 position, Quaternion rotation)
    {
        if (heroPrefab == null)
        {
            Debug.LogError($"Hero prefab is not assigned for {heroName}!");
            return null;
        }
        
        // Instantiate the hero prefab
        GameObject heroInstance = Instantiate(heroPrefab, position, rotation);
        
        // Configure hero instance with abilities and stats
        ConfigureHeroInstance(heroInstance);
        
        return heroInstance;
    }
    
    // Configure hero instance with abilities and stats
    private void ConfigureHeroInstance(GameObject heroInstance)
    {
        // Set up player stats
        PlayerStats playerStats = heroInstance.GetComponent<PlayerStats>();
        if (playerStats != null)
        {
            // Method to set stats values would need to be added to PlayerStats
            SetPlayerStats(playerStats);
        }
        
        // Set up abilities
        PlayerAbilityController abilityController = heroInstance.GetComponent<PlayerAbilityController>();
        if (abilityController != null)
        {
            // First, clear any default abilities
            abilityController.RemoveAllAbilities();
            
            // Then add the hero's specific abilities
            AddHeroAbilities(abilityController);
        }
        
        // Set up hero class behavior if needed
        SetupHeroClassBehavior(heroInstance);
    }
    
    // Set player stats based on hero data
    private void SetPlayerStats(PlayerStats playerStats)
    {
        // You would need to add these methods to PlayerStats
        // or use reflection to set the values
        playerStats.SetMaxHealth(baseHealth);
        playerStats.SetMaxMana(baseMana);
        playerStats.SetHealthRegen(healthRegen);
        playerStats.SetManaRegen(manaRegen);
    }
    
    // Add the hero's specific abilities to the ability controller
    private void AddHeroAbilities(PlayerAbilityController abilityController)
    {
        foreach (var abilityData in abilities)
        {
            if (abilityData != null && abilityData.abilityType != null)
            {
                // Get the System.Type from the string
                Type abilityType = Type.GetType(abilityData.abilityType);
                
                if (abilityType != null && typeof(BaseAbility).IsAssignableFrom(abilityType))
                {
                    // Use reflection to add the ability
                    BaseAbility newAbility = abilityController.gameObject.AddComponent(abilityType) as BaseAbility;
                    
                    // Configure the ability with data if needed
                    if (newAbility != null)
                    {
                        ConfigureAbility(newAbility, abilityData);
                    }
                }
                else
                {
                    Debug.LogError($"Invalid ability type: {abilityData.abilityType} for hero {heroName}");
                }
            }
        }
    }
    
    // Configure an ability with its data
    private void ConfigureAbility(BaseAbility ability, AbilityData abilityData)
    {
        ability.abilityName = abilityData.abilityName;
        ability.activationKey = abilityData.activationKey;
        ability.manaCost = abilityData.manaCost;
        ability.cooldown = abilityData.cooldown;
        ability.icon = abilityData.icon;
        
        // Additional ability-specific configuration could be done here
    }
    
    // Setup any class-specific behavior
    private void SetupHeroClassBehavior(GameObject heroInstance)
    {
        // You could add class-specific components or behaviors here
        // based on the hero class
        switch (heroClass)
        {
            case HeroClass.Tank:
                // Add tank-specific behavior
                break;
            case HeroClass.Damage:
                // Add damage-specific behavior
                break;
            case HeroClass.Support:
                // Add support-specific behavior
                break;
            case HeroClass.Builder:
                // Add builder-specific behavior
                break;
        }
    }
}

// Enum for hero classes
public enum HeroClass
{
    Tank,
    Damage,
    Support,
    Builder
}

// Class to define ability data
[Serializable]
public class AbilityData
{
    public string abilityName = "New Ability";
    [TextArea(2, 5)]
    public string description = "Ability description";
    public KeyCode activationKey = KeyCode.Q;
    public float manaCost = 30f;
    public float cooldown = 2f;
    public Sprite icon;
    
    [Tooltip("The full type name of the ability component (e.g., 'PlayerAbilities.DashAbility')")]
    public string abilityType;
    
    // Additional ability-specific properties could be added here
    
    // Constructor for easy creation
    public AbilityData(string name, string desc, KeyCode key, float mana, float cd, string type)
    {
        abilityName = name;
        description = desc;
        activationKey = key;
        manaCost = mana;
        cooldown = cd;
        abilityType = type;
    }
}