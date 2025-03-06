using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(PlayerStats))]
public class PlayerHealEffect : NetworkBehaviour
{
    [Header("Efectos Visuales")]
    [SerializeField] private GameObject healthEffectPrefab;
    [SerializeField] private GameObject manaEffectPrefab;
    
    [Header("Configuración de Partículas")]
    [SerializeField] private float healthParticleDuration = 2f;
    [SerializeField] private float manaParticleDuration = 2f;
    [SerializeField] private float particleScale = 0.7f;
    
    // Referencia a las estadísticas del jugador
    private PlayerStats playerStats;
    
    // Variables para controlar los efectos
    private float lastHealth = 0f;
    private float lastMana = 0f;
    
    private void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Solo el cliente local debe mostrar estos efectos
        if (IsLocalPlayer)
        {
            // Guardar valores iniciales
            lastHealth = playerStats.CurrentHealth;
            lastMana = playerStats.CurrentMana;
            
            // Suscribirse a eventos de cambio
            playerStats.OnHealthChanged += HandleHealthChanged;
            playerStats.OnManaChanged += HandleManaChanged;
        }
    }
    
    public override void OnNetworkDespawn()
    {
        // Desuscribirse de eventos
        if (IsLocalPlayer)
        {
            playerStats.OnHealthChanged -= HandleHealthChanged;
            playerStats.OnManaChanged -= HandleManaChanged;
        }
        
        base.OnNetworkDespawn();
    }
    
    private void HandleHealthChanged(float currentHealth, float maxHealth)
    {
        // Solo procesar si somos el cliente local
        if (!IsLocalPlayer) return;
        
        // Verificar si hubo curación (aumento de salud)
        if (currentHealth > lastHealth)
        {
            // Calcular cantidad de curación
            float healAmount = currentHealth - lastHealth;
            
            // Mostrar efecto visual
            ShowHealEffect(healAmount);
        }
        
        // Actualizar último valor
        lastHealth = currentHealth;
    }
    
    private void HandleManaChanged(float currentMana, float maxMana)
    {
        // Solo procesar si somos el cliente local
        if (!IsLocalPlayer) return;
        
        // Verificar si hubo restauración de maná
        if (currentMana > lastMana)
        {
            // Calcular cantidad restaurada
            float manaAmount = currentMana - lastMana;
            
            // Mostrar efecto visual
            ShowManaEffect(manaAmount);
        }
        
        // Actualizar último valor
        lastMana = currentMana;
    }
    
    private void ShowHealEffect(float amount)
    {
        // Solo mostrar efecto si la cantidad es significativa
        if (amount < 1f) return;
        
        // Crear efecto visual de partículas
        if (healthEffectPrefab != null)
        {
            GameObject effect = Instantiate(healthEffectPrefab, transform.position + Vector3.up, Quaternion.identity);
            effect.transform.SetParent(transform); // Que siga al jugador
            
            // Escalar el efecto según la cantidad de curación
            float scale = Mathf.Clamp(particleScale * (amount / playerStats.MaxHealth), 0.3f, 2f);
            effect.transform.localScale = Vector3.one * scale;
            
            // Destruir después de la duración
            Destroy(effect, healthParticleDuration);
            
            // También podríamos mostrar un número flotante con la cantidad curada
            ShowFloatingText("+" + Mathf.Round(amount).ToString(), Color.green);
        }
    }
    
    private void ShowManaEffect(float amount)
    {
        // Solo mostrar efecto si la cantidad es significativa
        if (amount < 1f) return;
        
        // Crear efecto visual de partículas
        if (manaEffectPrefab != null)
        {
            GameObject effect = Instantiate(manaEffectPrefab, transform.position + Vector3.up * 0.5f, Quaternion.identity);
            effect.transform.SetParent(transform); // Que siga al jugador
            
            // Escalar el efecto según la cantidad de maná restaurado
            float scale = Mathf.Clamp(particleScale * (amount / playerStats.MaxMana), 0.3f, 2f);
            effect.transform.localScale = Vector3.one * scale;
            
            // Destruir después de la duración
            Destroy(effect, manaParticleDuration);
            
            // También podríamos mostrar un número flotante con la cantidad restaurada
            ShowFloatingText("+" + Mathf.Round(amount).ToString(), Color.blue);
        }
    }
    
    // Método para mostrar texto flotante (opcional, requiere un sistema de texto flotante)
    private void ShowFloatingText(string text, Color color)
    {
        // Implementación básica que puedes expandir o conectar con un sistema existente
        GameObject textObj = new GameObject("FloatingText");
        textObj.transform.position = transform.position + Vector3.up * 2f;
        
        // Adjuntar script de texto flotante (deberías implementar esto)
        FloatingText floatingText = textObj.AddComponent<FloatingText>();
        if (floatingText != null)
        {
            floatingText.Initialize(text, color);
        }
    }
}