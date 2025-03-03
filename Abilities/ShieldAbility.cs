using Unity.Netcode;
using UnityEngine;
using System.Collections;

namespace PlayerAbilities

public class ShieldAbility : BaseAbility
{
    [Header("Configuración de Escudo")]
    [SerializeField] private float shieldDuration = 5f;
    [SerializeField] private float damageReduction = 0.5f; // 50% de reducción de daño
    [SerializeField] private GameObject shieldEffectPrefab;
    
    // Estado del escudo
    private bool isShieldActive = false;
    private GameObject activeShieldEffect = null;
    private float shieldEndTime = 0f;
    
    public override void Initialize(NetworkBehaviour owner)
    {
        base.Initialize(owner);
        abilityName = "Escudo Protector";
        activationKey = KeyCode.E;
        manaCost = 70f;
        cooldown = 8f;
    }
    
    public override bool CanActivate()
    {
        // No permitir activar si ya está activo
        if (isShieldActive)
        {
            if (networkOwner.IsOwner)
            {
                Debug.Log("El escudo ya está activo");
            }
            return false;
        }
        
        return isReady && playerStats.CurrentMana >= manaCost;
    }
    
    public override void Activate()
    {
        // Activar el escudo
        isShieldActive = true;
        shieldEndTime = Time.time + shieldDuration;
        
        // Crear efecto visual
        if (shieldEffectPrefab != null)
        {
            activeShieldEffect = Instantiate(shieldEffectPrefab, networkOwner.transform.position, Quaternion.identity);
            activeShieldEffect.transform.parent = networkOwner.transform;
        }
        
        // Registrar el escudo con el sistema de daño del jugador
        PlayerStats playerStats = networkOwner.GetComponent<PlayerStats>();
        if (playerStats != null)
        {
            playerStats.SetDamageReduction(damageReduction);
        }
        
        if (networkOwner.IsOwner)
        {
            Debug.Log($"¡Escudo activado! Duración: {shieldDuration} segundos");
        }
        
        // Iniciar corrutina para desactivar automáticamente
        networkOwner.StartCoroutine(DeactivateShieldAfterDuration());
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
        isShieldActive = false;
        
        // Eliminar efecto visual
        if (activeShieldEffect != null)
        {
            Destroy(activeShieldEffect);
            activeShieldEffect = null;
        }
        
        // Restaurar sistema de daño normal
        PlayerStats playerStats = networkOwner.GetComponent<PlayerStats>();
        if (playerStats != null)
        {
            playerStats.ResetDamageReduction();
        }
        
        if (networkOwner.IsOwner)
        {
            Debug.Log("Escudo desactivado");
        }
    }
    
    public override void UpdateAbility()
    {
        // Mostrar tiempo restante del escudo si está activo
        if (isShieldActive && networkOwner.IsOwner)
        {
            float timeRemaining = shieldEndTime - Time.time;
            if (timeRemaining <= 1.0f && timeRemaining > 0.9f)
            {
                Debug.Log("¡El escudo se desactivará en 1 segundo!");
            }
        }
    }
    
    public override void Cleanup()
    {
        // Asegurarse de desactivar el escudo al limpiar
        if (isShieldActive)
        {
            DeactivateShield();
        }
        
        // Eliminar cualquier referencia o efecto pendiente
        if (activeShieldEffect != null)
        {
            Destroy(activeShieldEffect);
        }
    }
    
    // Propiedades públicas
    public bool IsShieldActive => isShieldActive;
    public float GetRemainingShieldTime() => isShieldActive ? Mathf.Max(0, shieldEndTime - Time.time) : 0f;
}
}