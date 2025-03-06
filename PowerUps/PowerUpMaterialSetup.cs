using UnityEngine;

// Script de ayuda para configurar materiales de los power-ups en tiempo de ejecución
public class PowerUpMaterialSetup : MonoBehaviour
{
    [Header("Materiales")]
    [SerializeField] private Material healthMaterial;
    [SerializeField] private Material manaMaterial;
    [SerializeField] private Material healthManaMaterial;
    
    [Header("Colores")]
    [SerializeField] private Color healthColor = new Color(0.8f, 0.2f, 0.2f, 0.8f);  // Rojo
    [SerializeField] private Color manaColor = new Color(0.2f, 0.4f, 0.8f, 0.8f);    // Azul
    [SerializeField] private Color healthManaColor = new Color(0.8f, 0.2f, 0.8f, 0.8f); // Morado
    
    [Header("Propiedades de Emisión")]
    [SerializeField] private float emissionIntensity = 2.0f;
    [SerializeField] private bool useEmission = true;
    
    private void Awake()
    {
        // Crear los materiales si no están asignados
        CreateMaterials();
        
        // Asignar los materiales al HealthManaPowerUp si estamos adjuntos a uno
        AssignMaterials();
    }
    
    private void CreateMaterials()
    {
        // Crear material de salud si es necesario
        if (healthMaterial == null)
        {
            healthMaterial = new Material(Shader.Find("Standard"));
            healthMaterial.name = "HealthPowerUpMaterial";
            SetupMaterial(healthMaterial, healthColor);
        }
        
        // Crear material de maná si es necesario
        if (manaMaterial == null)
        {
            manaMaterial = new Material(Shader.Find("Standard"));
            manaMaterial.name = "ManaPowerUpMaterial";
            SetupMaterial(manaMaterial, manaColor);
        }
        
        // Crear material combinado si es necesario
        if (healthManaMaterial == null)
        {
            healthManaMaterial = new Material(Shader.Find("Standard"));
            healthManaMaterial.name = "HealthManaPowerUpMaterial";
            SetupMaterial(healthManaMaterial, healthManaColor);
        }
    }
    
    private void SetupMaterial(Material material, Color color)
    {
        // Configurar propiedades básicas
        material.color = color;
        
        // Configurar transparencia
        if (color.a < 1.0f)
        {
            material.SetFloat("_Mode", 3); // Transparent mode
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
        }
        
        // Configurar emisión si está activada
        if (useEmission)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emissionIntensity);
        }
    }
    
    private void AssignMaterials()
    {
        // Asignar los materiales al componente HealthManaPowerUp si existe
        HealthManaPowerUp powerUp = GetComponent<HealthManaPowerUp>();
        if (powerUp != null)
        {
            // Usamos reflexión para acceder a campos serializados privados
            // o podrías modificar la clase HealthManaPowerUp para exponer setters
            // Ejemplo de cómo se haría con reflexión:
            var healthMaterialField = typeof(HealthManaPowerUp).GetField("healthMaterial", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            
            if (healthMaterialField != null)
            {
                healthMaterialField.SetValue(powerUp, healthMaterial);
            }
        }
        
        // También podemos asignar directamente al MeshRenderer si existe
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            // Asignar según el tipo de power-up
            HealthManaPowerUp.PowerUpType type = HealthManaPowerUp.PowerUpType.Health;
            
            // Intentar obtener el tipo del componente
            if (powerUp != null)
            {
                var typeField = typeof(HealthManaPowerUp).GetField("powerUpType", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                
                if (typeField != null)
                {
                    type = (HealthManaPowerUp.PowerUpType)typeField.GetValue(powerUp);
                }
            }
            
            // Asignar material basado en el tipo
            switch (type)
            {
                case HealthManaPowerUp.PowerUpType.Health:
                    renderer.material = healthMaterial;
                    break;
                case HealthManaPowerUp.PowerUpType.Mana:
                    renderer.material = manaMaterial;
                    break;
                case HealthManaPowerUp.PowerUpType.HealthAndMana:
                    renderer.material = healthManaMaterial;
                    break;
            }
        }
    }
}