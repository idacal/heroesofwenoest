using Unity.Netcode;
using UnityEngine;
using PlayerAbilities;
using System.Collections;

public class AbilityPowerUp : NetworkBehaviour
{
    public enum PowerUpType
    {
        Shield,
        UltimateBomb,
        // Añadir más tipos de power-ups aquí
    }
    
    [Header("Configuración")]
    [SerializeField] private PowerUpType powerUpType = PowerUpType.Shield;
    [SerializeField] private float activeDuration = 30f; // Tiempo que dura la habilidad una vez recogida, 0 para indefinido
    [SerializeField] private GameObject pickupEffectPrefab; // Efecto visual al recoger
    
private void OnTriggerEnter(Collider other)
{
    // Try to get the new ability system first
    PlayerAbilityManager abilityManager = other.GetComponent<PlayerAbilityManager>();
    
    if (abilityManager != null && abilityManager.IsOwner)
    {
        // Prioritize using the new system
        CollectPowerUpServerRpc(abilityManager.GetComponent<NetworkObject>().OwnerClientId);
    }
    else
    {
        // Fall back to old system only if necessary
        PlayerAbilityController abilityController = other.GetComponent<PlayerAbilityController>();
        if (abilityController != null && abilityController.IsOwner)
        {
            CollectPowerUpServerRpc(abilityController.GetComponent<NetworkObject>().OwnerClientId);
        }
    }
}
    
    [ServerRpc(RequireOwnership = false)]
    private void CollectPowerUpServerRpc(ulong playerId)
    {
        bool abilityAdded = false;
        
        // Buscar el jugador por ID
        // Primero buscar jugadores con PlayerAbilityManager (nuevo sistema)
        foreach (var abilityManager in FindObjectsOfType<PlayerAbilityManager>())
        {
            if (abilityManager.OwnerClientId == playerId)
            {
                // Activar el powerup para este jugador con el nuevo sistema
                CollectPowerUpClientRpc(playerId, true);
                abilityAdded = true;
                break;
            }
        }
        
        // Si no se encontró jugador con PlayerAbilityManager, buscar PlayerAbilityController
        if (!abilityAdded)
        {
            foreach (var abilityController in FindObjectsOfType<PlayerAbilityController>())
            {
                if (abilityController.OwnerClientId == playerId)
                {
                    // Activar el powerup para este jugador con el sistema antiguo
                    CollectPowerUpClientRpc(playerId, false);
                    abilityAdded = true;
                    break;
                }
            }
        }
        
        if (abilityAdded)
        {
            // Desactivar el objeto de powerup
            gameObject.SetActive(false);
            
            // Destruir después de un breve retraso para asegurar que se procesan todas las acciones
            Destroy(gameObject, 0.5f);
        }
    }
    
    [ClientRpc]
    private void CollectPowerUpClientRpc(ulong playerId, bool useNewSystem)
    {
        // Mostrar efecto visual si estamos en cualquier cliente
        if (pickupEffectPrefab != null)
        {
            Instantiate(pickupEffectPrefab, transform.position, Quaternion.identity);
        }
        
        // Solo procesar la lógica para el jugador que recogió el powerup
        if (useNewSystem)
        {
            foreach (var abilityManager in FindObjectsOfType<PlayerAbilityManager>())
            {
                if (abilityManager.OwnerClientId == playerId)
                {
                    // Añadir la habilidad según el tipo usando el nuevo sistema
                    BaseAbility addedAbility = AddAbilityToPlayer(abilityManager);
                    
                    // También actualizar el sistema de UI a través de PlayerAbility para compatibilidad
                    UpdatePlayerAbilityReference(abilityManager, addedAbility);
                    
                    // Si hay duración limitada, programar su eliminación
                    if (activeDuration > 0)
                    {
                        StartCoroutine(RemoveAbilityAfterDuration(abilityManager));
                    }
                    
                    break;
                }
            }
        }
        else
        {
            foreach (var abilityController in FindObjectsOfType<PlayerAbilityController>())
            {
                if (abilityController.OwnerClientId == playerId)
                {
                    // Añadir la habilidad según el tipo usando el sistema antiguo
                    BaseAbility addedAbility = AddAbilityToPlayer(abilityController);
                    
                    // También actualizar el sistema de UI a través de PlayerAbility
                    UpdatePlayerAbilityReference(abilityController, addedAbility);
                    
                    // Si hay duración limitada, programar su eliminación
                    if (activeDuration > 0)
                    {
                        StartCoroutine(RemoveAbilityAfterDuration(abilityController));
                    }
                    
                    break;
                }
            }
        }
    }
    
