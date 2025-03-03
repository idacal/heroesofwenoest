using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerUI : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private PlayerStats playerStats;
    
    [Header("Health UI")]
    [SerializeField] private Image healthBarFill;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private Color fullHealthColor = Color.green;
    [SerializeField] private Color lowHealthColor = Color.red;
    
    [Header("Mana UI")]
    [SerializeField] private Image manaBarFill;
    [SerializeField] private TextMeshProUGUI manaText;
    [SerializeField] private Color manaColor = new Color(0f, 0.5f, 1f);
    
    [Header("Opciones")]
    [SerializeField] private float uiUpdateInterval = 0.5f; // En segundos, para forzar actualizaciones
    [SerializeField] private float barAnimationSpeed = 5f; // Velocidad de la animación de las barras
    
    // Valores objetivo para animación suave
    private float targetHealthFill = 1f;
    private float targetManaFill = 1f;
    
    private float lastUIUpdateTime;
    private bool hasFoundPlayerStats = false;

    private void Start()
    {
        Debug.Log("[PlayerUI] Start method called");
        Debug.Log($"[PlayerUI] PlayerStats reference: {playerStats}");
        
        lastUIUpdateTime = Time.time;
        FindPlayerStats();
        Invoke("DelayedFindPlayerStats", 1f);
    }
    
    private void DelayedFindPlayerStats()
    {
        if (!hasFoundPlayerStats)
        {
            Debug.Log("[PlayerUI] Realizando búsqueda retrasada de PlayerStats...");
            FindPlayerStats();
            
            // Si aún no lo encontramos, programar otro intento
            if (!hasFoundPlayerStats)
            {
                Debug.Log("[PlayerUI] PlayerStats no encontrado, intentando nuevamente en 2 segundos...");
                Invoke("DelayedFindPlayerStats", 2f);
            }
        }
    }
    
    private void FindPlayerStats()
    {
        Debug.Log("[PlayerUI] FindPlayerStats method called");
        
        if (playerStats == null)
        {
            PlayerNetwork[] players = FindObjectsOfType<PlayerNetwork>();
            
            Debug.Log($"[PlayerUI] Found {players.Length} PlayerNetwork objects");
            
            foreach (PlayerNetwork player in players)
            {
                Debug.Log($"[PlayerUI] Checking player: {player.gameObject.name}, IsLocalPlayer: {player.IsLocalPlayer}");
                
                if (player.IsLocalPlayer)
                {
                    playerStats = player.GetComponent<PlayerStats>();
                    
                    if (playerStats != null)
                    {
                        Debug.Log("[PlayerUI] PlayerStats found successfully");
                        hasFoundPlayerStats = true;
                        
                        // Suscribirse a los eventos de cambio
                        playerStats.OnHealthChanged += UpdateHealthUI;
                        playerStats.OnManaChanged += UpdateManaUI;
                        
                        // Inicializar los valores de UI
                        InitializeUI();
                        
                        break;
                    }
                    else
                    {
                        Debug.LogError("[PlayerUI] No PlayerStats component found on local player");
                    }
                }
            }
            
            if (playerStats == null)
            {
                Debug.LogWarning("[PlayerUI] No se pudo encontrar el PlayerStats del jugador local");
            }
        }
    }
    
    private void Update()
    {
        // Si aún no hemos encontrado PlayerStats, intentar periódicamente
        if (!hasFoundPlayerStats && Time.time - lastUIUpdateTime >= uiUpdateInterval)
        {
            FindPlayerStats();
            lastUIUpdateTime = Time.time;
        }
        
        // Si tenemos PlayerStats pero los eventos no funcionan, forzar actualización periódica
        if (hasFoundPlayerStats && Time.time - lastUIUpdateTime >= uiUpdateInterval)
        {
            // Forzar una actualización
            if (healthBarFill != null && manaBarFill != null)
            {
                UpdateHealthUI(playerStats.CurrentHealth, playerStats.MaxHealth);
                UpdateManaUI(playerStats.CurrentMana, playerStats.MaxMana);
            }
            
            lastUIUpdateTime = Time.time;
        }
        
        // Animar las barras suavemente
        AnimateBars();
    }
    
    private void AnimateBars()
    {
        // Animar la barra de vida
        if (healthBarFill != null)
        {
            healthBarFill.fillAmount = Mathf.Lerp(healthBarFill.fillAmount, targetHealthFill, Time.deltaTime * barAnimationSpeed);
        }
        
        // Animar la barra de mana
        if (manaBarFill != null)
        {
            manaBarFill.fillAmount = Mathf.Lerp(manaBarFill.fillAmount, targetManaFill, Time.deltaTime * barAnimationSpeed);
        }
    }
    
    private void InitializeUI()
    {
        Debug.Log("[PlayerUI] Inicializando UI con valores actuales...");
        
        if (playerStats == null)
        {
            Debug.LogError("[PlayerUI] PlayerStats es null en InitializeUI");
            return;
        }
        
        // Verificar que los componentes de UI existan
        if (healthBarFill == null)
        {
            Debug.LogError("[PlayerUI] healthBarFill no está asignado");
        }
        else
        {
            float healthRatio = Mathf.Clamp01(playerStats.CurrentHealth / playerStats.MaxHealth);
            targetHealthFill = healthRatio;
            healthBarFill.fillAmount = healthRatio;
            healthBarFill.color = Color.Lerp(lowHealthColor, fullHealthColor, healthRatio);
            
            Debug.Log($"[PlayerUI] Barra de vida inicializada - Ratio: {healthRatio}");
        }
        
        if (manaBarFill == null)
        {
            Debug.LogError("[PlayerUI] manaBarFill no está asignado");
        }
        else
        {
            float manaRatio = Mathf.Clamp01(playerStats.CurrentMana / playerStats.MaxMana);
            targetManaFill = manaRatio;
            manaBarFill.fillAmount = manaRatio;
            manaBarFill.color = manaColor;
            
            Debug.Log($"[PlayerUI] Barra de maná inicializada - Ratio: {manaRatio}");
        }
        
        // Actualizar textos
        UpdateHealthUI(playerStats.CurrentHealth, playerStats.MaxHealth);
        UpdateManaUI(playerStats.CurrentMana, playerStats.MaxMana);
    }
    
    private void OnDestroy()
    {
        // Desuscribirse de los eventos cuando se destruya el UI
        if (playerStats != null)
        {
            playerStats.OnHealthChanged -= UpdateHealthUI;
            playerStats.OnManaChanged -= UpdateManaUI;
        }
    }
    
    private void UpdateHealthUI(float currentHealth, float maxHealth)
    {
        // Calcular el porcentaje de vida de manera más explícita
        float healthRatio = Mathf.Clamp01(currentHealth / maxHealth);
        
        // Establecer directamente el target fill para la animación
        targetHealthFill = healthRatio;
        
        Debug.Log($"[PlayerUI] Actualizando barra de vida - Actual: {currentHealth}, Max: {maxHealth}, Ratio: {healthRatio}");
        
        if (healthText != null)
        {
            healthText.text = $"{Mathf.Ceil(currentHealth)}/{maxHealth}";
        }
        
        if (healthBarFill != null)
        {
            // Cambiar el color según la cantidad de vida
            healthBarFill.color = Color.Lerp(lowHealthColor, fullHealthColor, healthRatio);
        }
    }
    
    private void UpdateManaUI(float currentMana, float maxMana)
    {
        // Calcular el porcentaje de maná de manera más explícita
        float manaRatio = Mathf.Clamp01(currentMana / maxMana);
        
        // Establecer directamente el target fill para la animación
        targetManaFill = manaRatio;
        
        Debug.Log($"[PlayerUI] Actualizando barra de maná - Actual: {currentMana}, Max: {maxMana}, Ratio: {manaRatio}");
        
        if (manaText != null)
        {
            manaText.text = $"{Mathf.Ceil(currentMana)}/{maxMana}";
        }
    }
    
    // Método para asignar el PlayerStats desde otro script
    public void SetPlayerStats(PlayerStats stats)
    {
        Debug.Log("[PlayerUI] SetPlayerStats llamado");
        
        if (stats != null)
        {
            // Desuscribirse del anterior si existe
            if (playerStats != null)
            {
                playerStats.OnHealthChanged -= UpdateHealthUI;
                playerStats.OnManaChanged -= UpdateManaUI;
            }
            
            playerStats = stats;
            hasFoundPlayerStats = true;
            
            // Suscribirse a los eventos del nuevo PlayerStats
            playerStats.OnHealthChanged += UpdateHealthUI;
            playerStats.OnManaChanged += UpdateManaUI;
            
            // Inicializar UI con valores actuales
            InitializeUI();
            
            Debug.Log("[PlayerUI] PlayerStats asignado correctamente");
        }
        else
        {
            Debug.LogError("[PlayerUI] Se intentó asignar un PlayerStats null");
        }
    }
}