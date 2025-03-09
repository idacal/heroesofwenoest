using UnityEngine;

// Script para efectos visuales simples de los proyectiles
[RequireComponent(typeof(TrailRenderer))]
public class ProjectileEffect : MonoBehaviour
{
    [Header("Configuración Visual")]
    [SerializeField] private Color projectileColor = Color.cyan;
    [SerializeField] private float projectileScale = 0.5f;
    [SerializeField] private float pulseFrequency = 2f;
    [SerializeField] private float pulseAmount = 0.2f;
    
    [Header("Efectos")]
    [SerializeField] private Light projectileLight;
    [SerializeField] private TrailRenderer trail;
    [SerializeField] private ParticleSystem particles;
    
    // Variables privadas
    private Material material;
    private float initialLightIntensity;
    private float initialScale;
    
    private void Awake()
    {
        // Obtener referencias si no están asignadas
        if (trail == null) trail = GetComponent<TrailRenderer>();
        if (projectileLight == null) projectileLight = GetComponentInChildren<Light>();
        if (particles == null) particles = GetComponentInChildren<ParticleSystem>();
        
        // Obtener material
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            material = renderer.material;
            material.color = projectileColor;
        }
        
        // Aplicar tamaño
        initialScale = projectileScale;
        transform.localScale = Vector3.one * projectileScale;
        
        // Configurar luz
        if (projectileLight != null)
        {
            projectileLight.color = projectileColor;
            initialLightIntensity = projectileLight.intensity;
        }
        
        // Configurar partículas
        if (particles != null)
        {
            var main = particles.main;
            main.startColor = projectileColor;
        }
        
        // Configurar trail
        if (trail != null)
        {
            trail.startColor = projectileColor;
            trail.endColor = new Color(projectileColor.r, projectileColor.g, projectileColor.b, 0f);
        }
    }
    
    private void Update()
    {
        // Efecto de pulso
        float pulse = 1f + Mathf.Sin(Time.time * pulseFrequency) * pulseAmount;
        
        // Aplicar a escala
        transform.localScale = Vector3.one * initialScale * pulse;
        
        // Aplicar a luz
        if (projectileLight != null)
        {
            projectileLight.intensity = initialLightIntensity * pulse;
        }
    }
    
    // Método para activar/desactivar efectos al impactar
    public void OnImpact()
    {
        // Detener emisión de partículas
        if (particles != null)
        {
            var emission = particles.emission;
            emission.enabled = false;
        }
        
        // Detener trail
        if (trail != null)
        {
            trail.emitting = false;
        }
        
        // Opcional: Apagar luz con un destello final
        if (projectileLight != null)
        {
            projectileLight.intensity = initialLightIntensity * 2; // Destello final
            Destroy(projectileLight.gameObject, 0.1f); // Eliminar después del destello
        }
    }
}