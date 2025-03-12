using Unity.Netcode;
using UnityEngine;
using System;

public class PlayerStats : NetworkBehaviour
{
    [Header("Health Settings")]
    [SerializeField] private float maxHealth = 1000f;
    [SerializeField] private float healthRegen = 0.5f; // Regeneración por segundo
    [SerializeField] private bool enableHealthRegen = true;
    
    [Header("Mana Settings")]
    [SerializeField] private float maxMana = 500f;
    [SerializeField] private float manaRegen = 10f; // Regeneración por segundo
    [SerializeField] private bool enableManaRegen = true;

    [Header("Depuración")]
    [SerializeField] private bool showDebugMessages = true;

    // NUEVA: Variable para contar impactos recibidos (depuración)
    public NetworkVariable<int> hitCounter = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Variables de red para sincronizar vida y maná
    private NetworkVariable<float> networkHealth = new NetworkVariable<float>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkVariable<float> networkMana = new NetworkVariable<float>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    
    // Variables para manejar reducción de daño por escudos, etc.
    private NetworkVariable<float> damageReduction = new NetworkVariable<float>(
        0f, // 0% reducción por defecto
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    
    // Eventos para notificar cambios en vida y maná
    public event Action<float, float> OnHealthChanged; // current, max
    public event Action<float, float> OnManaChanged; // current, max
    public event Action<float> OnTakeDamage; // amount before reduction
    public event Action<float> OnDamageReductionChanged; // current reduction factor

    // Propiedades para acceder a los valores
    public float CurrentHealth => networkHealth.Value;
    public float MaxHealth => maxHealth;
    public float CurrentMana => networkMana.Value;
    public float MaxMana => maxMana;
    public float CurrentDamageReduction => damageReduction.Value;
    
    // Variables para controlar la regeneración
    private float lastRegenTime;
    
    // Variables para efectos visuales de daño
    private float lastDamageTime = 0f;
    private static readonly float damageIndicatorCooldown = 0.1f; // Evitar spam de indicadores

    public override void OnNetworkSpawn()
    {
        // Inicializar salud y maná al máximo cuando se spawne el jugador
        if (IsServer)
        {
            networkHealth.Value = maxHealth;
            networkMana.Value = maxMana;
            damageReduction.Value = 0f;
            hitCounter.Value = 0;
            
            Debug.Log($"[PlayerStats] Jugador {OwnerClientId} inicializado con {maxHealth} HP y {maxMana} MP");
        }
        
        // Suscribirse a cambios en las variables de red
        networkHealth.OnValueChanged += OnHealthValueChanged;
        networkMana.OnValueChanged += OnManaValueChanged;
        damageReduction.OnValueChanged += OnDamageReductionValueChanged;
        hitCounter.OnValueChanged += OnHitCounterChanged;
        
        // Inicializar tiempo para regeneración
        lastRegenTime = Time.time;
        
        // Notificar valores iniciales a la UI
        OnHealthChanged?.Invoke(networkHealth.Value, maxHealth);
        OnManaChanged?.Invoke(networkMana.Value, maxMana);
        OnDamageReductionChanged?.Invoke(damageReduction.Value);
    }
    
    public override void OnNetworkDespawn()
    {
        // Desuscribirse de eventos
        networkHealth.OnValueChanged -= OnHealthValueChanged;
        networkMana.OnValueChanged -= OnManaValueChanged;
        damageReduction.OnValueChanged -= OnDamageReductionValueChanged;
        hitCounter.OnValueChanged -= OnHitCounterChanged;
    }
    
    private void OnHitCounterChanged(int previousValue, int newValue)
    {
        if (IsOwner && showDebugMessages)
        {
            Debug.Log($"[PlayerStats] ¡IMPACTO RECIBIDO! Total de impactos: {newValue}");
        }
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
        
        // Mostrar información de daño si hubo una reducción de salud
        if (newValue < previousValue && IsOwner && showDebugMessages)
        {
            float damageTaken = previousValue - newValue;
            Debug.Log($"<color=red>DAÑO RECIBIDO: {damageTaken:F1}</color> - Vida restante: {newValue:F1}/{maxHealth}");
        }
    }
    
    private void OnManaValueChanged(float previousValue, float newValue)
    {
        // Notificar a los componentes que se han suscrito al evento
        OnManaChanged?.Invoke(newValue, maxMana);
    }
    
    private void OnDamageReductionValueChanged(float previousValue, float newValue)
    {
        OnDamageReductionChanged?.Invoke(newValue);
        Debug.Log($"[PlayerStats] Reducción de daño del jugador {OwnerClientId} cambió de {previousValue*100}% a {newValue*100}%");
    }

    // Método para que el servidor aplique daño al jugador
    public void TakeDamage(float amount)
    {
        if (!IsServer) return; // Solo el servidor puede modificar directamente estos valores
        
        // Incrementar contador de golpes (para depuración)
        hitCounter.Value++;
        
        // Notificar del daño inicial (antes de reducciones)
        OnTakeDamage?.Invoke(amount);
        
        // Aplicar reducción de daño si hay alguna activa
        float reducedAmount = amount * (1f - damageReduction.Value);
        
        // Calcular nueva salud y asegurar que no baje de 0
        float newHealth = Mathf.Max(networkHealth.Value - reducedAmount, 0f);
        float previousHealth = networkHealth.Value;
        networkHealth.Value = newHealth;
        
        // Mostrar efecto visual de daño para todos
        ShowDamageEffectClientRpc(reducedAmount, transform.position);
        
        Debug.Log($"[PlayerStats] Jugador {OwnerClientId} recibió {amount:F1} de daño (reducido a {reducedAmount:F1}). " +
                  $"Vida: {previousHealth:F1} -> {newHealth:F1}, Golpes recibidos: {hitCounter.Value}");
        
        // Verificar muerte (añadir lógica de muerte si la salud llega a 0)
        if (newHealth <= 0)
        {
            HandlePlayerDeath();
        }
    }
    
    // Método para manejar la muerte del jugador
    private void HandlePlayerDeath()
    {
        Debug.Log($"[PlayerStats] Jugador {OwnerClientId} ha muerto!");
        
        // Notificar a todos los clientes sobre la muerte
        NotifyDeathClientRpc();
        
        // Forzar respawn si tenemos el componente
        PlayerRespawnController respawnController = GetComponent<PlayerRespawnController>();
        if (respawnController != null)
        {
            respawnController.ForceRespawn();
        }
        else
        {
            Debug.LogError($"[PlayerStats] No se encontró PlayerRespawnController en el jugador {OwnerClientId}");
        }
    }

    [ClientRpc]
    private void NotifyDeathClientRpc()
    {
        if (IsOwner)
        {
            // Efectos locales para el jugador que murió
            Debug.Log("<color=red><size=20>¡HAS MUERTO!</size></color>");
            
            // Sacudir la cámara
            ShakeOwnerCamera();
        }
        
        // Efectos visuales en el jugador (para todos los clientes)
        StartCoroutine(PlayerDeathEffect());
    }

    private void ShakeOwnerCamera()
    {
        // Intentar encontrar la cámara del jugador
        // Método 1: Intentar con la Main Camera
        Camera camera = Camera.main;
        if (camera != null)
        {
            MOBACamera mobaCamera = camera.GetComponent<MOBACamera>();
            if (mobaCamera != null)
            {
                mobaCamera.ShakeCamera(0.8f, 0.7f);
                return;
            }
        }
        
        // Método 2: Buscar todas las MOBACameras y verificar su objetivo
        MOBACamera[] allCameras = FindObjectsOfType<MOBACamera>();
        foreach (MOBACamera mobaCam in allCameras)
        {
            if (mobaCam.GetTarget() == transform)
            {
                mobaCam.ShakeCamera(0.8f, 0.7f);
                return;
            }
        }
        
        Debug.LogWarning("[PlayerStats] No se pudo encontrar una cámara para aplicar el efecto de shake");
    }

    private System.Collections.IEnumerator PlayerDeathEffect()
    {
        // Obtener todos los renderers adjuntos al jugador
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        Material[] originalMaterials = new Material[renderers.Length];
        
        // Crear un material de muerte (rojo brillante)
        Material deathMaterial = new Material(Shader.Find("Standard"));
        deathMaterial.color = Color.red;
        deathMaterial.EnableKeyword("_EMISSION");
        deathMaterial.SetColor("_EmissionColor", Color.red * 3f);
        
        // Aplicar material de muerte a todos los renderers
        for (int i = 0; i < renderers.Length; i++)
        {
            // Guardar material original
            originalMaterials[i] = renderers[i].material;
            
            // Aplicar material de muerte
            renderers[i].material = deathMaterial;
        }
        
        // Crear efecto de explosión
        GameObject explosionEffect = new GameObject("DeathExplosion");
        explosionEffect.transform.position = transform.position + Vector3.up;
        
        // Añadir sistema de partículas
        ParticleSystem particles = explosionEffect.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.startSpeed = 5.0f;
        main.startSize = 0.5f;
        main.startLifetime = 1.5f;
        main.startColor = Color.red;
        
        // Configurar forma de emisión
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.5f;
        
        // Configurar burst de partículas
        var emission = particles.emission;
        emission.rateOverTime = 0;
        var burst = new ParticleSystem.Burst(0.0f, 50);
        emission.SetBurst(0, burst);
        
        // Configurar tamaño a lo largo de la vida
        var sizeOverLifetime = particles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeOverLifetimeCurve = new AnimationCurve();
        sizeOverLifetimeCurve.AddKey(0.0f, 1.0f);
        sizeOverLifetimeCurve.AddKey(1.0f, 0.0f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1.0f, sizeOverLifetimeCurve);
        
        // Añadir luz
        Light light = explosionEffect.AddComponent<Light>();
        light.color = Color.red;
        light.intensity = 5.0f;
        light.range = 8.0f;
        
        // Hacer que la luz parpadee
        StartCoroutine(FlashLight(light));
        
        // Esperar un momento
        yield return new WaitForSeconds(1.5f);
        
        // Restaurar los materiales originales si el jugador sigue vivo
        if (this != null && gameObject.activeInHierarchy)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && originalMaterials[i] != null)
                {
                    renderers[i].material = originalMaterials[i];
                }
            }
        }
        
        // Destruir el efecto después de un tiempo
        Destroy(explosionEffect, 2.0f);
    }

    private System.Collections.IEnumerator FlashLight(Light light)
    {
        float duration = 1.5f;
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            light.intensity = Mathf.Lerp(5.0f, 0f, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    // Método público para curar al jugador (desde el servidor)
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
    
    // Método para establecer la reducción de daño
    public void SetDamageReduction(float reduction)
    {
        if (!IsServer)
        {
            SetDamageReductionServerRpc(reduction);
            return;
        }
        
        // Clamping entre 0 y 1 (0% a 100%)
        damageReduction.Value = Mathf.Clamp01(reduction);
    }
    
    // Método para restablecer la reducción de daño a 0
    public void ResetDamageReduction()
    {
        if (!IsServer)
        {
            ResetDamageReductionServerRpc();
            return;
        }
        
        damageReduction.Value = 0f;
    }

    // RPCs para clientes
    [ServerRpc]
    public void TakeDamageServerRpc(float amount)
    {
        TakeDamage(amount);
    }

    [ServerRpc]
    public void UseManaServerRpc(float amount)
    {
        UseMana(amount);
    }
    
    [ServerRpc]
    public void HealServerRpc(float amount)
    {
        Heal(amount);
    }
    
    [ServerRpc]
    public void RestoreManaServerRpc(float amount)
    {
        RestoreMana(amount);
    }
    
    [ServerRpc]
    private void SetDamageReductionServerRpc(float reduction)
    {
        SetDamageReduction(reduction);
    }
    
    [ServerRpc]
    private void ResetDamageReductionServerRpc()
    {
        ResetDamageReduction();
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
    
    // Efectos visuales de daño
    [ClientRpc]
    private void ShowDamageEffectClientRpc(float amount, Vector3 position)
    {
        // No mostrar indicadores demasiado rápido (spam)
        if (Time.time - lastDamageTime < damageIndicatorCooldown)
            return;
        
        lastDamageTime = Time.time;
        
        // Decidir si es golpe crítico (ejemplo simplificado)
        bool isCritical = UnityEngine.Random.value < 0.2f; // 20% de probabilidad
        
        // Crear indicador de daño flotante
        DamageIndicator.Create(position, amount, isCritical);
        
        // Si hay un efecto de daño disponible, mostrarlo
        HitEffect.Create(position, Color.red);
        
        if (IsOwner && showDebugMessages)
        {
            // El jugador que recibe daño verá este mensaje
            string criticalText = isCritical ? "¡CRÍTICO! " : "";
            Debug.Log($"<color=red>{criticalText}Recibiste {amount:F1} de daño</color>");
        }
    }

    [ClientRpc]
    private void ShowDeathEffectClientRpc(Vector3 position)
    {
        // Aquí podrías instanciar un efecto de muerte más dramático
        
        if (IsOwner)
        {
            // Mostrar mensaje más grande para el jugador que murió
            Debug.Log("<color=red><size=20>¡HAS MUERTO!</size></color>");
        }
    }
}