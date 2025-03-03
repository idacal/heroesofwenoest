using Unity.Netcode;
using UnityEngine;
using System.Collections;

// Este script es una versión del efecto de escudo que respeta los colores de equipo
public class SimpleShieldEffect : MonoBehaviour
{
    public Color shieldColor = new Color(0, 0.7f, 1f, 0.4f); // Azul brillante semitransparente
    public float pulseDuration = 1.5f;
    public float minScale = 0.95f;
    public float maxScale = 1.05f;
    
    private Transform sphereShield;
    private Material shieldMaterial;
    
    // Variables para almacenar los renderers del jugador
    private Renderer[] playerRenderers;
    private Color[] teamColors;
    private bool hasInitializedColors = false;
    
    void Start()
    {
        // Primero creamos el efecto de escudo visual sin modificar al jugador
        CreateShieldEffect();
        
        // Luego, esperamos un breve momento para capturar los colores correctos
        // Esto nos da tiempo para asegurarnos de que los colores de equipo ya estén aplicados
        StartCoroutine(InitializePlayerColorsAfterDelay());
    }
    
    private void CreateShieldEffect()
    {
        // Crear un objeto esférico para el efecto de escudo
        GameObject shield = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shield.name = "VisualShield";
        shield.transform.SetParent(transform);
        shield.transform.localPosition = Vector3.zero;
        shield.transform.localScale = Vector3.one * 1.2f; // Un poco más grande que el jugador
        
        // Eliminar el collider para que no interfiera
        Destroy(shield.GetComponent<Collider>());
        
        // Configurar el material del escudo
        Renderer renderer = shield.GetComponent<Renderer>();
        shieldMaterial = new Material(Shader.Find("Standard"));
        shieldMaterial.color = shieldColor;
        
        // Configurar como transparente
        shieldMaterial.SetFloat("_Mode", 3); // Transparent mode
        shieldMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        shieldMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        shieldMaterial.SetInt("_ZWrite", 0);
        shieldMaterial.DisableKeyword("_ALPHATEST_ON");
        shieldMaterial.EnableKeyword("_ALPHABLEND_ON");
        shieldMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        shieldMaterial.renderQueue = 3000;
        
        // Añadir efecto de emisión
        shieldMaterial.EnableKeyword("_EMISSION");
        shieldMaterial.SetColor("_EmissionColor", new Color(0, 0.5f, 1f) * 0.5f);
        
        renderer.material = shieldMaterial;
        
        // Guardar referencia
        sphereShield = shield.transform;
        
        // Iniciar efecto de pulso
        StartCoroutine(PulseEffect());
        
        Debug.Log("Efecto visual de escudo creado");
    }
    
    private IEnumerator InitializePlayerColorsAfterDelay()
    {
        // Esperar un breve momento para asegurarnos de que los colores de equipo estén aplicados
        yield return new WaitForSeconds(0.2f);
        
        // Obtener todos los renderers del jugador
        playerRenderers = GetComponentsInChildren<Renderer>();
        teamColors = new Color[playerRenderers.Length];
        
        // Guardar los colores originales (que ya deberían incluir el color de equipo)
        for (int i = 0; i < playerRenderers.Length; i++)
        {
            if (playerRenderers[i] != null && playerRenderers[i].material != null)
            {
                teamColors[i] = playerRenderers[i].material.color;
                Debug.Log($"Guardado color de equipo para renderer {i}: {teamColors[i]}");
                
                // Cambiar temporalmente al color del escudo (mezclado con el color del equipo)
                // Mantener algo del color original para que se note de qué equipo es
                Color teamShieldColor = Color.Lerp(teamColors[i], shieldColor, 0.7f);
                teamShieldColor.a = 1f; // Mantener opacidad completa
                
                // Aplicar el nuevo color de escudo
                playerRenderers[i].material.color = teamShieldColor;
            }
        }
        
        hasInitializedColors = true;
        Debug.Log("Colores de equipo capturados y modificados para el efecto de escudo");
    }
    
    private IEnumerator PulseEffect()
    {
        float timer = 0;
        
        while (true)
        {
            timer += Time.deltaTime;
            float pulseValue = Mathf.Lerp(minScale, maxScale, (Mathf.Sin(timer / pulseDuration * Mathf.PI * 2) + 1) / 2);
            
            if (sphereShield != null)
            {
                Vector3 baseScale = Vector3.one * 1.2f;
                sphereShield.localScale = baseScale * pulseValue;
            }
            
            yield return null;
        }
    }
    
    void OnDestroy()
    {
        // Solo restaurar colores si los inicializamos antes
        if (hasInitializedColors)
        {
            // Restaurar los colores originales del equipo
            for (int i = 0; i < playerRenderers.Length; i++)
            {
                if (playerRenderers[i] != null && i < teamColors.Length)
                {
                    playerRenderers[i].material.color = teamColors[i];
                }
            }
            
            Debug.Log("Colores de equipo restaurados");
        }
        
        // Asegurarse de limpiar el material del escudo
        if (shieldMaterial != null)
        {
            Destroy(shieldMaterial);
        }
        
        Debug.Log("SimpleShieldEffect destruido");
    }
}