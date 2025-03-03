using Unity.Netcode;
using UnityEngine;
using System;

public class PlayerStats : NetworkBehaviour
{
    [Header("Health Settings")]
    [SerializeField] private float maxHealth = 1000f;
    [SerializeField] private float healthRegen = 5f; // Regeneración por segundo
    [SerializeField] private bool enableHealthRegen = true;
    
    [Header("Mana Settings")]
    [SerializeField] private float maxMana = 500f;
    [SerializeField] private float manaRegen = 10f; // Regeneración por segundo
    [SerializeField] private bool enableManaRegen = true;

    // Variables de red para sincronizar vida y maná
    private NetworkVariable<float> networkHealth = new NetworkVariable<float>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkVariable<float> networkMana = new NetworkVariable<float>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    
    // Eventos para notificar cambios en vida y maná
    public event Action<float, float> OnHealthChanged; // current, max
    public event Action<float, float> OnManaChanged; // current, max

    // Propiedades para acceder a los valores
    public float CurrentHealth => networkHealth.Value;
    public float MaxHealth => maxHealth;
    public float CurrentMana => networkMana.Value;
    public float MaxMana => maxMana;
    
    // Variables para controlar la regeneración
    private float lastRegenTime;

    public override void OnNetworkSpawn()
    {
        // Inicializar salud y maná al máximo cuando se spawne el jugador
        if (IsServer)
        {
            networkHealth.Value = maxHealth;
            networkMana.Value = maxMana;
            
            Debug.Log($"[PlayerStats] Jugador {OwnerClientId} inicializado con {maxHealth} HP y {maxMana} MP");
        }
        
        // Suscribirse a cambios en las variables de red
        networkHealth.OnValueChanged += OnHealthValueChanged;
        networkMana.OnValueChanged += OnManaValueChanged;
        
        // Inicializar tiempo para regeneración
        lastRegenTime = Time.time;
        
        // Notificar valores iniciales a la UI
        OnHealthChanged?.Invoke(networkHealth.Value, maxHealth);
        OnManaChanged?.Invoke(networkMana.Value, maxMana);
    }
    
    public override void OnNetworkDespawn()
    {
        // Desuscribirse de eventos
        networkHealth.OnValueChanged -= OnHealthValueChanged;
        networkMana.OnValueChanged -= OnManaValueChanged;
    }
    
    private void Update()
    {
        // Solo el servidor maneja la regeneración
        if (IsServer)
        {
            RegenerateResources();
        }
    }
    
    private void RegenerateResources()
    {
        float currentTime = Time.time;
        float deltaTime = currentTime - lastRegenTime;
        
        if (deltaTime > 0.1f) // Actualizar cada 0.1 segundos para suavidad
        {
            // Regenerar vida si está activado
            if (enableHealthRegen && networkHealth.Value < maxHealth)
            {
                float newHealth = Mathf.Min(networkHealth.Value + (healthRegen * deltaTime), maxHealth);
                networkHealth.Value = newHealth;
            }
            
            // Regenerar maná si está activado
            if (enableManaRegen && networkMana.Value < maxMana)
            {
                float newMana = Mathf.Min(networkMana.Value + (manaRegen * deltaTime), maxMana);
                networkMana.Value = newMana;
            }
            
            lastRegenTime = currentTime;
        }
    }
    
    // Callbacks para las variables de red
    private void OnHealthValueChanged(float previousValue, float newValue)
    {
        // Notificar a los componentes que se han suscrito al evento
        OnHealthChanged?.Invoke(newValue, maxHealth);
    }
    
    private void OnManaValueChanged(float previousValue, float newValue)
    {
        // Notificar a los componentes que se han suscrito al evento
        OnManaChanged?.Invoke(newValue, maxMana);
    }

    // Método para que el servidor aplique daño al jugador
    public void TakeDamage(float amount)
    {
        if (!IsServer) return; // Solo el servidor puede modificar directamente estos valores
        
        // Calcular nueva salud y asegurar que no baje de 0
        float newHealth = Mathf.Max(networkHealth.Value - amount, 0f);
        networkHealth.Value = newHealth;
        
        Debug.Log($"[PlayerStats] Jugador {OwnerClientId} recibió {amount} de daño. Vida restante: {newHealth}");
    }

    // Método para que el servidor consuma maná
    public bool UseMana(float amount)
    {
        if (!IsServer) return false;
        
        // Verificar si hay suficiente maná
        if (networkMana.Value >= amount)
        {
            networkMana.Value -= amount;
            Debug.Log($"[PlayerStats] Jugador {OwnerClientId} usó {amount} de maná. Maná restante: {networkMana.Value}");
            return true;
        }
        
        Debug.Log($"[PlayerStats] Jugador {OwnerClientId} intentó usar {amount} de maná pero solo tiene {networkMana.Value}");
        return false;
    }
    
    // Método para curar al jugador (desde el servidor)
    public void Heal(float amount)
    {
        if (!IsServer) return;
        
        float newHealth = Mathf.Min(networkHealth.Value + amount, maxHealth);
        networkHealth.Value = newHealth;
    }
    
    // Método para restaurar maná (desde el servidor)
    public void RestoreMana(float amount)
    {
        if (!IsServer) return;
        
        float newMana = Mathf.Min(networkMana.Value + amount, maxMana);
        networkMana.Value = newMana;
    }

    // Permitir a los clientes solicitar tomar daño (para pruebas)
    [ServerRpc]
    public void TakeDamageServerRpc(float amount)
    {
        TakeDamage(amount);
    }

    // Permitir a los clientes solicitar usar maná (para pruebas)
    [ServerRpc]
    public void UseManaServerRpc(float amount)
    {
        UseMana(amount);
    }
    
    // Permitir a los clientes solicitar curación (para pruebas)
    [ServerRpc]
    public void HealServerRpc(float amount)
    {
        Heal(amount);
    }
    
    // Permitir a los clientes solicitar restauración de maná (para pruebas)
    [ServerRpc]
    public void RestoreManaServerRpc(float amount)
    {
        RestoreMana(amount);
    }
}