using Unity.Netcode;
using UnityEngine;

public class HealthManaPowerUp : NetworkBehaviour
{
    public enum PowerUpType
    {
        Health,
        Mana,
        HealthAndMana
    }
    
    [Header("Configuración")]
    [SerializeField] private PowerUpType powerUpType = PowerUpType.Health;
    [SerializeField] private float healthAmount = 250f;      // Cantidad de vida que restaura
    [SerializeField] private float manaAmount = 150f;        // Cantidad de maná que restaura
    [SerializeField] private float respawnTime = 30f;        // Tiempo que tarda en reaparecer
    [SerializeField] private GameObject pickupEffectPrefab;  // Efecto visual al recoger
    
    [Header("Apariencia")]
    [SerializeField] private Material healthMaterial;        // Material para power-up de vida
    [SerializeField] private Material manaMaterial;          // Material para power-up de maná
    [SerializeField] private Material healthManaMaterial;    // Material para power-up combinado
    
    private MeshRenderer meshRenderer;
    private bool isAvailable = true;
    private Vector3 originalPosition;
    
    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        originalPosition = transform.position;
        
        // Configurar material según el tipo de power-up
        UpdateAppearance();
    }
    
    public override void OnNetworkSpawn()
    {
        // Asegurarse de que esté disponible al aparecer
        if (IsServer)
        {
            SetAvailability(true);
        }
    }
    
    private void UpdateAppearance()
    {
        if (meshRenderer == null) return;
        
        switch (powerUpType)
        {
            case PowerUpType.Health:
                if (healthMaterial != null)
                    meshRenderer.material = healthMaterial;
                break;
                
            case PowerUpType.Mana:
                if (manaMaterial != null)
                    meshRenderer.material = manaMaterial;
                break;
                
            case PowerUpType.HealthAndMana:
                if (healthManaMaterial != null)
                    meshRenderer.material = healthManaMaterial;
                break;
        }
    }
    
    private void OnTriggerEnter(Collider other)
    {
        // Verificar si está disponible para ser recogido
        if (!isAvailable) return;
        
        // Verificar si es un jugador
        PlayerStats playerStats = other.GetComponent<PlayerStats>();
        
        if (playerStats != null && playerStats.IsOwner)
        {
            // Solicitar al servidor que procese la recogida
            CollectPowerUpServerRpc(playerStats.GetComponent<NetworkObject>().OwnerClientId);
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void CollectPowerUpServerRpc(ulong playerId)
    {
        // Verificar si todavía está disponible (para evitar duplicados)
        if (!isAvailable) return;
        
        // Buscar el jugador por ID
        foreach (var player in FindObjectsOfType<PlayerStats>())
        {
            if (player.OwnerClientId == playerId)
            {
                // Aplicar los efectos del power-up según su tipo
                switch (powerUpType)
                {
                    case PowerUpType.Health:
                        player.Heal(healthAmount);
                        break;
                        
                    case PowerUpType.Mana:
                        player.RestoreMana(manaAmount);
                        break;
                        
                    case PowerUpType.HealthAndMana:
                        player.Heal(healthAmount);
                        player.RestoreMana(manaAmount);
                        break;
                }
                
                // Enviar efecto visual a todos los clientes
                CollectPowerUpClientRpc(transform.position);
                
                // Desactivar el power-up
                SetAvailability(false);
                
                // Programar respawn
                StartCoroutine(RespawnAfterDelay());
                
                return;
            }
        }
    }
    
    [ClientRpc]
    private void CollectPowerUpClientRpc(Vector3 position)
    {
        // Mostrar efecto visual si estamos en cualquier cliente
        if (pickupEffectPrefab != null)
        {
            Instantiate(pickupEffectPrefab, position, Quaternion.identity);
        }
        
        // Reproducir sonido (si tienes un sistema de audio)
        PlayPickupSound();
    }
    
    private void PlayPickupSound()
    {
        // Aquí puedes implementar la reproducción de sonido
        // Por ejemplo, si tienes un AudioSource:
        // AudioSource audioSource = GetComponent<AudioSource>();
        // if (audioSource != null) audioSource.Play();
    }
    
    private void SetAvailability(bool available)
    {
        isAvailable = available;
        
        // Actualizar visibilidad
        if (meshRenderer != null)
        {
            meshRenderer.enabled = available;
        }
        
        // Activar/desactivar collider
        Collider collider = GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = available;
        }
        
        // Activar/desactivar efectos visuales como rotación
        RotatePowerUp rotator = GetComponent<RotatePowerUp>();
        if (rotator != null)
        {
            rotator.enabled = available;
        }
    }
    
    private System.Collections.IEnumerator RespawnAfterDelay()
    {
        // Esperar el tiempo de respawn
        yield return new WaitForSeconds(respawnTime);
        
        // Asegurarse de que estamos en la posición original
        transform.position = originalPosition;
        
        // Hacer disponible nuevamente
        SetAvailability(true);
    }
}