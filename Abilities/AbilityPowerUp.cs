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
                AddAbilityToPlayer(player);
                
                // Si hay duración limitada, programar su eliminación
                if (activeDuration > 0)
                {
                    StartCoroutine(RemoveAbilityAfterDuration(player));
                }
                
                break;
            }
        }
    }
    
    private void AddAbilityToPlayer(PlayerAbilityController controller)
    {
        switch (powerUpType)
        {
            case PowerUpType.Shield:
                controller.AddAbility<ShieldAbility>();
                break;
                
            case PowerUpType.UltimateBomb:
                // Ejemplo de otra habilidad que podrías implementar
                // controller.AddAbility<UltimateBombAbility>();
                break;
                
            default:
                Debug.LogWarning("Tipo de power-up no implementado: " + powerUpType);
                break;
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
                break;
                
            case PowerUpType.UltimateBomb:
                // controller.RemoveAbility<UltimateBombAbility>();
                break;
        }
    }
}