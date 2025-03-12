using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class MOBAGameManager : NetworkBehaviour
{
    [Header("Player Setup")]
    [SerializeField] private GameObject defaultPlayerPrefab; // Fallback if hero prefab is missing

    [Header("Team Spawn Points")]
    [SerializeField] private Transform[] team1SpawnPoints;
    [SerializeField] private Transform[] team2SpawnPoints;
    
    [Header("Team Colors")]
    [SerializeField] private Color team1Color = Color.blue;
    [SerializeField] private Color team2Color = Color.red;
    
    [Header("Hero System")]
    [SerializeField] private HeroData[] availableHeroes;
    
    // Nueva variable para controlar si estamos en fase de selección de héroes
    private bool inHeroSelectionMode = false;

    // Dictionary to track players and their teams
    private Dictionary<ulong, int> playerTeams = new Dictionary<ulong, int>();
    // Dictionary to track which hero each player selected
    private Dictionary<ulong, int> playerHeroSelections = new Dictionary<ulong, int>();
    private int playerSpawnCount = 0;
    
    // Método para establecer el modo de selección de héroes
    public void SetHeroSelectionMode(bool inSelection)
    {
        inHeroSelectionMode = inSelection;
        Debug.Log($"MOBAGameManager: Modo selección de héroes = {inSelection}");
    }
    
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Debug.Log("[MANAGER] MOBAGameManager initialized as server");
            
            // Solo inicializar jugadores si NO estamos en modo selección
            if (!inHeroSelectionMode)
            {
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
            else
            {
                Debug.Log("MOBAGameManager en modo selección de héroes - no spawneando jugadores automáticamente");
                
                // Solo registramos el evento de desconexión para limpieza
                NetworkManager.Singleton.OnClientDisconnectCallback += OnPlayerDisconnected;
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
        
        // If hero selection phase is complete, spawn with selected hero
        if (playerHeroSelections.Count > 0)
        {
            SpawnPlayerWithSelectedHero(clientId);
        }
        else
        {
            // Spawn player for this client
            SpawnPlayer(clientId);
        }
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
        GameObject playerInstance = Instantiate(defaultPlayerPrefab, spawnPosition, spawnRotation);
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
    
    // This method will be called by the HeroSelectionManager when all players have selected heroes
    public void StartGameWithHeroSelections(Dictionary<ulong, int> heroSelections)
    {
        if (!IsServer) return;
        
        Debug.Log("[MANAGER] Starting game with hero selections");
        
        // Actualizar modo de selección
        inHeroSelectionMode = false;
        
        // Store the hero selections
        playerHeroSelections = heroSelections;
        
        // Register for player connection events now that selection is complete
        NetworkManager.Singleton.OnClientConnectedCallback += OnPlayerConnected;
        
        // Spawn players with their selected heroes
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            SpawnPlayerWithSelectedHero(clientId);
        }
    }
    
    // Modified player spawning method that uses the selected hero
    private void SpawnPlayerWithSelectedHero(ulong clientId)
    {
        // Determine which team the player should join
        int teamId = (playerSpawnCount % 2) + 1;
        playerTeams[clientId] = teamId;
        
        // Select spawn point
        Transform[] spawnPoints = (teamId == 1) ? team1SpawnPoints : team2SpawnPoints;
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError($"[MANAGER] Error: No spawn points configured for team {teamId}");
            return;
        }
        
        // Use modulo to select spawn point
        int spawnIndex = (playerSpawnCount / 2) % spawnPoints.Length;
        Transform spawnPoint = spawnPoints[spawnIndex];
        
        // Get spawn position and rotation
        Vector3 spawnPosition = spawnPoint.position;
        Quaternion spawnRotation = spawnPoint.rotation;
        
        // Get the hero prefab based on selection
        GameObject heroToSpawn = GetHeroPrefabForPlayer(clientId);
        
        Debug.Log($"[MANAGER] Spawning player {clientId} as team {teamId} with hero {GetHeroNameForPlayer(clientId)} at position {spawnPosition}");
        
        // Instantiate the hero prefab
        GameObject playerInstance = Instantiate(heroToSpawn, spawnPosition, spawnRotation);
        NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();
        
        if (networkObject != null)
        {
            // Increment spawn counter
            playerSpawnCount++;
            
            // Add unique ID for better identification
            playerInstance.name = $"Player_{clientId}_Team{teamId}_{GetHeroNameForPlayer(clientId)}";
            
            // Verify position
            Debug.Log($"[MANAGER] Pre-Spawn Position: {playerInstance.transform.position}");
            
            // Set position again to ensure accuracy
            playerInstance.transform.position = spawnPosition;
            playerInstance.transform.rotation = spawnRotation;
            
            // Spawn in network
            networkObject.SpawnAsPlayerObject(clientId);
            
            // Force position after spawn
            playerInstance.transform.position = spawnPosition;
            playerInstance.transform.rotation = spawnRotation;
            
            Debug.Log($"[MANAGER] Post-Spawn Position: {playerInstance.transform.position}");
            
            // Force sync through network
            PlayerNetwork playerNetwork = playerInstance.GetComponent<PlayerNetwork>();
            if (playerNetwork != null)
            {
                playerNetwork.SyncInitialTransformServerRpc(spawnPosition, spawnRotation);
            }
            
            // Notify client about team and position
            InitializePlayerClientRpc(clientId, teamId, spawnPosition, spawnRotation);
        }
        else
        {
            Debug.LogError("[MANAGER] Player prefab does not have NetworkObject component");
            Destroy(playerInstance);
        }
    }
    
    // Method to get the appropriate hero prefab for a player
    private GameObject GetHeroPrefabForPlayer(ulong clientId)
    {
        // Check if this player has a hero selection
        if (playerHeroSelections.TryGetValue(clientId, out int heroIndex))
        {
            // Check if the hero index is valid
            if (heroIndex >= 0 && heroIndex < availableHeroes.Length)
            {
                // Get the hero data
                HeroData heroData = availableHeroes[heroIndex];
                
                // Return hero prefab if it exists
                if (heroData != null && heroData.heroPrefab != null)
                {
                    return heroData.heroPrefab;
                }
            }
        }
        
        // Fallback to default player prefab
        Debug.LogWarning($"[MANAGER] No valid hero selection for client {clientId}, using default player prefab");
        return defaultPlayerPrefab;
    }
    
    // Helper method to get hero name for logging
    private string GetHeroNameForPlayer(ulong clientId)
    {
        if (playerHeroSelections.TryGetValue(clientId, out int heroIndex))
        {
            if (heroIndex >= 0 && heroIndex < availableHeroes.Length)
            {
                return availableHeroes[heroIndex].heroName;
            }
        }
        return "DefaultHero";
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
        
        // Clean up hero selection data if it exists
        if (playerHeroSelections.ContainsKey(clientId))
        {
            playerHeroSelections.Remove(clientId);
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