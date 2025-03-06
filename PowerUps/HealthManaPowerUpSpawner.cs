using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class HealthManaPowerUpSpawner : NetworkBehaviour
{
    [System.Serializable]
    public class PowerUpSpawnPoint
    {
        public Transform transform;
        public HealthManaPowerUp.PowerUpType powerUpType = HealthManaPowerUp.PowerUpType.Health;
        public float respawnTime = 30f;
        public bool spawnOnStart = true;
    }
    
    [Header("Power-Up Prefabs")]
    [SerializeField] private GameObject healthPowerUpPrefab;
    [SerializeField] private GameObject manaPowerUpPrefab;
    [SerializeField] private GameObject healthManaPowerUpPrefab;
    
    [Header("Spawn Configuration")]
    [SerializeField] private PowerUpSpawnPoint[] spawnPoints;
    [SerializeField] private bool randomizeSpawnPoints = false;
    
    [Header("Global Settings")]
    [SerializeField] private float globalRespawnTime = 30f;     // Anulará los tiempos individuales si > 0
    [SerializeField] private int maxConcurrentPowerUps = 5;     // Máximo número de power-ups activos a la vez
    
    // Lista para realizar un seguimiento de los power-ups activos
    private List<NetworkObject> activePowerUps = new List<NetworkObject>();
    
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Solo el servidor spawneará los power-ups
            if (randomizeSpawnPoints)
            {
                RandomizeSpawnPoints();
            }
            
            // Iniciar spawns programados
            SpawnInitialPowerUps();
        }
    }
    
    private void RandomizeSpawnPoints()
    {
        // Mezclar aleatoriamente los tipos de power-up en los spawn points
        System.Random random = new System.Random();
        HealthManaPowerUp.PowerUpType[] types = { 
            HealthManaPowerUp.PowerUpType.Health, 
            HealthManaPowerUp.PowerUpType.Mana, 
            HealthManaPowerUp.PowerUpType.HealthAndMana 
        };
        
        foreach (var spawnPoint in spawnPoints)
        {
            // Asignar un tipo aleatorio
            spawnPoint.powerUpType = types[random.Next(types.Length)];
        }
    }
    
    private void SpawnInitialPowerUps()
    {
        // Iniciar los power-ups marcados para spawnearse al inicio
        foreach (var spawnPoint in spawnPoints)
        {
            if (spawnPoint.spawnOnStart && spawnPoint.transform != null)
            {
                SpawnPowerUpAtPosition(spawnPoint.transform.position, spawnPoint.powerUpType);
            }
        }
    }
    
    private void SpawnPowerUpAtPosition(Vector3 position, HealthManaPowerUp.PowerUpType type)
    {
        // Verificar si ya tenemos el máximo de power-ups
        if (activePowerUps.Count >= maxConcurrentPowerUps)
        {
            // Eliminar el más antiguo para hacer espacio
            if (activePowerUps.Count > 0 && activePowerUps[0] != null)
            {
                NetworkObject oldPowerUp = activePowerUps[0];
                activePowerUps.RemoveAt(0);
                oldPowerUp.Despawn();
            }
        }
        
        // Seleccionar el prefab según el tipo
        GameObject prefabToSpawn = null;
        switch (type)
        {
            case HealthManaPowerUp.PowerUpType.Health:
                prefabToSpawn = healthPowerUpPrefab;
                break;
                
            case HealthManaPowerUp.PowerUpType.Mana:
                prefabToSpawn = manaPowerUpPrefab;
                break;
                
            case HealthManaPowerUp.PowerUpType.HealthAndMana:
                prefabToSpawn = healthManaPowerUpPrefab;
                break;
        }
        
        // Si no hay prefab válido, usar el de vida como fallback
        if (prefabToSpawn == null)
        {
            prefabToSpawn = healthPowerUpPrefab;
            
            // Si ni siquiera tenemos un prefab de vida, no podemos continuar
            if (prefabToSpawn == null)
            {
                Debug.LogError("No hay prefab de power-up asignado para el tipo " + type);
                return;
            }
        }
        
        // Crear el power-up
        GameObject powerUp = Instantiate(prefabToSpawn, position, Quaternion.identity);
        
        // Configurar el tipo si es necesario
        HealthManaPowerUp powerUpComponent = powerUp.GetComponent<HealthManaPowerUp>();
        if (powerUpComponent != null)
        {
            // Aquí podrías configurar propiedades adicionales del power-up si lo necesitas
            
            // Sobrescribir el tiempo de respawn global si está configurado
            if (globalRespawnTime > 0)
            {
                // Para sobrescribir el respawnTime necesitaríamos hacer que sea público
                // o agregar un método setter en HealthManaPowerUp
            }
        }
        
        // Spawnear en la red
        NetworkObject networkObject = powerUp.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            networkObject.Spawn();
            activePowerUps.Add(networkObject);
        }
    }
    
    // Método público para spawn manual desde otros scripts
    public void SpawnPowerUp(int spawnPointIndex, HealthManaPowerUp.PowerUpType type)
    {
        if (!IsServer) return;
        
        // Verificar índice válido
        if (spawnPointIndex >= 0 && spawnPointIndex < spawnPoints.Length && 
            spawnPoints[spawnPointIndex].transform != null)
        {
            SpawnPowerUpAtPosition(spawnPoints[spawnPointIndex].transform.position, type);
        }
    }
    
    // Método para limpiar todos los power-ups activos
    public void ClearAllPowerUps()
    {
        if (!IsServer) return;
        
        foreach (var powerUp in activePowerUps)
        {
            if (powerUp != null)
            {
                powerUp.Despawn(true);
            }
        }
        
        activePowerUps.Clear();
    }
    
    public override void OnDestroy()
    {
        base.OnDestroy();
        
        // Limpieza de recursos
        if (IsServer)
        {
            ClearAllPowerUps();
        }
    }
}