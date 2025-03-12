using UnityEngine;
using UnityEditor;
using Unity.Netcode;
using PlayerAbilities;
using System;
using System.Collections.Generic;

#if UNITY_EDITOR
// Editor script to help create hero prefabs
public class HeroPrefabCreator : EditorWindow
{
    private string heroName = "NewHero";
    private HeroClass heroClass = HeroClass.Damage;
    private GameObject basePrefab;
    private List<AbilityDefinition> abilities = new List<AbilityDefinition>();
    
    private float baseHealth = 1000f;
    private float baseMana = 500f;
    private float healthRegen = 5f;
    private float manaRegen = 10f;
    private float moveSpeed = 7f;
    
    private Vector2 scrollPosition;
    private string prefabSavePath = "Assets/Prefabs/Heroes/";
    private Texture2D heroIcon;

    [MenuItem("Tools/MOBA Tools/Hero Prefab Creator")]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow(typeof(HeroPrefabCreator), false, "Hero Creator");
    }
    
    private void OnEnable()
    {
        // Initialize with 4 ability slots (Q, W, E, R)
        if (abilities.Count == 0)
        {
            abilities.Add(new AbilityDefinition { activationKey = KeyCode.Q });
            abilities.Add(new AbilityDefinition { activationKey = KeyCode.W });
            abilities.Add(new AbilityDefinition { activationKey = KeyCode.E });
            abilities.Add(new AbilityDefinition { activationKey = KeyCode.R });
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("Hero Prefab Creator", EditorStyles.boldLabel);
        
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Basic Information", EditorStyles.boldLabel);
        
        heroName = EditorGUILayout.TextField("Hero Name:", heroName);
        heroClass = (HeroClass)EditorGUILayout.EnumPopup("Hero Class:", heroClass);
        heroIcon = (Texture2D)EditorGUILayout.ObjectField("Hero Icon:", heroIcon, typeof(Texture2D), false);
        basePrefab = (GameObject)EditorGUILayout.ObjectField("Base Prefab:", basePrefab, typeof(GameObject), false);
        
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space();
        
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Stats", EditorStyles.boldLabel);
        
        baseHealth = EditorGUILayout.FloatField("Base Health:", baseHealth);
        baseMana = EditorGUILayout.FloatField("Base Mana:", baseMana);
        healthRegen = EditorGUILayout.FloatField("Health Regen:", healthRegen);
        manaRegen = EditorGUILayout.FloatField("Mana Regen:", manaRegen);
        moveSpeed = EditorGUILayout.FloatField("Move Speed:", moveSpeed);
        
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space();
        
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Abilities", EditorStyles.boldLabel);
        
        for (int i = 0; i < abilities.Count; i++)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"Ability {i+1} ({abilities[i].activationKey})", EditorStyles.boldLabel);
            
            abilities[i].name = EditorGUILayout.TextField("Name:", abilities[i].name);
            abilities[i].activationKey = (KeyCode)EditorGUILayout.EnumPopup("Key:", abilities[i].activationKey);
            abilities[i].abilityType = EditorGUILayout.TextField("Ability Type:", abilities[i].abilityType);
            abilities[i].icon = (Texture2D)EditorGUILayout.ObjectField("Icon:", abilities[i].icon, typeof(Texture2D), false);
            abilities[i].manaCost = EditorGUILayout.FloatField("Mana Cost:", abilities[i].manaCost);
            abilities[i].cooldown = EditorGUILayout.FloatField("Cooldown:", abilities[i].cooldown);
            
            if (GUILayout.Button("Browse Ability Types"))
            {
                ShowAbilityTypePicker(i);
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }
        
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space();
        
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Prefab Settings", EditorStyles.boldLabel);
        prefabSavePath = EditorGUILayout.TextField("Save Path:", prefabSavePath);
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.EndScrollView();
        
        EditorGUILayout.Space();
        
        if (GUILayout.Button("Create Hero Prefab"))
        {
            CreateHeroPrefab();
        }
        
        if (GUILayout.Button("Create Hero Data ScriptableObject"))
        {
            CreateHeroDataAsset();
        }
    }
    
    private void ShowAbilityTypePicker(int abilityIndex)
    {
        // Get all types that derive from BaseAbility
        List<Type> abilityTypes = new List<Type>();
        
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (typeof(BaseAbility).IsAssignableFrom(type) && !type.IsAbstract)
                    {
                        abilityTypes.Add(type);
                    }
                }
            }
            catch (Exception)
            {
                // Skip assemblies that can't be loaded
            }
        }
        
        // Create the menu
        GenericMenu menu = new GenericMenu();
        
        foreach (var type in abilityTypes)
        {
            menu.AddItem(new GUIContent(type.FullName), false, () => {
                abilities[abilityIndex].abilityType = type.FullName;
                Repaint();
            });
        }
        
        menu.ShowAsContext();
    }
    
    private void CreateHeroPrefab()
    {
        if (basePrefab == null)
        {
            EditorUtility.DisplayDialog("Error", "Please select a base prefab!", "OK");
            return;
        }
        
        // Create directory if it doesn't exist
        if (!System.IO.Directory.Exists(prefabSavePath))
        {
            System.IO.Directory.CreateDirectory(prefabSavePath);
        }
        
        // Create hero object based on base prefab
        GameObject heroObject = Instantiate(basePrefab);
        heroObject.name = heroName + "Hero";
        
        // Make sure it has all the required components
        EnsureRequiredComponents(heroObject);
        
        // Set up hero-specific properties
        SetupHeroProperties(heroObject);
        
        // Create the prefab
        string prefabPath = prefabSavePath + heroObject.name + ".prefab";
        bool success = false;
        
        try
        {
            PrefabUtility.SaveAsPrefabAsset(heroObject, prefabPath, out success);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error creating prefab: {e.Message}");
            success = false;
        }
        
        // Clean up
        DestroyImmediate(heroObject);
        
        if (success)
        {
            EditorUtility.DisplayDialog("Success", "Hero prefab created at: " + prefabPath, "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Error", "Failed to create hero prefab!", "OK");
        }
    }
    
    private void EnsureRequiredComponents(GameObject heroObject)
    {
        // Make sure it has NetworkObject
        if (heroObject.GetComponent<NetworkObject>() == null)
        {
            heroObject.AddComponent<NetworkObject>();
        }
        
        // Make sure it has PlayerNetwork
        if (heroObject.GetComponent<PlayerNetwork>() == null)
        {
            heroObject.AddComponent<PlayerNetwork>();
        }
        
        // Make sure it has PlayerStats
        if (heroObject.GetComponent<PlayerStats>() == null)
        {
            heroObject.AddComponent<PlayerStats>();
        }
        
        // Make sure it has PlayerAbilityController
        if (heroObject.GetComponent<PlayerAbilityController>() == null)
        {
            heroObject.AddComponent<PlayerAbilityController>();
        }
        
        // Make sure it has the Hero component
        Hero heroComponent = heroObject.GetComponent<Hero>();
        if (heroComponent == null)
        {
            heroComponent = heroObject.AddComponent<Hero>();
        }
        
        // Set the hero class
        heroComponent.heroClass = heroClass;
        
        // Other required components
        if (heroObject.GetComponent<PlayerAbility>() == null)
        {
            heroObject.AddComponent<PlayerAbility>();
        }
        
        if (heroObject.GetComponent<PlayerCombat>() == null)
        {
            heroObject.AddComponent<PlayerCombat>();
        }
        
        if (heroObject.GetComponent<PlayerRespawnController>() == null)
        {
            heroObject.AddComponent<PlayerRespawnController>();
        }
    }
    
    private void SetupHeroProperties(GameObject heroObject)
    {
        // Set up PlayerStats with hero stats
        PlayerStats stats = heroObject.GetComponent<PlayerStats>();
        if (stats != null)
        {
            // We need to use SerializedObject for private fields
            SerializedObject serializedStats = new SerializedObject(stats);
            serializedStats.FindProperty("maxHealth").floatValue = baseHealth;
            serializedStats.FindProperty("maxMana").floatValue = baseMana;
            serializedStats.FindProperty("healthRegen").floatValue = healthRegen;
            serializedStats.FindProperty("manaRegen").floatValue = manaRegen;
            serializedStats.ApplyModifiedProperties();
        }
        
        // Set up PlayerNetwork movement speed
        PlayerNetwork playerNetwork = heroObject.GetComponent<PlayerNetwork>();
        if (playerNetwork != null)
        {
            SerializedObject serializedNetwork = new SerializedObject(playerNetwork);
            SerializedProperty movementProp = serializedNetwork.FindProperty("clickMovementSpeed");
            if (movementProp != null)
            {
                movementProp.floatValue = moveSpeed;
                serializedNetwork.ApplyModifiedProperties();
            }
        }
        
        // Set up Hero component
        Hero heroComponent = heroObject.GetComponent<Hero>();
        if (heroComponent != null)
        {
            heroComponent.heroName = heroName;
            heroComponent.heroClass = heroClass;
        }
    }
    
    private void CreateHeroDataAsset()
    {
        // Create a new HeroData scriptable object
        HeroData heroData = ScriptableObject.CreateInstance<HeroData>();
        
        // Set basic properties
        heroData.heroName = heroName;
        heroData.description = $"{heroClass} hero with unique abilities";
        heroData.heroClass = heroClass;
        heroData.baseHealth = baseHealth;
        heroData.baseMana = baseMana;
        heroData.healthRegen = healthRegen;
        heroData.manaRegen = manaRegen;
        heroData.moveSpeed = moveSpeed;
        
        // If we have an icon, convert it to sprite
        if (heroIcon != null)
        {
            Sprite iconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(heroIcon));
            if (iconSprite != null)
            {
                heroData.portrait = iconSprite;
            }
        }
        
        // Set up abilities
        for (int i = 0; i < abilities.Count && i < heroData.abilities.Length; i++)
        {
            AbilityData abilityData = new AbilityData(
                abilities[i].name,
                $"Description for {abilities[i].name}",
                abilities[i].activationKey,
                abilities[i].manaCost,
                abilities[i].cooldown,
                abilities[i].abilityType
            );
            
            if (abilities[i].icon != null)
            {
                Sprite abilitySprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(abilities[i].icon));
                if (abilitySprite != null)
                {
                    abilityData.icon = abilitySprite;
                }
            }
            
            heroData.abilities[i] = abilityData;
        }
        
        // Set hero prefab if it exists
        string heroPath = prefabSavePath + heroName + "Hero.prefab";
        if (System.IO.File.Exists(heroPath))
        {
            GameObject heroPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(heroPath);
            if (heroPrefab != null)
            {
                heroData.heroPrefab = heroPrefab;
            }
        }
        
        // Create directory if it doesn't exist
        string dataPath = "Assets/ScriptableObjects/Heroes/";
        if (!System.IO.Directory.Exists(dataPath))
        {
            System.IO.Directory.CreateDirectory(dataPath);
        }
        
        // Save the asset
        string assetPath = dataPath + heroName + "Data.asset";
        AssetDatabase.CreateAsset(heroData, assetPath);
        AssetDatabase.SaveAssets();
        
        // Show the asset in the Project window
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = heroData;
        
        EditorUtility.DisplayDialog("Success", $"Hero data asset created at: {assetPath}", "OK");
    }
    
    // Simple class to hold ability definition in the editor
    [System.Serializable]
    private class AbilityDefinition
    {
        public string name = "New Ability";
        public KeyCode activationKey = KeyCode.Q;
        public string abilityType = "PlayerAbilities.DashAbility";
        public Texture2D icon;
        public float manaCost = 30f;
        public float cooldown = 10f;
    }
}
#endif