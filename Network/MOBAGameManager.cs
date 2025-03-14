using Unity.Netcode;
using UnityEngine;
using System.Collections;
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
    [SerializeField] private HeroDefinition[] availableHeroes; // Hero definitions
    
    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;
    
    // Game state network variables
    private NetworkVariable<bool> gameStarted = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Dictionary to track players and their teams
    private Dictionary<ulong, int> playerTeams = new Dictionary<ulong, int>();
    
    // Dictionary to track which hero each player selected
    private Dictionary<ulong, int> playerHeroSelections = new Dictionary<ulong, int>();
    
    // Set of clients that have been spawned
    private HashSet<ulong> spawnedClients = new HashSet<ulong>();
    
    // Variable to control if we're in hero selection phase
    private bool inHeroSelectionMode = false;
    
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Debug.Log("[MOBAGameManager] Server initialized");
            
            // Only register connection events if not in selection mode
            if (!inHeroSelectionMode)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += OnPlayerConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnPlayerDisconnected;
                
                // If we're also a client (host mode), spawn our own player
                if (IsClient)
                {
                    ulong localClientId = NetworkManager.Singleton.LocalClientId;
                    Debug.Log($"[MOBAGameManager] Host mode detected, spawning host player with ID: {localClientId}");
                    SpawnPlayer(localClientId);
                }
            }
            else
            {
                // Only register disconnect event for cleanup
                NetworkManager.Singleton.OnClientDisconnectCallback += OnPlayerDisconnected;
            }
        }
        
        // Subscribe to game state changes
        gameStarted.OnValueChanged += OnGameStartedChanged;
    }
    
    // Handler for game state changes
    private void OnGameStartedChanged(bool oldValue, bool newValue)
    {
        if (newValue)
        {
            Debug.Log("[MOBAGameManager] Game has started!");
            
            // If we're a client and the game just started, make sure we have a player
            if (IsClient && !IsServer)
            {
                Debug.Log("[MOBAGameManager] Client detected game start, requesting spawn");
                RequestSpawnPlayerServerRpc();
            }
        }
    }
    
    private void OnPlayerConnected(ulong clientId)
    {
        Debug.Log($"[MOBAGameManager] Player connected: {clientId}");
        
        // Skip if already registered
        if (playerTeams.ContainsKey(clientId))
        {
            Debug.LogWarning($"[MOBAGameManager] Player {clientId} already registered, ignoring duplicate connection.");
            return;
        }
        
        // If game has started and we have hero selections, spawn with selected hero
        if (gameStarted.Value && playerHeroSelections.Count > 0)
        {
            if (playerHeroSelections.TryGetValue(clientId, out int heroIndex))
            {
                // Use team 2 for clients connecting after game start
                SpawnPlayerWithHero(clientId, heroIndex, 2);
            }
            else
            {
                // No hero selection found, use default
                SpawnPlayer(clientId);
            }
        }
        else if (!inHeroSelectionMode)
        {
            // Normal spawn if not in hero selection mode
            SpawnPlayer(clientId);
        }
    }
    
    private void OnPlayerDisconnected(ulong clientId)
    {
        Debug.Log($"[MOBAGameManager] Player disconnected: {clientId}");
        
        // Clean up player data
        playerTeams.Remove(clientId);
        playerHeroSelections.Remove(clientId);
        spawnedClients.Remove(clientId);
    }
    
    // Method to set hero selection mode
    public void SetHeroSelectionMode(bool inSelection)
    {
        inHeroSelectionMode = inSelection;
        Debug.Log($"[MOBAGameManager] Hero selection mode set to: {inSelection}");
    }
    
    // Method called by HeroSelectionManager to start the game
    public void StartGameWithHeroSelections(Dictionary<ulong, int> heroSelections)
    {
        if (!IsServer) return;
        
        Debug.Log("[MOBAGameManager] Starting game with hero selections");
        
        // Validate hero definitions
        if (availableHeroes == null || availableHeroes.Length == 0)
        {
            Debug.LogError("[MOBAGameManager] No hero definitions available!");
            return;
        }
        
        // Clear existing state
        spawnedClients.Clear();
        playerTeams.Clear();
        
        // Store hero selections
        playerHeroSelections = new Dictionary<ulong, int>(heroSelections);
        
        // Clean up any existing player instances
        CleanupExistingPlayerInstances();
        
        // Separate host (team 1) from clients (team 2)
        ulong hostId = NetworkManager.ServerClientId;
        List<ulong> clientIds = new List<ulong>();
        
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (clientId != hostId)
            {
                clientIds.Add(clientId);
            }
        }
        
        // Spawn host with explicit team 1 assignment
        if (playerHeroSelections.TryGetValue(hostId, out int hostHeroIndex))
        {
            playerTeams[hostId] = 1; // Host is team 1
            SpawnPlayerWithHero(hostId, hostHeroIndex, 1);
        }
        
        // Spawn clients with explicit team 2 assignment
        foreach (var clientId in clientIds)
        {
            if (playerHeroSelections.TryGetValue(clientId, out int clientHeroIndex))
            {
                playerTeams[clientId] = 2; // All clients are team 2
                SpawnPlayerWithHero(clientId, clientHeroIndex, 2);
                
                // Mark this client as spawned
                spawnedClients.Add(clientId);
            }
        }
        
        // Mark game as started
        gameStarted.Value = true;
        
        // Notify all clients
        NotifyGameStartedClientRpc();
    }
    
    // Basic player spawning (used for testing and fallback)
    private void SpawnPlayer(ulong clientId)
    {
        // Assign to team based on even/odd count
        int teamId = (spawnedClients.Count % 2) + 1;
        playerTeams[clientId] = teamId;
        
        // Get spawn position
        Vector3 spawnPosition = GetSpawnPosition(teamId);
        Quaternion spawnRotation = Quaternion.identity;
        
        Debug.Log($"[MOBAGameManager] Spawning player {clientId} as team {teamId} at {spawnPosition}");
        
        // Instantiate player
        GameObject playerInstance = Instantiate(defaultPlayerPrefab, spawnPosition, spawnRotation);
        NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();
        
        if (networkObject != null)
        {
            // Name for identification
            playerInstance.name = $"Player_{clientId}_Team{teamId}";
            
            // Spawn on network
            networkObject.SpawnAsPlayerObject(clientId);
            
            // Record that this client has been spawned
            spawnedClients.Add(clientId);
            
            // Notify client
            InitializePlayerClientRpc(clientId, teamId, spawnPosition, spawnRotation);
        }
        else
        {
            Debug.LogError("[MOBAGameManager] Player prefab missing NetworkObject component!");
            Destroy(playerInstance);
        }
    }
    
    // Main method to spawn a player with their selected hero
    private void SpawnPlayerWithHero(ulong clientId, int heroIndex, int teamId)
    {
        try
        {
            Debug.Log($"[MOBAGameManager] Spawning player {clientId} as team {teamId} with hero index {heroIndex}");
            
            // Get spawn position based on team
            Vector3 spawnPosition = GetSpawnPosition(teamId);
            Quaternion spawnRotation = Quaternion.identity;
            
            // Get hero prefab
            GameObject heroPrefab = GetHeroPrefabForIndex(heroIndex);
            if (heroPrefab == null)
            {
                Debug.LogWarning($"[MOBAGameManager] No hero prefab found for index {heroIndex}, using default");
                heroPrefab = defaultPlayerPrefab;
            }
            
            // Spawn the player with offset to prevent collision
            Vector3 offset = new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 0.5f, UnityEngine.Random.Range(-0.5f, 0.5f));
            GameObject playerInstance = Instantiate(heroPrefab, spawnPosition + offset, spawnRotation);
            
            // Configure network object
            NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Debug.LogError("[MOBAGameManager] Player prefab missing NetworkObject component!");
                Destroy(playerInstance);
                return;
            }
            
            // Configure hero-specific properties before spawning
            Hero heroComponent = playerInstance.GetComponent<Hero>();
            if (heroComponent != null && heroIndex >= 0 && heroIndex < availableHeroes.Length)
            {
                HeroDefinition heroDef = availableHeroes[heroIndex];
                if (heroDef != null)
                {
                    // Set basic properties
                    heroComponent.heroName = heroDef.heroName;
                    heroComponent.heroClass = heroDef.heroClass;
                    
                    // Configure stats
                    PlayerStats stats = playerInstance.GetComponent<PlayerStats>();
                    if (stats != null)
                    {
                        stats.InitializeStatsDirectly(
                            heroDef.baseHealth,
                            heroDef.baseMana,
                            heroDef.healthRegen,
                            heroDef.manaRegen
                        );
                    }
                }
            }
            
            // Set player name
            playerInstance.name = $"Player_{clientId}_Team{teamId}_{GetHeroNameForIndex(heroIndex)}";
            
            // Record team assignment
            playerTeams[clientId] = teamId;
            
            // Spawn as player object
            networkObject.SpawnAsPlayerObject(clientId);
            
            // Force position after spawn
            playerInstance.transform.position = spawnPosition + offset;
            playerInstance.transform.rotation = spawnRotation;
            
            // Initialize player after spawn
            StartCoroutine(InitializePlayerAfterSpawn(playerInstance, clientId, teamId, spawnPosition, spawnRotation));
            
            // Record that this client has been spawned
            spawnedClients.Add(clientId);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[MOBAGameManager] Error spawning player: {e.Message}\n{e.StackTrace}");
        }
    }
    
    // Helper method to get spawn position
    private Vector3 GetSpawnPosition(int teamId)
    {
        Transform[] spawnPoints = (teamId == 1) ? team1SpawnPoints : team2SpawnPoints;
        
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            // Default fallback position based on team
            return new Vector3(teamId * 10, 5, 0);
        }
        
        // Find an unoccupied spawn point
        List<Transform> availablePoints = new List<Transform>();
        
        foreach (var point in spawnPoints)
        {
            if (point == null) continue;
            
            bool isOccupied = false;
            foreach (var player in FindObjectsOfType<PlayerNetwork>())
            {
                if (Vector3.Distance(player.transform.position, point.position) < 2.0f)
                {
                    isOccupied = true;
                    break;
                }
            }
            
            if (!isOccupied)
            {
                availablePoints.Add(point);
            }
        }
        
        // If we have available points, pick a random one
        if (availablePoints.Count > 0)
        {
            int index = UnityEngine.Random.Range(0, availablePoints.Count);
            return availablePoints[index].position;
        }
        
        // If all occupied, use the first one
        if (spawnPoints.Length > 0)
        {
            return spawnPoints[0].position;
        }
        
        // Ultimate fallback
        return new Vector3(teamId * 10, 5, 0);
    }
    
    // Helper method to get hero prefab
    private GameObject GetHeroPrefabForIndex(int heroIndex)
    {
        if (heroIndex >= 0 && heroIndex < availableHeroes.Length && availableHeroes[heroIndex] != null)
        {
            return availableHeroes[heroIndex].modelPrefab;
        }
        return defaultPlayerPrefab;
    }
    
    // Helper to get hero name
    private string GetHeroNameForIndex(int heroIndex)
    {
        if (heroIndex >= 0 && heroIndex < availableHeroes.Length && availableHeroes[heroIndex] != null)
        {
            return availableHeroes[heroIndex].heroName;
        }
        return "DefaultHero";
    }
    
    // Initialize player after spawn
    private IEnumerator InitializePlayerAfterSpawn(GameObject playerInstance, ulong clientId, int teamId, Vector3 position, Quaternion rotation)
    {
        // Wait a moment for network to stabilize
        yield return new WaitForSeconds(0.2f);
        
        if (playerInstance == null) yield break;
        
        // Force visibility
        PlayerRespawnController respawnController = playerInstance.GetComponent<PlayerRespawnController>();
        if (respawnController != null)
        {
            respawnController.ForceVisibility();
        }
        
        // Initialize hero abilities
        Hero heroComponent = playerInstance.GetComponent<Hero>();
        if (heroComponent != null)
        {
            heroComponent.InitializeHeroAbilities();
        }
        
        // Notify the client
        InitializePlayerClientRpc(clientId, teamId, position, rotation);
    }
    
    [ClientRpc]
    private void InitializePlayerClientRpc(ulong clientId, int teamId, Vector3 position, Quaternion rotation)
    {
        // Only process if we are the target client
        if (NetworkManager.Singleton.LocalClientId != clientId) return;
        
        Debug.Log($"[MOBAGameManager] Initializing local player as team {teamId} at {position}");
        
        // Find our local player
        PlayerNetwork localPlayer = null;
        foreach (var player in FindObjectsOfType<PlayerNetwork>())
        {
            if (player.IsLocalPlayer)
            {
                localPlayer = player;
                break;
            }
        }
        
        if (localPlayer != null)
        {
            // Force position
            localPlayer.transform.position = position;
            localPlayer.transform.rotation = rotation;
            
            // Hide hero selection UI if it's still visible
            HeroSelectionUI selectionUI = FindObjectOfType<HeroSelectionUI>();
            if (selectionUI != null)
            {
                selectionUI.Hide();
            }
        }
        else
        {
            Debug.LogWarning("[MOBAGameManager] Could not find local player object!");
        }
    }
    
    // Client-side request for spawn
    [ServerRpc(RequireOwnership = false)]
    public void RequestSpawnPlayerServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        Debug.Log($"[MOBAGameManager] Received spawn request from client {clientId}");
        
        // Check if already spawned
        if (spawnedClients.Contains(clientId))
        {
            Debug.Log($"[MOBAGameManager] Client {clientId} already has a player spawned");
            return;
        }
        
        // Spawn with selected hero if available
        if (playerHeroSelections.TryGetValue(clientId, out int heroIndex))
        {
            Debug.Log($"[MOBAGameManager] Spawning player for client {clientId} with hero index {heroIndex}");
            SpawnPlayerWithHero(clientId, heroIndex, 2); // Clients are team 2
        }
        else
        {
            // Fallback to default spawn
            Debug.Log($"[MOBAGameManager] No hero selection for client {clientId}, using default");
            SpawnPlayer(clientId);
        }
    }
    
    // Notify all clients that the game has started
    [ClientRpc]
    private void NotifyGameStartedClientRpc()
    {
        Debug.Log("[MOBAGameManager] Game started notification received");
        
        // Hide hero selection UI
        HeroSelectionUI selectionUI = FindObjectOfType<HeroSelectionUI>();
        if (selectionUI != null)
        {
            selectionUI.Hide();
        }
        
        // Check if we have a player (client only)
        if (IsClient && !IsServer)
        {
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            bool hasPlayer = false;
            
            // Check if we have a player object
            foreach (var netObj in FindObjectsOfType<NetworkObject>())
            {
                if (netObj.IsPlayerObject && netObj.OwnerClientId == localClientId)
                {
                    hasPlayer = true;
                    break;
                }
            }
            
            // If no player found, request spawn
            if (!hasPlayer)
            {
                Debug.Log("[MOBAGameManager] No player object found, requesting spawn");
                RequestSpawnPlayerServerRpc();
            }
        }
    }
    
    // Clean up any existing player instances
    private void CleanupExistingPlayerInstances()
    {
        var existingPlayers = FindObjectsOfType<PlayerNetwork>();
        int count = 0;
        
        foreach (var player in existingPlayers)
        {
            if (player != null && player.NetworkObject != null && player.NetworkObject.IsSpawned)
            {
                player.NetworkObject.Despawn(true);
                count++;
            }
        }
        
        Debug.Log($"[MOBAGameManager] Cleaned up {count} existing player instances");
    }
    
    // Check if game has started (public accessor)
    public bool HasGameStarted()
    {
        return gameStarted.Value;
    }
    
    public override void OnDestroy()
    {
        // Clean up event subscriptions
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnPlayerConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnPlayerDisconnected;
        }
        
        // Clean up game state subscriptions
        gameStarted.OnValueChanged -= OnGameStartedChanged;
    }
}