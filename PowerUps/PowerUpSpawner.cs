using UnityEngine;
using Unity.Netcode;

public class PowerUpSpawner : NetworkBehaviour
{
    [SerializeField] private GameObject shieldPowerUpPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private float respawnTime = 60f;
    
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Solo el servidor spawneará los powerups
            SpawnPowerUps();
        }
    }
    
    private void SpawnPowerUps()
    {
        foreach (Transform spawnPoint in spawnPoints)
        {
            SpawnPowerUpAtPosition(spawnPoint.position);
        }
    }
    
    private void SpawnPowerUpAtPosition(Vector3 position)
    {
        GameObject powerUp = Instantiate(shieldPowerUpPrefab, position, Quaternion.identity);
        NetworkObject networkObject = powerUp.GetComponent<NetworkObject>();
        networkObject.Spawn();
        
        // Programar respawn cuando se recoja
        StartCoroutine(RespawnAfterDelay(position, respawnTime));
    }
    
    private System.Collections.IEnumerator RespawnAfterDelay(Vector3 position, float delay)
    {
        yield return new WaitForSeconds(delay);
        SpawnPowerUpAtPosition(position);
    }
}