    private BaseAbility AddAbilityToPlayer(PlayerAbilityManager abilityManager)
    {
        BaseAbility addedAbility = null;
        
        // Determinar qué slot usar para el new sistema (2 para Shield, 3 para UltimateBomb, etc.)
        int slot = (int)powerUpType + 2;
        
        switch (powerUpType)
        {
            case PowerUpType.Shield:
                addedAbility = abilityManager.AddAbility<ShieldAbility>(slot);
                Debug.Log($"[AbilityPowerUp] Shield ability added to player using PlayerAbilityManager");
                break;
                
            case PowerUpType.UltimateBomb:
                // addedAbility = abilityManager.AddAbility<UltimateBombAbility>(slot);
                Debug.Log($"[AbilityPowerUp] UltimateBomb ability (not implemented) would be added here");
                break;
                
            default:
                Debug.LogWarning("Tipo de power-up no implementado: " + powerUpType);
                break;
        }
        
        return addedAbility;
    }
    
    private BaseAbility AddAbilityToPlayer(PlayerAbilityController controller)
    {
        BaseAbility addedAbility = null;
        
        switch (powerUpType)
        {
            case PowerUpType.Shield:
                addedAbility = controller.AddAbility<ShieldAbility>();
                Debug.Log($"[AbilityPowerUp] Shield ability added to {controller.gameObject.name}");
                break;
                
            case PowerUpType.UltimateBomb:
                // addedAbility = controller.AddAbility<UltimateBombAbility>();
                break;
                
            default:
                Debug.LogWarning("Tipo de power-up no implementado: " + powerUpType);
                break;
        }
        
        return addedAbility;
    }
    
    // Este método ahora maneja tanto PlayerAbilityManager como PlayerAbilityController
    private void UpdatePlayerAbilityReference(Component component, BaseAbility addedAbility)
    {
        if (addedAbility == null) return;
        
        // Buscar PlayerAbility para registro en UI antiguo independientemente del sistema
        PlayerAbility playerAbility = component.GetComponent<PlayerAbility>();
        
        if (playerAbility != null)
        {
            // Registrar la nueva habilidad en el sistema que maneja la UI
            playerAbility.RegisterPowerUpAbility(addedAbility, (int)powerUpType + 2); // +2 porque 0 y 1 son dash y earthquake/strongjump
            Debug.Log($"[AbilityPowerUp] Registered {addedAbility.abilityName} in UI system at slot {(int)powerUpType + 2}");
        }
        else
        {
            Debug.LogWarning("[AbilityPowerUp] No PlayerAbility component found for UI integration");
        }
    }
    
    private IEnumerator RemoveAbilityAfterDuration(PlayerAbilityManager abilityManager)
    {
        yield return new WaitForSeconds(activeDuration);
        
        // Eliminar la habilidad según el tipo
        switch (powerUpType)
        {
            case PowerUpType.Shield:
                abilityManager.RemoveAbility<ShieldAbility>();
                
                // También actualizar el sistema de UI
                RemoveAbilityFromPlayerAbility(abilityManager);
                break;
                
            case PowerUpType.UltimateBomb:
                // abilityManager.RemoveAbility<UltimateBombAbility>();
                // RemoveAbilityFromPlayerAbility(abilityManager);
                break;
        }
    }
    
    private IEnumerator RemoveAbilityAfterDuration(PlayerAbilityController controller)
    {
        yield return new WaitForSeconds(activeDuration);
        
        // Eliminar la habilidad según el tipo
        switch (powerUpType)
        {
            case PowerUpType.Shield:
                controller.RemoveAbility<ShieldAbility>();
                
                // También actualizar el sistema de UI
                RemoveAbilityFromPlayerAbility(controller);
                break;
                
            case PowerUpType.UltimateBomb:
                // controller.RemoveAbility<UltimateBombAbility>();
                // RemoveAbilityFromPlayerAbility(controller);
                break;
        }
    }
    
    // Este método ahora maneja tanto PlayerAbilityManager como PlayerAbilityController
    private void RemoveAbilityFromPlayerAbility(Component component)
    {
        PlayerAbility playerAbility = component.GetComponent<PlayerAbility>();
        if (playerAbility != null)
        {
            playerAbility.UnregisterPowerUpAbility((int)powerUpType + 2);
            Debug.Log($"[AbilityPowerUp] Unregistered ability from UI system at slot {(int)powerUpType + 2}");
        }
    }
}