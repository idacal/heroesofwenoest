using UnityEngine;

public class HitEffect : MonoBehaviour
{
    [Header("Configuración Visual")]
    [SerializeField] private float duration = 0.8f;
    [SerializeField] private float startScale = 0.3f;
    [SerializeField] private float endScale = 1.5f;
    [SerializeField] private AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private Color startColor = new Color(1f, 0.3f, 0.3f, 0.8f); // Rojo
    [SerializeField] private Color endColor = new Color(1f, 0.3f, 0.3f, 0f); // Transparente
    
    [Header("Componentes")]
    [SerializeField] private ParticleSystem particles;
    [SerializeField] private AudioSource audioSource;
    
    // Referencias internas
    private Renderer myRenderer;
    private float timeSinceStart = 0f;
    
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
        
        // Orientar efecto hacia la cámara
        if (Camera.main != null)
        {
            transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
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
        
        // Orientar constantemente hacia la cámara
        if (Camera.main != null)
        {
            transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
        }
        
        // Destruir el objeto cuando termine la animación
        if (progress >= 1.0f)
        {
            Destroy(gameObject);
        }
    }
    
    // Método para configurar colores personalizados
    public void SetColors(Color start, Color end)
    {
        startColor = start;
        endColor = end;
        
        if (myRenderer != null)
        {
            myRenderer.material.color = startColor;
        }
    }
    
    // Método estático para crear un efecto rápidamente
    public static HitEffect Create(Vector3 position, Color color)
    {
        // Buscar el prefab en Resources
        GameObject prefab = Resources.Load<GameObject>("Effects/HitEffect");
        
        if (prefab == null)
        {
            // Si no hay prefab, crear un objeto básico
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            obj.name = "HitEffect";
            
            // Añadir el componente
            HitEffect effect = obj.AddComponent<HitEffect>();
            
            // Configurar
            obj.transform.position = position;
            
            // Devolver el componente
            return effect;
        }
        else
        {
            // Instanciar el prefab
            GameObject instance = Instantiate(prefab, position, Quaternion.identity);
            HitEffect effect = instance.GetComponent<HitEffect>();
            
            if (effect != null)
            {
                effect.SetColors(color, new Color(color.r, color.g, color.b, 0f));
            }
            
            return effect;
        }
    }
}