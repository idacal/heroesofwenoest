using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class MOBAGameManager : NetworkBehaviour
{
    [Header("Player Setup")]
    [SerializeField] private GameObject playerPrefab;
    
    [Header("Team Spawn Points")]
    [SerializeField] private Transform[] team1SpawnPoints;
    [SerializeField] private Transform[] team2SpawnPoints;
    
    [Header("Team Colors")]
    [SerializeField] private Color team1Color = Color.blue;
    [SerializeField] private Color team2Color = Color.red;
    
    // Dictionary to track players and their teams
    private Dictionary<ulong, int> playerTeams = new Dictionary<ulong, int>();
    private int playerSpawnCount = 0;
    
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Debug.Log("[MANAGER] MOBAGameManager initialized as server");
            
            // Only the server handles player connections
            NetworkManager.Singleton.OnClientConnectedCallback += OnPlayerConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnPlayerDisconnected;
            
            // If we're also a client (host mode), spawn our own player
            if (IsClient)
            {
                ulong localClientId = NetworkManager.Singleton.LocalClientId;
                Debug.Log($"[MANAGER] Host mode detected, spawning host player with ID: {localClientId}");
                SpawnPlayer(localClientId);
            }
        }
    }
    
    private void OnPlayerConnected(ulong clientId)
    {
        // Don't spawn again if already spawned (prevents duplicates)
        if (playerTeams.ContainsKey(clientId))
        {
            Debug.LogWarning($"[MANAGER] Player {clientId} already registered, ignoring duplicate connection.");
            return;
        }
        
        Debug.Log($"[MANAGER] New player connected with ID: {clientId}");
        
        // Spawn player for this client
        SpawnPlayer(clientId);
    }
    
    private void SpawnPlayer(ulong clientId)
    {
        // Asignar a team 1 o 2 basado en even/odd count
        int teamId = (playerSpawnCount % 2) + 1;
        playerTeams[clientId] = teamId;
        
        // Seleccionar punto de spawn
        Transform[] spawnPoints = (teamId == 1) ? team1SpawnPoints : team2SpawnPoints;
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError($"[MANAGER] Error: No spawn points configured for team {teamId}");
            return;
        }
        
        // Usar modulo para seleccionar punto de spawn (ciclo a través de puntos disponibles)
        int spawnIndex = (playerSpawnCount / 2) % spawnPoints.Length;
        Transform spawnPoint = spawnPoints[spawnIndex];
        
        // Guardar información de spawn
        Vector3 spawnPosition = spawnPoint.position;
        Quaternion spawnRotation = spawnPoint.rotation;
        
        Debug.Log($"[MANAGER] Spawning player {clientId} as team {teamId} at position {spawnPosition}");
        
        // IMPORTANTE: Instanciar directamente en la posición del punto de spawn
        GameObject playerInstance = Instantiate(playerPrefab, spawnPosition, spawnRotation);
        NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();
        
        if (networkObject != null)
        {
            // Incrementar contador después de spawn exitoso
            playerSpawnCount++;
            
            // Añadir ID único de jugador para mejor identificación en logs
            playerInstance.name = $"Player_{clientId}_Team{teamId}";
            
            // IMPORTANTE: Verificar la posición antes del spawn
            Debug.Log($"[MANAGER] Pre-Spawn Position: {playerInstance.transform.position}");
            
            // IMPORTANTE: Establecer la posición de nuevo para asegurar
            playerInstance.transform.position = spawnPosition;
            playerInstance.transform.rotation = spawnRotation;
            
            // Spawnear el objeto en la red
            networkObject.SpawnAsPlayerObject(clientId);
            
            // IMPORTANTE: Forzar la posición nuevamente después del spawn
            playerInstance.transform.position = spawnPosition;
            playerInstance.transform.rotation = spawnRotation;
            
            Debug.Log($"[MANAGER] Post-Spawn Position: {playerInstance.transform.position}");
            
            // IMPORTANTE: Forzar sincronización a través de la red
            PlayerNetwork playerNetwork = playerInstance.GetComponent<PlayerNetwork>();
            if (playerNetwork != null)
            {
                playerNetwork.SyncInitialTransformServerRpc(spawnPosition, spawnRotation);
            }
            
            // Notificar al cliente sobre su equipo y posición
            InitializePlayerClientRpc(clientId, teamId, spawnPosition, spawnRotation);
        }
        else
        {
            Debug.LogError("[MANAGER] Player prefab does not have NetworkObject component");
            Destroy(playerInstance);
        }
    }
    
    [ClientRpc]
    private void InitializePlayerClientRpc(ulong clientId, int teamId, Vector3 spawnPosition, Quaternion spawnRotation)
    {
        Debug.Log($"[MANAGER] Cliente {NetworkManager.Singleton.LocalClientId} recibió inicialización para cliente {clientId}");
        
        // Solo actuar si somos el cliente destinatario
        if (NetworkManager.Singleton.LocalClientId != clientId)
        {
            return;
        }
        
        Debug.Log($"[MANAGER] Inicializando jugador: Team {teamId}, Position {spawnPosition}");
        
        // Buscar nuestro objeto de jugador local
        PlayerNetwork[] players = FindObjectsOfType<PlayerNetwork>();
        foreach (PlayerNetwork player in players)
        {
            if (player.IsLocalPlayer)
            {
                // Forzar posición correcta
                player.transform.position = spawnPosition;
                player.transform.rotation = spawnRotation;
                
                // Solicitar al servidor que sincronice nuestra posición
                player.SyncInitialTransformServerRpc(spawnPosition, spawnRotation);
                
                Debug.Log($"[MANAGER] Jugador local inicializado en {spawnPosition}");
                
                // Cambiar color basado en equipo
                Renderer renderer = player.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // Aplicar color de equipo apropiado
                    Color teamColor = (teamId == 1) ? team1Color : team2Color;
                    renderer.material.color = teamColor;
                    
                    Debug.Log($"[MANAGER] Color de equipo {teamId} aplicado al jugador");
                }
                
                break;
            }
        }
    }
    
    private void OnPlayerDisconnected(ulong clientId)
    {
        Debug.Log($"[MANAGER] Player disconnected: {clientId}");
        
        // Clean up player data
        if (playerTeams.ContainsKey(clientId))
        {
            playerTeams.Remove(clientId);
        }
    }
    
    public override void OnDestroy()
    {
        // Clean up event subscriptions
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnPlayerConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnPlayerDisconnected;
        }
    }
}