using UnityEngine;

// Script para el indicador visual de clic en el suelo con efecto de radar
public class ClickIndicator : MonoBehaviour
{
    [Header("Visual Settings")]
    [SerializeField] private float maxRadius = 2f;      // Radio máximo del indicador
    [SerializeField] private float initialSize = 0.3f;  // Tamaño inicial del punto
    [SerializeField] private float expansionCurve = 2f; // Controla la curva de expansión
    [SerializeField] private float duration = 0.8f;     // Duración total de la animación
    [SerializeField] private float fadeSpeed = 2.0f;    // Velocidad de desvanecimiento
    [SerializeField] private Color indicatorColor = new Color(0.2f, 0.8f, 0.2f, 0.5f); // Color del indicador

    [Header("References")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    
    // Variables para animación
    private float animTimer = 0f;
    private bool isAnimating = false;
    private Renderer visualRenderer; // Referencia general a cualquier tipo de renderer
    
    private void Awake()
    {
        // MODIFICADO: Buscar cualquier tipo de renderer
        // Verificar si tenemos el SpriteRenderer
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
        
        // Si no hay SpriteRenderer, buscar cualquier renderer (MeshRenderer, etc.)
        if (spriteRenderer == null)
        {
            visualRenderer = GetComponent<Renderer>();
            
            if (visualRenderer == null)
            {
                // MODIFICADO: En lugar de error, crear un componente visual básico
                Debug.LogWarning("ClickIndicator: No se encontró Renderer. Creando MeshRenderer básico.");
                CreateBasicVisualElement();
            }
        }
        else
        {
            // Usar SpriteRenderer como renderer visual
            visualRenderer = spriteRenderer;
        }
    }
    
    // NUEVO: Método para crear un elemento visual básico si no hay renderer
    private void CreateBasicVisualElement()
    {
        // Crear un objeto visual simple (un quad)
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.transform.SetParent(transform);
        quad.transform.localPosition = new Vector3(0, 0.01f, 0); // Ligeramente por encima del suelo
        quad.transform.localRotation = Quaternion.Euler(90, 0, 0); // Rotación para que sea horizontal
        
        // Configurar un material básico
        visualRenderer = quad.GetComponent<MeshRenderer>();
        Material material = new Material(Shader.Find("Standard"));
        material.color = indicatorColor;
        
        // Hacer el material transparente
        material.SetFloat("_Mode", 3); // Transparent mode
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000;
        
        visualRenderer.material = material;
    }
    
    private void Start()
    {
        // Inicialmente invisible
        if (visualRenderer != null)
        {
            Color tempColor = visualRenderer.material.color;
            tempColor.a = 0f;
            visualRenderer.material.color = tempColor;
        }
        
        // Inicializar con escala cero
        transform.localScale = Vector3.zero;
        
        // Desactivar el objeto inicialmente
        gameObject.SetActive(false);
    }
    
    private void Update()
    {
        if (isAnimating && visualRenderer != null)
        {
            // Incrementar timer
            animTimer += Time.deltaTime;
            
            // Calcular progreso normalizado (0-1)
            float progress = Mathf.Clamp01(animTimer / duration);
            
            // Animar escala con curva de expansión suave
            // Comenzamos desde initialSize y expandimos hasta maxRadius
            float currentSize = Mathf.Lerp(initialSize, maxRadius, Mathf.Pow(progress, expansionCurve));
            transform.localScale = new Vector3(currentSize, 0.01f, currentSize);
            
            // Animar transparencia (efecto de desvanecimiento)
            Color currentColor = visualRenderer.material.color;
            currentColor.a = Mathf.Lerp(indicatorColor.a, 0f, Mathf.Pow(progress, fadeSpeed));
            visualRenderer.material.color = currentColor;
            
            // Finalizar animación
            if (progress >= 1.0f)
            {
                isAnimating = false;
                gameObject.SetActive(false);
            }
        }
    }
    
    // Método público para mostrar el indicador en una posición
    public void ShowAt(Vector3 position)
    {
        // Si no tenemos renderer, no hacer nada
        if (visualRenderer == null) return;
        
        // Ajustar posición (ligeramente por encima del suelo para evitar z-fighting)
        position.y += 0.01f;
        transform.position = position;
        
        // Reiniciar animación
        animTimer = 0f;
        isAnimating = true;
        
        // Establecer escala inicial
        transform.localScale = new Vector3(initialSize, 0.01f, initialSize);
        
        Color tempColor = visualRenderer.material.color;
        tempColor.r = indicatorColor.r;
        tempColor.g = indicatorColor.g;
        tempColor.b = indicatorColor.b;
        tempColor.a = indicatorColor.a;
        visualRenderer.material.color = tempColor;
        
        // Activar el objeto
        gameObject.SetActive(true);
    }
}