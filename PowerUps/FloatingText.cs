using UnityEngine;
using TMPro;

public class FloatingText : MonoBehaviour
{
    [Header("Movimiento")]
    [SerializeField] private float moveSpeed = 1.5f;
    [SerializeField] private Vector3 direction = Vector3.up;
    [SerializeField] private AnimationCurve moveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    [Header("Apariencia")]
    [SerializeField] private float duration = 1.5f;
    [SerializeField] private float startScale = 0.5f;
    [SerializeField] private float maxScale = 1.0f;
    [SerializeField] private AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 0, 0.2f, 1);
    [SerializeField] private AnimationCurve alphaCurve = AnimationCurve.EaseInOut(0, 1, 0.7f, 0);
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Font textFont;
    
    // Referencias internas
    private TextMeshPro textMesh;
    private float timeSinceStart = 0f;
    private Vector3 startPosition;
    
    private void Awake()
    {
        // Crear componente TextMeshPro si no existe
        textMesh = GetComponent<TextMeshPro>();
        if (textMesh == null)
        {
            textMesh = gameObject.AddComponent<TextMeshPro>();
            ConfigureTextMesh();
        }
        
        // Guardar posición inicial
        startPosition = transform.position;
        
        // Inicializar escala
        transform.localScale = Vector3.one * startScale;
    }
    
    private void ConfigureTextMesh()
    {
        if (textMesh != null)
        {
            textMesh.alignment = TextAlignmentOptions.Center;
            textMesh.fontSize = 5;
            
            // Configurar para que mire a la cámara
            textMesh.transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
            
            // Asignar fuente si está disponible
            if (textFont != null)
            {
                // Nota: no se puede asignar Font directamente a TextMeshPro
                // TMP necesita un TMP_FontAsset específico
            }
        }
    }
    
    private void Update()
    {
        // Actualizar temporizador
        timeSinceStart += Time.deltaTime;
        
        // Calcular progreso normalizado (0-1)
        float progress = Mathf.Clamp01(timeSinceStart / duration);
        
        // Actualizar posición
        float moveProgress = moveCurve.Evaluate(progress);
        transform.position = startPosition + direction * moveSpeed * moveProgress;
        
        // Actualizar escala
        float scaleValue = Mathf.Lerp(startScale, maxScale, scaleCurve.Evaluate(progress));
        transform.localScale = Vector3.one * scaleValue;
        
        // Actualizar transparencia/alfa
        if (textMesh != null)
        {
            Color currentColor = textMesh.color;
            currentColor.a = alphaCurve.Evaluate(progress);
            textMesh.color = currentColor;
        }
        
        // Asegurar que el texto siempre mire a la cámara
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
    
    // Método público para inicializar el texto
    public void Initialize(string text, Color color)
    {
        if (textMesh == null)
        {
            textMesh = GetComponent<TextMeshPro>();
            if (textMesh == null)
            {
                textMesh = gameObject.AddComponent<TextMeshPro>();
                ConfigureTextMesh();
            }
        }
        
        // Asignar texto y color
        textMesh.text = text;
        textMesh.color = color;
        textColor = color;
        
        // Asegurar que el texto siempre mire a la cámara
        if (Camera.main != null)
        {
            transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
        }
    }
    
    // Método estático para crear texto flotante sin necesidad de referencias externas
    public static FloatingText Create(Vector3 position, string text, Color color)
    {
        GameObject textObj = new GameObject("FloatingText_" + text);
        textObj.transform.position = position;
        
        FloatingText floatingText = textObj.AddComponent<FloatingText>();
        floatingText.Initialize(text, color);
        
        return floatingText;
    }
}