using Unity.Netcode;
using UnityEngine;
using System.Collections;

namespace PlayerAbilities
{
public abstract class BaseAbility : MonoBehaviour
{
    [Header("Configuración Básica")]
    public string abilityName = "Ability";
    public KeyCode activationKey = KeyCode.Q;
    public float manaCost = 30f;
    public float cooldown = 2f;
    public Sprite icon; // Icono para la UI
    
    // Variables para seguimiento de cooldown
    [HideInInspector] public float cooldownEndTime = 0f;
    [HideInInspector] public bool isReady = true;
    
    // Referencias
    protected PlayerStats playerStats;
    protected PlayerNetwork playerNetwork;
    protected NetworkBehaviour networkOwner;
    protected CharacterController characterController; // Definido aquí
    protected Rigidbody rb; // Definido aquí
    
    // Inicialización
    public virtual void Initialize(NetworkBehaviour owner)
    {
        networkOwner = owner;
        playerStats = owner.GetComponent<PlayerStats>();
        playerNetwork = owner.GetComponent<PlayerNetwork>();
        characterController = owner.GetComponent<CharacterController>();
        rb = owner.GetComponent<Rigidbody>();
    }
    
    // Método para activar la habilidad
    public virtual bool CanActivate()
    {
        return isReady && playerStats != null && playerStats.CurrentMana >= manaCost;
    }
    
    // Método que se llama cuando se activa la habilidad
    public virtual void Activate()
    {
        // Implementación en clases derivadas
    }
    
    // Método que se llama cuando la habilidad falla (ej: maná insuficiente)
    public virtual void OnFailed()
    {
        if (networkOwner != null && networkOwner.IsOwner)
        {
            Debug.Log($"No tienes suficiente maná para usar {abilityName}");
        }
    }
    
    // Iniciar cooldown
    public virtual IEnumerator StartCooldown()
    {
        isReady = false;
        cooldownEndTime = Time.time + cooldown;
        
        if (networkOwner != null && networkOwner.IsOwner)
        {
            Debug.Log($"Habilidad {abilityName} en cooldown por {cooldown} segundos");
        }
        
        yield return new WaitForSeconds(cooldown);
        
        isReady = true;
        
        if (networkOwner != null && networkOwner.IsOwner)
        {
            Debug.Log($"Habilidad {abilityName} lista para usar");
        }
    }
    
    // Obtener tiempo restante de cooldown - HACERLO VIRTUAL para que pueda sobrescribirse
    public virtual float GetRemainingCooldown()
    {
        if (!isReady)
        {
            return Mathf.Max(0, cooldownEndTime - Time.time);
        }
        return 0f;
    }
    
    // Método para actualizar la habilidad cada frame
    public virtual void UpdateAbility()
    {
        // Implementación en clases derivadas
    }
    
    // Método para limpiar recursos cuando se elimina la habilidad
    public virtual void Cleanup()
    {
        // Implementación en clases derivadas
    }
}
}