using UnityEngine;

public class PowerUpEffect : MonoBehaviour
{
    [Header("Configuración Visual")]
    [SerializeField] private float duration = 1.5f;
    [SerializeField] private float startScale = 0.5f;
    [SerializeField] private float endScale = 3f;
    [SerializeField] private AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private Color startColor = new Color(1f, 1f, 1f, 0.8f);
    [SerializeField] private Color endColor = new Color(1f, 1f, 1f, 0f);
    
    [Header("Componentes")]
    [SerializeField] private ParticleSystem particles;
    [SerializeField] private AudioSource audioSource;
    
    private float timeSinceStart = 0f;
    private Renderer myRenderer;
    
    private void Awake()
    {
        myRenderer = GetComponent<Renderer>();
        
        // Inicializar escala
        transform.localScale = Vector3.one * startScale;
        
        // Inicializar color si hay renderer
        if (myRenderer != null)
        {
            myRenderer.material.color = startColor;
        }
        
        // Iniciar sistema de partículas si existe
        if (particles != null)
        {
            particles.Play();
        }
        
        // Reproducir sonido si existe
        if (audioSource != null && audioSource.clip != null)
        {
            audioSource.Play();
        }
    }
    
    private void Update()
    {
        // Actualizar temporizador
        timeSinceStart += Time.deltaTime;
        
        // Calcular progreso normalizado (0-1)
        float progress = Mathf.Clamp01(timeSinceStart / duration);
        
        // Actualizar escala usando la curva de animación
        float scale = Mathf.Lerp(startScale, endScale, scaleCurve.Evaluate(progress));
        transform.localScale = Vector3.one * scale;
        
        // Actualizar color con interpolación lineal
        if (myRenderer != null)
        {
            myRenderer.material.color = Color.Lerp(startColor, endColor, progress);
        }
        
        // Destruir el objeto cuando termine la animación
        if (progress >= 1.0f)
        {
            Destroy(gameObject);
        }
    }
    
    // Método estático para crear efectos de diferentes tipos
    public static PowerUpEffect CreateEffect(Vector3 position, PowerUpEffectType type)
    {
        // Aquí se podría seleccionar diferentes prefabs según el tipo
        // Por ahora, usamos un solo prefab para simplificar
        
        string prefabPath = "Prefabs/PowerUpEffect";
        GameObject prefab = Resources.Load<GameObject>(prefabPath);
        
        if (prefab != null)
        {
            GameObject instance = Instantiate(prefab, position, Quaternion.identity);
            PowerUpEffect effect = instance.GetComponent<PowerUpEffect>();
            
            if (effect != null)
            {
                // Configurar colores según el tipo
                switch (type)
                {
                    case PowerUpEffectType.Health:
                        effect.startColor = new Color(1f, 0.3f, 0.3f, 0.8f); // Rojo
                        effect.ConfigureParticlesColor(effect.startColor);
                        break;
                    case PowerUpEffectType.Mana:
                        effect.startColor = new Color(0.3f, 0.3f, 1f, 0.8f); // Azul
                        effect.ConfigureParticlesColor(effect.startColor);
                        break;
                    case PowerUpEffectType.HealthAndMana:
                        effect.startColor = new Color(1f, 0.3f, 1f, 0.8f); // Morado
                        effect.ConfigureParticlesColor(effect.startColor);
                        break;
                }
                
                return effect;
            }
            
            return null;
        }
        
        Debug.LogWarning("PowerUpEffect prefab not found at: " + prefabPath);
        return null;
    }
    
    private void ConfigureParticlesColor(Color color)
    {
        if (particles != null)
        {
            var main = particles.main;
            main.startColor = color;
        }
    }
    
    // Enum para los diferentes tipos de efectos
    public enum PowerUpEffectType
    {
        Health,
        Mana,
        HealthAndMana
    }
}