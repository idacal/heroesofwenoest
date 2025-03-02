using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class MOBAGameManager : NetworkBehaviour
{
    [SerializeField] private GameObject playerPrefab;
    
    // Lista de puntos de aparición para el equipo 1
    [SerializeField] private Transform[] team1SpawnPoints;
    
    // Lista de puntos de aparición para el equipo 2
    [SerializeField] private Transform[] team2SpawnPoints;
    
    // Diccionario para rastrear los jugadores y sus equipos
    private Dictionary<ulong, int> playerTeams = new Dictionary<ulong, int>();
    
    // Contador para debugging
    private int playerSpawnCount = 0;
    
    // Variable para rastrear si ya hemos spawneado al jugador local
    private bool localPlayerSpawned = false;
    
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Debug.Log("MOBAGameManager iniciado como servidor");
            
            // Solo el servidor maneja la conexión de jugadores
            NetworkManager.Singleton.OnClientConnectedCallback += OnPlayerConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnPlayerDisconnected;
        }
    }
    
    private void OnPlayerConnected(ulong clientId)
    {
        // Verificación para evitar spawns duplicados
        if (playerTeams.ContainsKey(clientId))
        {
            Debug.LogWarning($"[MOBA] El jugador {clientId} ya está registrado, ignorando conexión duplicada.");
            return;
        }
        
        Debug.Log($"[MOBA] Nuevo jugador conectado con ID: {clientId}, conteo actual: {playerSpawnCount}");
        
        // Asignar al equipo 1 o 2 dependiendo de si es número par o impar
        int teamId = (playerSpawnCount % 2) + 1;
        playerTeams[clientId] = teamId;
        
        // Seleccionar punto de spawn
        Transform[] spawnPoints = (teamId == 1) ? team1SpawnPoints : team2SpawnPoints;
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError($"[MOBA] Error: No hay puntos de spawn configurados para el equipo {teamId}");
            return;
        }
        
        // Usar módulo para seleccionar spawn point (se repiten cíclicamente)
        int spawnIndex = (playerSpawnCount / 2) % spawnPoints.Length;
        Transform spawnPoint = spawnPoints[spawnIndex];
        
        Debug.Log($"[MOBA] Spawneando jugador {clientId} como equipo {teamId} en posición {spawnPoint.position}");
        
        // Instanciar el jugador
        GameObject playerInstance = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
        NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();
        
        if (networkObject != null)
        {
            // Incrementar contador después de spawn exitoso
            playerSpawnCount++;
            
            // Spawn del objeto en la red
            networkObject.SpawnAsPlayerObject(clientId);
            
            // Notificar al cliente sobre su equipo
            NotifyPlayerTeamClientRpc(clientId, teamId);
        }
        else
        {
            Debug.LogError("[MOBA] El prefab del jugador no tiene componente NetworkObject");
            Destroy(playerInstance);
        }
    }
    
    private void OnPlayerDisconnected(ulong clientId)
    {
        Debug.Log($"[MOBA] Jugador desconectado: {clientId}");
        
        // Limpiar datos
        if (playerTeams.ContainsKey(clientId))
        {
            playerTeams.Remove(clientId);
        }
    }
    
    [ClientRpc]
    private void NotifyPlayerTeamClientRpc(ulong clientId, int teamId)
    {
        // Solo actuar si somos el cliente al que se le envió este mensaje
        if (NetworkManager.Singleton.LocalClientId == clientId)
        {
            Debug.Log($"[MOBA] Eres parte del equipo {teamId}");
            
            // Encontrar el objeto de jugador local
            GameObject localPlayerObject = null;
            PlayerNetwork[] players = FindObjectsOfType<PlayerNetwork>();
            
            foreach (PlayerNetwork player in players)
            {
                if (player.IsLocalPlayer)
                {
                    localPlayerObject = player.gameObject;
                    break;
                }
            }
            
            if (localPlayerObject != null)
            {
                // Cambiar color según equipo
                Renderer renderer = localPlayerObject.GetComponent<Renderer>();
                if (renderer != null)
                {
                    if (teamId == 1)
                    {
                        renderer.material.color = Color.blue;
                    }
                    else
                    {
                        renderer.material.color = Color.red;
                    }
                    
                    Debug.Log($"[MOBA] Color del jugador actualizado para equipo {teamId}");
                }
            }
            else
            {
                Debug.LogWarning("[MOBA] No se pudo encontrar el objeto de jugador local");
            }
        }
    }
}