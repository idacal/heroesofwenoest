using Unity.Netcode;
using UnityEngine;
using PlayerAbilities;

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
        // Verificar si es un jugador y tiene el controlador de habilidades
        PlayerAbilityController abilityController = other.GetComponent<PlayerAbilityController>();
        
        if (abilityController != null && abilityController.IsOwner)
        {
            // Solicitar al servidor que procese la recogida
            CollectPowerUpServerRpc(abilityController.GetComponent<NetworkObject>().OwnerClientId);
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void CollectPowerUpServerRpc(ulong playerId)
    {
        // Buscar el jugador por ID
        foreach (var player in FindObjectsOfType<PlayerAbilityController>())
        {
            if (player.OwnerClientId == playerId)
            {
                // Activar el powerup para este jugador
                CollectPowerUpClientRpc(playerId);
                
                // Desactivar el objeto de powerup
                gameObject.SetActive(false);
                
                // Destruir después de un breve retraso para asegurar que se procesan todas las acciones
                Destroy(gameObject, 0.5f);
                
                return;
            }
        }
    }
    
    [ClientRpc]
    private void CollectPowerUpClientRpc(ulong playerId)
    {
        // Mostrar efecto visual si estamos en cualquier cliente
        if (pickupEffectPrefab != null)
        {
            Instantiate(pickupEffectPrefab, transform.position, Quaternion.identity);
        }
        
        // Solo procesar la lógica para el jugador que recogió el powerup
        foreach (var player in FindObjectsOfType<PlayerAbilityController>())
        {
            if (player.OwnerClientId == playerId)
            {
                // Añadir la habilidad según el tipo
                BaseAbility addedAbility = AddAbilityToPlayer(player);
                
                // También actualizar el sistema de UI a través de PlayerAbility
                UpdatePlayerAbilityReference(player, addedAbility);
                
                // Si hay duración limitada, programar su eliminación
                if (activeDuration > 0)
                {
                    StartCoroutine(RemoveAbilityAfterDuration(player));
                }
                
                break;
            }
        }
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
                // Ejemplo de otra habilidad que podrías implementar
                // addedAbility = controller.AddAbility<UltimateBombAbility>();
                break;
                
            default:
                Debug.LogWarning("Tipo de power-up no implementado: " + powerUpType);
                break;
        }
        
        return addedAbility;
    }
    
    // Este método es nuevo - conecta la habilidad adquirida con el sistema de UI
    private void UpdatePlayerAbilityReference(PlayerAbilityController controller, BaseAbility addedAbility)
    {
        if (addedAbility == null) return;
        
        // Buscar el componente PlayerAbility que gestiona la UI
        PlayerAbility playerAbility = controller.GetComponent<PlayerAbility>();
        if (playerAbility != null)
        {
            // Registrar la nueva habilidad en el sistema que maneja la UI
            playerAbility.RegisterPowerUpAbility(addedAbility, (int)powerUpType + 2); // +2 porque 0 y 1 son dash y earthquake
            Debug.Log($"[AbilityPowerUp] Registered {addedAbility.abilityName} in UI system at slot {(int)powerUpType + 2}");
        }
        else
        {
            Debug.LogWarning("[AbilityPowerUp] No PlayerAbility component found for UI integration");
        }
    }
    
    private System.Collections.IEnumerator RemoveAbilityAfterDuration(PlayerAbilityController controller)
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
    
    // Este método es nuevo - desregistra la habilidad del sistema de UI
    private void RemoveAbilityFromPlayerAbility(PlayerAbilityController controller)
    {
        PlayerAbility playerAbility = controller.GetComponent<PlayerAbility>();
        if (playerAbility != null)
        {
            playerAbility.UnregisterPowerUpAbility((int)powerUpType + 2);
            Debug.Log($"[AbilityPowerUp] Unregistered ability from UI system at slot {(int)powerUpType + 2}");
        }
    }
}