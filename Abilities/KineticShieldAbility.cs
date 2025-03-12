using Unity.Netcode;
using UnityEngine;
using System.Collections;

namespace PlayerAbilities
{
public class KineticShieldAbility : BaseAbility
{
    [Header("Configuración de Escudo Cinético")]
    [SerializeField] private float baseDamageReduction = 0.3f; // 30% reducción base
    [SerializeField] private float maxDamageReduction = 0.6f; // 60% máxima reducción
    [SerializeField] private float shieldDuration = 5f;
    [SerializeField] private float minimumSpeed = 1f; // Velocidad mínima para activar bonus
    [SerializeField] private float maxSpeedBonus = 10f; // Velocidad para máximo bonus
    
    [Header("Reflejo de Daño")]
    [SerializeField] private bool enableDamageReflection = true;
    [SerializeField] private float reflectionPercent = 0.25f; // 25% del daño reflejado
    [SerializeField] private GameObject reflectionEffectPrefab;
    
    // Estado del escudo
    private bool isShieldActive = false;
    private float shieldEndTime = 0f;
    private float currentDamageReduction = 0f;
    
    // Componente de efecto visual
    private SimpleShieldEffect visualEffect;
    
    // Referencia al PlayerNetwork para obtener la velocidad
    private PlayerNetwork playerNetwork;
    private DashAbility dashAbility;
    
    // Para prevenir múltiples activaciones del cooldown
    private bool needsDelayedCooldown = true;
    
    public override void Initialize(NetworkBehaviour owner)
    {
        base.Initialize(owner);
        abilityName = "Escudo Cinético";
        activationKey = KeyCode.E;
        manaCost = 70f;
        cooldown = 10f;
        
        // Obtener referencias adicionales
        playerNetwork = owner.GetComponent<PlayerNetwork>();
        dashAbility = owner.GetComponent<DashAbility>();
    }
    
    public override bool CanActivate()
    {
        // No permitir activar si ya está activo
        if (isShieldActive)
        {
            if (networkOwner.IsOwner)
            {
                Debug.Log("El escudo cinético ya está activo");
            }
            return false;
        }
        
        return isReady && playerStats.CurrentMana >= manaCost;
    }
    
    public override void Activate()
    {
        // Guardar estado para reiniciar el cooldown más tarde
        needsDelayedCooldown = true;
        
        Debug.Log($"[KineticShieldAbility] Activando escudo cinético");
        
        // Activar el escudo
        isShieldActive = true;
        shieldEndTime = Time.time + shieldDuration;
        
        // Calcular reducción de daño inicial basada en velocidad actual
        UpdateDamageReductionBasedOnSpeed();
        
        // Activar efecto visual en todos los clientes
        ActivateVisualEffectServerRpc();
        
        // Aplicar reducción de daño usando PlayerStats
        playerStats.SetDamageReduction(currentDamageReduction);
        
        // Suscribirse al evento de daño para reflejar
        if (enableDamageReflection && playerStats != null)
        {
            playerStats.OnTakeDamage += ReflectDamageToAttacker;
        }
        
        if (networkOwner.IsOwner)
        {
            Debug.Log($"¡Escudo cinético activado! Reducción inicial: {currentDamageReduction*100:F0}%, Duración: {shieldDuration} segundos");
        }
        
        // Iniciar corrutina para actualizar la reducción de daño basada en velocidad
        StartCoroutine(UpdateShieldStrength());
        
        // Iniciar corrutina para desactivar automáticamente
        StartCoroutine(DeactivateShieldAfterDuration());
        
        // Iniciar corrutina que resetea el cooldown después de un pequeño delay
        StartCoroutine(ResetCooldownAfterDelay(0.5f));
    }
    
    private IEnumerator UpdateShieldStrength()
    {
        while (isShieldActive)
        {
            // Actualizar reducción de daño según velocidad cada 0.2 segundos
            UpdateDamageReductionBasedOnSpeed();
            yield return new WaitForSeconds(0.2f);
        }
    }
    
    private void UpdateDamageReductionBasedOnSpeed()
    {
        float currentSpeed = 0f;
        
        // Obtener velocidad actual
        if (playerNetwork != null && playerNetwork.IsMoving())
        {
            // Si estamos en dash, usar directamente la velocidad del dash
            if (dashAbility != null && dashAbility.IsDashing)
            {
                currentSpeed = maxSpeedBonus; // Asumir velocidad máxima durante dash
            }
            else
            {
                // Calcular velocidad basada en el PlayerNetwork
                Vector3 currentVelocity = playerNetwork.transform.GetComponent<Rigidbody>()?.velocity ?? Vector3.zero;
                currentSpeed = currentVelocity.magnitude;
            }
        }
        
        // Calcular factor de velocidad (0-1)
        float speedFactor = Mathf.Clamp01((currentSpeed - minimumSpeed) / (maxSpeedBonus - minimumSpeed));
        
        // Calcular reducción de daño interpolando entre base y máxima
        currentDamageReduction = Mathf.Lerp(baseDamageReduction, maxDamageReduction, speedFactor);
        
        // Actualizar la reducción de daño si el escudo está activo
        if (isShieldActive && playerStats != null)
        {
            playerStats.SetDamageReduction(currentDamageReduction);
            
            // Actualizar el color del escudo según la fuerza
            UpdateShieldVisual(speedFactor);
        }
        
        if (networkOwner.IsOwner && Time.frameCount % 60 == 0) // Solo logear cada 60 frames para no saturar
        {
            Debug.Log($"[KineticShield] Velocidad: {currentSpeed:F1}, Reducción: {currentDamageReduction*100:F0}%");
        }
    }
    
