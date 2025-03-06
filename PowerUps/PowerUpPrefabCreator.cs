using UnityEngine;
using UnityEditor;
using Unity.Netcode;
#if UNITY_EDITOR
// Editor script para ayudar a crear prefabs de power-ups
public class PowerUpPrefabCreator : EditorWindow
{
    private HealthManaPowerUp.PowerUpType powerUpType = HealthManaPowerUp.PowerUpType.Health;
    private float healthAmount = 250f;
    private float manaAmount = 150f;
    private float respawnTime = 30f;
    private GameObject modelPrefab;
    private Material healthMaterial;
    private Material manaMaterial;
    private Material healthManaMaterial;
    private GameObject pickupEffectPrefab;
    private string prefabSavePath = "Assets/Prefabs/PowerUps/";
    private string prefabName = "HealthPowerUp";
    
    [MenuItem("Tools/MOBA Tools/Power-Up Prefab Creator")]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow(typeof(PowerUpPrefabCreator), false, "Power-Up Creator");
    }
    
    private void OnGUI()
    {
        GUILayout.Label("Power-Up Prefab Creator", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Power-Up Properties", EditorStyles.boldLabel);
        
        powerUpType = (HealthManaPowerUp.PowerUpType)EditorGUILayout.EnumPopup("Power-Up Type:", powerUpType);
        
        // Mostrar diferentes campos según el tipo seleccionado
        switch (powerUpType)
        {
            case HealthManaPowerUp.PowerUpType.Health:
                healthAmount = EditorGUILayout.FloatField("Health Amount:", healthAmount);
                prefabName = "HealthPowerUp";
                break;
                
            case HealthManaPowerUp.PowerUpType.Mana:
                manaAmount = EditorGUILayout.FloatField("Mana Amount:", manaAmount);
                prefabName = "ManaPowerUp";
                break;
                
            case HealthManaPowerUp.PowerUpType.HealthAndMana:
                healthAmount = EditorGUILayout.FloatField("Health Amount:", healthAmount);
                manaAmount = EditorGUILayout.FloatField("Mana Amount:", manaAmount);
                prefabName = "HealthManaPowerUp";
                break;
        }
        
        respawnTime = EditorGUILayout.FloatField("Respawn Time (seconds):", respawnTime);
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space();
        
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Visual Components", EditorStyles.boldLabel);
        modelPrefab = (GameObject)EditorGUILayout.ObjectField("Model Prefab:", modelPrefab, typeof(GameObject), false);
        pickupEffectPrefab = (GameObject)EditorGUILayout.ObjectField("Pickup Effect Prefab:", pickupEffectPrefab, typeof(GameObject), false);
        
        // Materiales
        healthMaterial = (Material)EditorGUILayout.ObjectField("Health Material:", healthMaterial, typeof(Material), false);
        manaMaterial = (Material)EditorGUILayout.ObjectField("Mana Material:", manaMaterial, typeof(Material), false);
        healthManaMaterial = (Material)EditorGUILayout.ObjectField("Health & Mana Material:", healthManaMaterial, typeof(Material), false);
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space();
        
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Prefab Settings", EditorStyles.boldLabel);
        prefabSavePath = EditorGUILayout.TextField("Save Path:", prefabSavePath);
        prefabName = EditorGUILayout.TextField("Prefab Name:", prefabName);
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space();
        
        if (GUILayout.Button("Create Power-Up Prefab"))
        {
            CreatePowerUpPrefab();
        }
    }
    
    private void CreatePowerUpPrefab()
    {
        // Validar que tenemos un modelo
        if (modelPrefab == null)
        {
            EditorUtility.DisplayDialog("Error", "Please assign a model prefab first!", "OK");
            return;
        }
        
        // Crear nuevo objeto para el power-up
        GameObject powerUpObject = new GameObject(prefabName);
        
        // Añadir el modelo como hijo
        GameObject modelInstance = Instantiate(modelPrefab, Vector3.zero, Quaternion.identity) as GameObject;
        modelInstance.transform.SetParent(powerUpObject.transform);
        modelInstance.transform.localPosition = Vector3.zero;
        
        // Añadir componentes necesarios
        HealthManaPowerUp powerUpComponent = powerUpObject.AddComponent<HealthManaPowerUp>();
        
        // Acceder a campos serializados usando SerializedObject
        SerializedObject serializedPowerUp = new SerializedObject(powerUpComponent);
        
        // Configurar tipo de power-up
        SerializedProperty powerUpTypeProp = serializedPowerUp.FindProperty("powerUpType");
        powerUpTypeProp.enumValueIndex = (int)powerUpType;
        
        // Configurar propiedades basadas en el tipo
        SerializedProperty healthAmountProp = serializedPowerUp.FindProperty("healthAmount");
        SerializedProperty manaAmountProp = serializedPowerUp.FindProperty("manaAmount");
        SerializedProperty respawnTimeProp = serializedPowerUp.FindProperty("respawnTime");
        SerializedProperty pickupEffectPrefabProp = serializedPowerUp.FindProperty("pickupEffectPrefab");
        
        healthAmountProp.floatValue = healthAmount;
        manaAmountProp.floatValue = manaAmount;
        respawnTimeProp.floatValue = respawnTime;
        
        if (pickupEffectPrefab != null)
        {
            pickupEffectPrefabProp.objectReferenceValue = pickupEffectPrefab;
        }
        
        // Aplicar cambios
        serializedPowerUp.ApplyModifiedProperties();
        
        // Añadir RotatePowerUp para el efecto de rotación
        RotatePowerUp rotator = powerUpObject.AddComponent<RotatePowerUp>();
        
        // Añadir colisor
        SphereCollider collider = powerUpObject.AddComponent<SphereCollider>();
        collider.radius = 1.5f;
        collider.isTrigger = true;
        
        // Añadir componente de red
        powerUpObject.AddComponent<NetworkObject>();
        
        // Configurar materiales según el tipo
        Renderer renderer = modelInstance.GetComponent<Renderer>();
        if (renderer != null)
        {
            switch (powerUpType)
            {
                case HealthManaPowerUp.PowerUpType.Health:
                    if (healthMaterial != null)
                        renderer.sharedMaterial = healthMaterial;
                    break;
                case HealthManaPowerUp.PowerUpType.Mana:
                    if (manaMaterial != null)
                        renderer.sharedMaterial = manaMaterial;
                    break;
                case HealthManaPowerUp.PowerUpType.HealthAndMana:
                    if (healthManaMaterial != null)
                        renderer.sharedMaterial = healthManaMaterial;
                    break;
            }
        }
        
        // Asegurar que el directorio existe
        if (!System.IO.Directory.Exists(prefabSavePath))
        {
            System.IO.Directory.CreateDirectory(prefabSavePath);
        }
        
        // Crear y guardar el prefab
        string prefabPath = prefabSavePath + prefabName + ".prefab";
        
        // Crear prefab
        bool success = false;
        PrefabUtility.SaveAsPrefabAsset(powerUpObject, prefabPath, out success);
        
        // Destruir el objeto temporal
        DestroyImmediate(powerUpObject);
        
        if (success)
        {
            EditorUtility.DisplayDialog("Success", "Power-Up prefab created successfully at: " + prefabPath, "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Error", "Failed to create prefab!", "OK");
        }
    }
}
#endif