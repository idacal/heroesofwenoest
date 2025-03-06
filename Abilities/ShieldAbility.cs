using Unity.Netcode;
using UnityEngine;
using System.Collections;

namespace PlayerAbilities
{
public class ShieldAbility : BaseAbility
{
    [Header("Configuración de Escudo")]
    [SerializeField] private float shieldDuration = 5f;
    [SerializeField] private float damageReduction = 0.5f; // 50% de reducción de daño
    
    // Estado del escudo
    private bool isShieldActive = false;
    private float shieldEndTime = 0f;
    
    // Componente de efecto visual
    private SimpleShieldEffect visualEffect;
    
    // Nuevo: Flag para controlar si necesitamos reactivar el cooldown al finalizar
    private bool needsDelayedCooldown = true;
    
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
        // Importante: NO bloquear el cooldown que aplicará PlayerAbilityController
        // En lugar de eso, reset it inmediatamente y posponerlo
        
        // Guardar el estado para saber si debemos reiniciar el cooldown más tarde
        needsDelayedCooldown = true;
        
        Debug.Log($"[ShieldAbility] Activando escudo (se reiniciará el cooldown después)");
        
        // Activar el escudo
        isShieldActive = true;
        shieldEndTime = Time.time + shieldDuration;
        
        // Activar efecto visual en todos los clientes
        ActivateVisualEffectServerRpc();
        
        // Aplicar reducción de daño usando PlayerStats
        playerStats.SetDamageReduction(damageReduction);
        
        if (networkOwner.IsOwner)
        {
            Debug.Log($"¡Escudo activado! Duración: {shieldDuration} segundos");
        }
        
        // Iniciar corrutina para desactivar automáticamente
        StartCoroutine(DeactivateShieldAfterDuration());
        
        // Iniciar corrutina que resetea el cooldown después de un pequeño delay
        // para que no se bloquee el uso de la habilidad durante su efecto
        StartCoroutine(ResetCooldownAfterDelay(0.5f));
    }
    
    // Nueva corrutina para resetear el cooldown después de un pequeño delay
    private IEnumerator ResetCooldownAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // Solo hacemos esto si el cooldown se activó (lo que debería ocurrir en PlayerAbilityController)
        if (!isReady && needsDelayedCooldown)
        {
            Debug.Log($"[ShieldAbility] Reseteando cooldown para permitir uso durante el efecto");
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
        
        Debug.Log("Efecto visual de escudo activado en cliente");
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
        Debug.Log($"[ShieldAbility] Desactivando escudo e iniciando cooldown de {cooldown} segundos");
        
        isShieldActive = false;
        
        // Desactivar efecto visual en todos los clientes
        DeactivateVisualEffectServerRpc();
        
        // Eliminar reducción de daño
        playerStats.ResetDamageReduction();
        
        // IMPORTANTE: Iniciar el cooldown SOLO si el flag está activo
        // (lo que significa que no hubo un cooldown inicial o ya fue reseteado)
        networkOwner.StartCoroutine(StartCooldown());
        
        if (networkOwner.IsOwner)
        {
            Debug.Log("Escudo desactivado");
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
        
        Debug.Log("Efecto visual de escudo desactivado en cliente");
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
        
        // Eliminar cualquier efecto visual residual
        if (visualEffect != null)
        {
            Destroy(visualEffect);
            visualEffect = null;
        }
    }
    
    // Propiedades públicas
    public bool IsShieldActive => isShieldActive;
    public float GetRemainingShieldTime() => isShieldActive ? Mathf.Max(0, shieldEndTime - Time.time) : 0f;
}
}