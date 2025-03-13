using UnityEngine;
using System;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewHero", menuName = "MOBA/Hero Definition")]
public class HeroDefinition : ScriptableObject
{
    [Header("Basic Info")]
    public string heroName = "New Hero";
    public string description;
    public HeroClass heroClass;
    public Sprite portrait;
    public GameObject modelPrefab;

    [Header("Base Stats")]
    public float baseHealth = 200f;
    public float baseMana = 100f;
    public float healthRegen = 1f;
    public float manaRegen = 5f;
    public float moveSpeed = 10f;

    [Header("Abilities")]
    public List<AbilityDefinition> abilities = new List<AbilityDefinition>();

    // Helper method to validate hero configuration
    public bool Validate()
    {
        if (string.IsNullOrEmpty(heroName))
            return false;
        
        if (modelPrefab == null)
            return false;
            
        return true;
    }
}

[Serializable]
public class AbilityDefinition
{
    public string abilityName;
    public string abilityType; // Fully qualified name of ability class
    public KeyCode activationKey = KeyCode.Q;
    public float manaCost = 30f;
    public float cooldown = 3f;
    public Sprite icon;
}