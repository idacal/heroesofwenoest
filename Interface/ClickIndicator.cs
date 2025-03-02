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
    
    private void Awake()
    {
        // Verificar si tenemos el SpriteRenderer
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            
            if (spriteRenderer == null)
            {
                Debug.LogError("ClickIndicator: No se encontró SpriteRenderer!");
            }
        }
    }
    
    private void Start()
    {
        // Inicialmente invisible
        if (spriteRenderer != null)
        {
            spriteRenderer.color = new Color(indicatorColor.r, indicatorColor.g, indicatorColor.b, 0f);
        }
        
        // Inicializar con escala cero
        transform.localScale = Vector3.zero;
        
        // Desactivar el objeto inicialmente
        gameObject.SetActive(false);
    }
    
    private void Update()
    {
        if (isAnimating)
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
            if (spriteRenderer != null)
            {
                Color currentColor = spriteRenderer.color;
                currentColor.a = Mathf.Lerp(indicatorColor.a, 0f, Mathf.Pow(progress, fadeSpeed));
                spriteRenderer.color = currentColor;
            }
            
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
        // Ajustar posición (ligeramente por encima del suelo para evitar z-fighting)
        position.y += 0.01f;
        transform.position = position;
        
        // Reiniciar animación
        animTimer = 0f;
        isAnimating = true;
        
        // Establecer escala inicial
        transform.localScale = new Vector3(initialSize, 0.01f, initialSize);
        
        if (spriteRenderer != null)
        {
            spriteRenderer.color = indicatorColor;
        }
        
        // Activar el objeto
        gameObject.SetActive(true);
    }
}