    private void UpdateShieldVisual(float intensityFactor)
    {
        if (visualEffect != null)
        {
            // Cambiar el color del escudo según la intensidad
            // Del azul claro (baja) al azul brillante (alta)
            Color baseColor = new Color(0, 0.5f, 1f, 0.4f); // Azul claro
            Color maxColor = new Color(0.2f, 0.8f, 1f, 0.6f); // Azul brillante
            
            Color newColor = Color.Lerp(baseColor, maxColor, intensityFactor);
            
            // Usar reflection para modificar el color ya que el campo puede ser privado
            System.Reflection.FieldInfo colorField = typeof(SimpleShieldEffect).GetField("shieldColor", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                
            if (colorField != null)
            {
                colorField.SetValue(visualEffect, newColor);
            }
        }
    }
    
    private void ReflectDamageToAttacker(float damageAmount)
    {
        if (!isShieldActive || !enableDamageReflection) return;
        
        // Solo reflejar daño si estamos en movimiento
        if (playerNetwork != null && playerNetwork.IsMoving())
        {
            float damageToReflect = damageAmount * reflectionPercent;
            
            // Aquí implementaríamos la lógica para devolver daño al atacante
            // Esto requeriría conocer quién fue el atacante, que podríamos obtener 
            // extendiendo el evento de OnTakeDamage para incluir la fuente
            
            if (networkOwner.IsOwner)
            {
                Debug.Log($"[KineticShield] Reflejando {damageToReflect:F0} de daño al atacante");
            }
            
            // Mostrar efecto visual de reflejo
            if (reflectionEffectPrefab != null)
            {
                SpawnReflectionEffectServerRpc();
            }
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void SpawnReflectionEffectServerRpc()
    {
        SpawnReflectionEffectClientRpc();
    }
    
    [ClientRpc]
    private void SpawnReflectionEffectClientRpc()
    {
        if (reflectionEffectPrefab != null)
        {
            GameObject effect = Instantiate(reflectionEffectPrefab, transform.position, Quaternion.identity);
            effect.transform.SetParent(transform);
            Destroy(effect, 1.5f);
        }
    }
    
    // Nueva corrutina para resetear el cooldown después de un pequeño delay
    private IEnumerator ResetCooldownAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // Solo hacemos esto si el cooldown se activó (lo que debería ocurrir en PlayerAbilityController)
        if (!isReady && needsDelayedCooldown)
        {
            Debug.Log($"[KineticShieldAbility] Reseteando cooldown para permitir uso durante el efecto");
            isReady = true;
            needsDelayedCooldown = false;
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void ActivateVisualEffectServerRpc()
    {
        ActivateVisualEffectClientRpc();
    }
    
    [ClientRpc]
    private void ActivateVisualEffectClientRpc()
    {
        // Si ya hay un efecto visual, eliminarlo primero
        if (visualEffect != null)
        {
            Destroy(visualEffect);
        }
        
        // Añadir el componente de efecto visual
        visualEffect = networkOwner.gameObject.AddComponent<SimpleShieldEffect>();
        
        Debug.Log("Efecto visual de escudo cinético activado en cliente");
    }
    
    private IEnumerator DeactivateShieldAfterDuration()
    {
        yield return new WaitForSeconds(shieldDuration);
        
        // Desactivar si aún está activo
        if (isShieldActive)
        {
            DeactivateShield();
        }
    }
    
    private void DeactivateShield()
    {
        Debug.Log($"[KineticShieldAbility] Desactivando escudo e iniciando cooldown de {cooldown} segundos");
        
        isShieldActive = false;
        
        // Desuscribirse del evento de daño
        if (enableDamageReflection && playerStats != null)
        {
            playerStats.OnTakeDamage -= ReflectDamageToAttacker;
        }
        
        // Desactivar efecto visual en todos los clientes
        DeactivateVisualEffectServerRpc();
        
        // Eliminar reducción de daño
        playerStats.ResetDamageReduction();
        
        // Iniciar el cooldown
        networkOwner.StartCoroutine(StartCooldown());
        
        if (networkOwner.IsOwner)
        {
            Debug.Log("Escudo cinético desactivado");
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void DeactivateVisualEffectServerRpc()
    {
        DeactivateVisualEffectClientRpc();
    }
    
    [ClientRpc]
    private void DeactivateVisualEffectClientRpc()
    {
        // Eliminar el componente de efecto visual
        if (visualEffect != null)
        {
            Destroy(visualEffect);
            visualEffect = null;
        }
        
        Debug.Log("Efecto visual de escudo cinético desactivado en cliente");
    }
    
    public override void UpdateAbility()
    {
        // Mostrar tiempo restante del escudo si está activo
        if (isShieldActive && networkOwner.IsOwner && Time.frameCount % 60 == 0)
        {
            float timeRemaining = shieldEndTime - Time.time;
            Debug.Log($"[KineticShield] Tiempo restante: {timeRemaining:F1}s, Reducción actual: {currentDamageReduction*100:F0}%");
        }
    }
    
    public override void Cleanup()
    {
        // Asegurarse de desactivar el escudo al limpiar
        if (isShieldActive)
        {
            DeactivateShield();
        }
        
        // Eliminar cualquier efecto visual residual
        if (visualEffect != null)
        {
            Destroy(visualEffect);
            visualEffect = null;
        }
        
        // Desuscribirse del evento de daño
        if (enableDamageReflection && playerStats != null)
        {
            playerStats.OnTakeDamage -= ReflectDamageToAttacker;
        }
    }
    
    // Propiedades públicas
    public bool IsShieldActive => isShieldActive;
    public float GetRemainingShieldTime() => isShieldActive ? Mathf.Max(0, shieldEndTime - Time.time) : 0f;
    public float GetCurrentDamageReduction() => currentDamageReduction;
}
}