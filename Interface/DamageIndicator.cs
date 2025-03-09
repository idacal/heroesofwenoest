using UnityEngine;
using TMPro;

public class DamageIndicator : MonoBehaviour
{
    [Header("Movimiento")]
    [SerializeField] private float moveSpeed = 1.0f;
    [SerializeField] private Vector3 direction = new Vector3(0, 1, 0);
    [SerializeField] private AnimationCurve moveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    [Header("Apariencia")]
    [SerializeField] private float duration = 1.2f;
    [SerializeField] private float startScale = 0.5f;
    [SerializeField] private float maxScale = 1.2f;
    [SerializeField] private AnimationCurve scaleCurve = AnimationCurve.Linear(0, 0, 0.3f, 1);
    [SerializeField] private AnimationCurve alphaCurve = AnimationCurve.Linear(0, 1, 1, 0);
    
    [Header("Configuración")]
    [SerializeField] private bool useCriticalHitEffect = true;
    [SerializeField] private float criticalHitScaleMultiplier = 1.5f;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color criticalColor = Color.red;
    [SerializeField] private bool addRandomOffset = true;
    [SerializeField] private float randomOffset = 0.5f;
    
    // Referencias internas
    private TextMeshPro textMesh;
    private float timeSinceStart = 0f;
    private Vector3 startPosition;
    private bool isCriticalHit = false;
    private float damage = 0;
    
    private void Awake()
    {
        // Crear componente TextMeshPro si no existe
        textMesh = GetComponent<TextMeshPro>();
        if (textMesh == null)
        {
            textMesh = gameObject.AddComponent<TextMeshPro>();
            ConfigureTextMesh();
        }
        
        // Añadir offset aleatorio si está configurado
        if (addRandomOffset)
        {
            Vector3 offset = new Vector3(
                UnityEngine.Random.Range(-randomOffset, randomOffset),
                0,
                UnityEngine.Random.Range(-randomOffset, randomOffset)
            );
            
            transform.position += offset;
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
        float targetScale = isCriticalHit ? maxScale * criticalHitScaleMultiplier : maxScale;
        float scaleValue = Mathf.Lerp(startScale, targetScale, scaleCurve.Evaluate(progress));
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
    
    // Método para inicializar el indicador de daño
    public void Initialize(float damageAmount, bool critical = false)
    {
        damage = damageAmount;
        isCriticalHit = critical;
        
        if (textMesh == null)
        {
            textMesh = GetComponent<TextMeshPro>();
            if (textMesh == null)
            {
                textMesh = gameObject.AddComponent<TextMeshPro>();
                ConfigureTextMesh();
            }
        }
        
        // Formatear el texto con el daño
        textMesh.text = Mathf.FloorToInt(damageAmount).ToString();
        
        // Aplicar color según si es crítico o no
        if (critical && useCriticalHitEffect)
        {
            textMesh.color = criticalColor;
            textMesh.text = damageAmount.ToString() + "!"; // Añadir símbolo de exclamación
            
            // Opcional: añadir texto "CRIT!"
            // textMesh.text = damageAmount.ToString() + "\nCRIT!";
        }
        else
        {
            textMesh.color = normalColor;
        }
    }
    
    // Método estático para crear un indicador de daño fácilmente
    public static DamageIndicator Create(Vector3 position, float damageAmount, bool critical = false)
    {
        GameObject obj = new GameObject("DamageIndicator");
        obj.transform.position = position + Vector3.up * 1.5f; // Ajustar altura
        
        DamageIndicator indicator = obj.AddComponent<DamageIndicator>();
        indicator.Initialize(damageAmount, critical);
        
        return indicator;
    }
}