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
    [SerializeField] private HeroDefinition[] availableHeroes; // Cambiado de HeroData[] a HeroDefinition[]
    
    // Variable to control if we're in hero selection phase
    private bool inHeroSelectionMode = false;

    // Dictionary to track players and their teams
    private Dictionary<ulong, int> playerTeams = new Dictionary<ulong, int>();
    // Dictionary to track which hero each player selected
    private Dictionary<ulong, int> playerHeroSelections = new Dictionary<ulong, int>();
    private int playerSpawnCount = 0;
    
    // Method to set hero selection mode
    public void SetHeroSelectionMode(bool inSelection)
    {
        bool previousMode = inHeroSelectionMode;
        inHeroSelectionMode = inSelection;
        Debug.Log($"[MOBAGameManager] Hero selection mode changed: {previousMode} -> {inSelection}");
    }
    
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Debug.Log($"[MOBAGameManager] OnNetworkSpawn - IsServer={IsServer}, IsClient={IsClient}, inHeroSelectionMode={inHeroSelectionMode}");
            
            // Only initialize players if NOT in selection mode
            if (!inHeroSelectionMode)
            {
                Debug.Log("[MOBAGameManager] Not in hero selection mode - registering player connection callback");
                
                // Only the server handles player connections
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
                Debug.Log("[MOBAGameManager] In hero selection mode - NOT registering player connection callback");
                
                // Only register disconnect event for cleanup
                NetworkManager.Singleton.OnClientDisconnectCallback += OnPlayerDisconnected;
            }
        }
    }
    
    private void OnPlayerConnected(ulong clientId)
    {
        Debug.Log($"[MOBAGameManager] OnPlayerConnected called for client ID: {clientId}");
        
        // Don't spawn again if already spawned (prevents duplicates)
        if (playerTeams.ContainsKey(clientId))
        {
            Debug.LogWarning($"[MOBAGameManager] Player {clientId} already registered, ignoring duplicate connection.");
            return;
        }
        
        Debug.Log($"[MOBAGameManager] New player connected with ID: {clientId}");
        
        // Debug current state
        Debug.Log($"[MOBAGameManager] Current state: inHeroSelectionMode={inHeroSelectionMode}, playerHeroSelections.Count={playerHeroSelections.Count}");
        
        // If hero selection phase is complete, spawn with selected hero
        if (playerHeroSelections.Count > 0)
        {
            Debug.Log($"[MOBAGameManager] Spawning player {clientId} with selected hero");
            SpawnPlayerWithSelectedHero(clientId);
        }
        else if (!inHeroSelectionMode)
        {
            // Normal spawn if not in hero selection mode
            Debug.Log($"[MOBAGameManager] Spawning player {clientId} with default hero");
            SpawnPlayer(clientId);
        }
        else
        {
            Debug.Log($"[MOBAGameManager] In hero selection mode - not spawning player {clientId} yet");
        }
    }
    
    private void SpawnPlayer(ulong clientId)
    {
        try {
            // Assign to team 1 or 2 based on even/odd count
            int teamId = (playerSpawnCount % 2) + 1;
            playerTeams[clientId] = teamId;
            
            // Select spawn point
            Transform[] spawnPoints = (teamId == 1) ? team1SpawnPoints : team2SpawnPoints;
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                Debug.LogError($"[MOBAGameManager] Error: No spawn points configured for team {teamId}");
                return;
            }
            
            // Use modulo to select spawn point (cycle through available points)
            int spawnIndex = (playerSpawnCount / 2) % spawnPoints.Length;
            Transform spawnPoint = spawnPoints[spawnIndex];
            
            // Save spawn information
            Vector3 spawnPosition = spawnPoint.position;
            Quaternion spawnRotation = spawnPoint.rotation;
            
            Debug.Log($"[MOBAGameManager] Spawning player {clientId} as team {teamId} at position {spawnPosition}");
            
            // IMPORTANT: Instantiate directly at the spawn point position
            GameObject playerInstance = Instantiate(defaultPlayerPrefab, spawnPosition, spawnRotation);
            NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();
            
            if (networkObject != null)
            {
                // Increment counter after successful spawn
                playerSpawnCount++;
                
                // Add unique player ID for better identification in logs
                playerInstance.name = $"Player_{clientId}_Team{teamId}";
                
                // IMPORTANT: Verify the position before spawn
                Debug.Log($"[MOBAGameManager] Pre-Spawn Position: {playerInstance.transform.position}");
                
                // IMPORTANT: Set the position again to ensure
                playerInstance.transform.position = spawnPosition;
                playerInstance.transform.rotation = spawnRotation;
                
                // Spawn the object in the network
                networkObject.SpawnAsPlayerObject(clientId);
                
                // IMPORTANT: Force the position again after spawn
                playerInstance.transform.position = spawnPosition;
                playerInstance.transform.rotation = spawnRotation;
                
                Debug.Log($"[MOBAGameManager] Post-Spawn Position: {playerInstance.transform.position}");
                
                // IMPORTANT: Force synchronization across the network
                PlayerNetwork playerNetwork = playerInstance.GetComponent<PlayerNetwork>();
                if (playerNetwork != null)
                {
                    playerNetwork.SyncInitialTransformServerRpc(spawnPosition, spawnRotation);
                }
                
                // Notify the client about their team and position
                InitializePlayerClientRpc(clientId, teamId, spawnPosition, spawnRotation);
            }
            else
            {
                Debug.LogError("[MOBAGameManager] Player prefab does not have NetworkObject component");
                Destroy(playerInstance);
            }
        }
        catch (System.Exception e) {
            Debug.LogError($"[MOBAGameManager] Error al hacer spawn del jugador: {e.Message}\n{e.StackTrace}");
        }
    }
    
    public void StartGameWithHeroSelections(Dictionary<ulong, int> heroSelections)
    {
        if (!IsServer) return;
        
        Debug.Log("[MOBAGameManager] Iniciando juego con selecciones de héroes");
        
        // Verificar si tenemos definiciones de héroe
        if (availableHeroes == null || availableHeroes.Length == 0)
        {
            Debug.LogError("[MOBAGameManager] ¡ERROR CRÍTICO! El array availableHeroes es nulo o está vacío");
            return; // Salir para evitar errores posteriores
        }
        else
        {
            Debug.Log($"[MOBAGameManager] Disponibles {availableHeroes.Length} definiciones de héroe");
            
            // Imprimir detalles para depuración
            for (int i = 0; i < availableHeroes.Length; i++) {
                if (availableHeroes[i] != null) {
                    Debug.Log($"[MOBAGameManager] Héroe {i}: {availableHeroes[i].heroName}, Prefab: {(availableHeroes[i].modelPrefab != null ? "ASIGNADO" : "NULO")}");
                } else {
                    Debug.LogError($"[MOBAGameManager] ¡Héroe en índice {i} es NULO!");
                }
            }
        }
        
        // Limpia cualquier registro de evento anterior para evitar duplicados
        NetworkManager.Singleton.OnClientConnectedCallback -= OnPlayerConnected;
        
        // Actualiza el modo de selección
        inHeroSelectionMode = false;
        
        // IMPORTANTE: Almacena las selecciones de héroes
        playerHeroSelections = heroSelections;
        
        // Imprimir selecciones para depuración
        foreach (var selection in playerHeroSelections) {
            Debug.Log($"[MOBAGameManager] Jugador {selection.Key} seleccionó héroe índice {selection.Value}");
        }
        
        // Registra nuevamente el evento de conexión de jugadores
        NetworkManager.Singleton.OnClientConnectedCallback += OnPlayerConnected;
        
        // Registra los clientes conectados
        Debug.Log($"[MOBAGameManager] Clientes conectados: {NetworkManager.Singleton.ConnectedClientsIds.Count}");
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            Debug.Log($"[MOBAGameManager] Cliente ID conectado: {clientId}");
        }
        
        // Limpia cualquier instancia de jugador que pudiera haber sido creada
        foreach (var player in FindObjectsOfType<PlayerNetwork>())
        {
            // No eliminar si está en nuestras selecciones de héroe (jugador legítimo)
            if (playerHeroSelections.ContainsKey(player.OwnerClientId))
            {
                Debug.Log($"[MOBAGameManager] Manteniendo instancia legítima de jugador: {player.OwnerClientId}");
                continue;
            }
            
            // Limpiar instancia inesperada de jugador
            if (player.NetworkObject != null && player.NetworkObject.IsSpawned)
            {
                Debug.Log($"[MOBAGameManager] Limpiando instancia inesperada de jugador: {player.OwnerClientId}");
                player.NetworkObject.Despawn();
            }
        }
        
        // IMPORTANTE: Genera jugadores con sus héroes seleccionados
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            // Verificar si este cliente ya tiene un jugador spawneado para evitar duplicados
            bool yaSpawneado = false;
            foreach (var player in FindObjectsOfType<PlayerNetwork>())
            {
                if (player.OwnerClientId == clientId && player.IsSpawned)
                {
                    yaSpawneado = true;
                    Debug.Log($"[MOBAGameManager] Cliente {clientId} ya tiene una instancia de jugador, omitiendo spawn");
                    break;
                }
            }
            
            if (!yaSpawneado)
            {
                if (playerHeroSelections.TryGetValue(clientId, out int heroIndex))
                {
                    Debug.Log($"[MOBAGameManager] Generando jugador para cliente {clientId} con héroe índice {heroIndex}");
                    SpawnPlayerWithSelectedHero(clientId);
                }
                else
                {
                    Debug.LogWarning($"[MOBAGameManager] Cliente {clientId} no tiene selección de héroe");
                }
            }
        }
    }
    
    // Obtener el prefab correcto para el jugador basado en su selección de héroe
    private GameObject GetHeroPrefabForPlayer(ulong clientId)
    {
        try {
            // Verificar selección de héroe
            if (playerHeroSelections.TryGetValue(clientId, out int heroIndex))
            {
                // Verificar índice válido
                if (heroIndex >= 0 && availableHeroes != null && heroIndex < availableHeroes.Length)
                {
                    // Obtener definición del héroe
                    HeroDefinition heroDefinition = availableHeroes[heroIndex];
                    
                    // Devolver el prefab del héroe si existe
                    if (heroDefinition != null && heroDefinition.modelPrefab != null)
                    {
                        Debug.Log($"[MOBAGameManager] Usando prefab de héroe '{heroDefinition.heroName}' para cliente {clientId}");
                        return heroDefinition.modelPrefab;
                    }
                    else
                    {
                        Debug.LogWarning($"[MOBAGameManager] HeroDefinition o modelPrefab es nulo para índice {heroIndex}");
                        // Si la definición es válida pero el prefab es nulo, usar el defaultPlayerPrefab
                        if (heroDefinition != null) {
                            Debug.LogWarning($"[MOBAGameManager] La definición '{heroDefinition.heroName}' no tiene modelPrefab asignado");
                        }
                    }
                }
                else
                {
                    Debug.LogError($"[MOBAGameManager] Índice de héroe {heroIndex} fuera de rango o availableHeroes es nulo. " +
                                   $"availableHeroes?.Length: {availableHeroes?.Length ?? 0}");
                }
            }
            else
            {
                Debug.LogWarning($"[MOBAGameManager] No se encontró selección de héroe para cliente {clientId}");
            }
        }
        catch (System.Exception e) {
            Debug.LogError($"[MOBAGameManager] Error obteniendo prefab de héroe: {e.Message}");
        }
        
        // Si falla, usar prefab por defecto
        Debug.LogWarning($"[MOBAGameManager] Usando prefab de jugador por defecto para cliente {clientId}");
        return defaultPlayerPrefab;
    }
    
    // Obtener el nombre del héroe para logs
    private string GetHeroNameForPlayer(ulong clientId)
    {
        if (playerHeroSelections.TryGetValue(clientId, out int heroIndex))
        {
            if (heroIndex >= 0 && availableHeroes != null && heroIndex < availableHeroes.Length)
            {
                HeroDefinition heroDefinition = availableHeroes[heroIndex];
                return heroDefinition != null ? heroDefinition.heroName : "Unknown Hero";
            }
        }
        return "DefaultHero";
    }
    
    // Método para invocar en los clientes después de spawn
    [ClientRpc]
    private void InitializePlayerClientRpc(ulong clientId, int teamId, Vector3 spawnPosition, Quaternion spawnRotation)
    {
        Debug.Log($"[MOBAGameManager] Client {NetworkManager.Singleton.LocalClientId} received initialization for client {clientId}");
        
        // Only act if we are the target client
        if (NetworkManager.Singleton.LocalClientId != clientId)
        {
            return;
        }
        
        Debug.Log($"[MOBAGameManager] Initializing player: Team {teamId}, Position {spawnPosition}");
        
        // Find our local player object
        PlayerNetwork[] players = FindObjectsOfType<PlayerNetwork>();
        foreach (PlayerNetwork player in players)
        {
            if (player.IsLocalPlayer)
            {
                // Force correct position
                player.transform.position = spawnPosition;
                player.transform.rotation = spawnRotation;
                
                // Request the server to sync our position
                player.SyncInitialTransformServerRpc(spawnPosition, spawnRotation);
                
                Debug.Log($"[MOBAGameManager] Local player initialized at {spawnPosition}");
                
                // Change color based on team
                Renderer renderer = player.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // Apply appropriate team color
                    Color teamColor = (teamId == 1) ? team1Color : team2Color;
                    renderer.material.color = teamColor;
                    
                    Debug.Log($"[MOBAGameManager] Team {teamId} color applied to player");
                }
                
                break;
            }
        }
    }
    
    private void OnPlayerDisconnected(ulong clientId)
    {
        Debug.Log($"[MOBAGameManager] Player disconnected: {clientId}");
        
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

    // Spawn player with their selected hero
    private void SpawnPlayerWithSelectedHero(ulong clientId)
    {
        try {
            // Assign to team 1 or 2 based on even/odd count
            int teamId = (playerSpawnCount % 2) + 1;
            playerTeams[clientId] = teamId;
            
            // Select spawn point
            Transform[] spawnPoints = (teamId == 1) ? team1SpawnPoints : team2SpawnPoints;
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                Debug.LogError($"[MOBAGameManager] Error: No spawn points configured for team {teamId}");
                return;
            }
            
            // Use modulo to select spawn point (cycle through available points)
            int spawnIndex = (playerSpawnCount / 2) % spawnPoints.Length;
            Transform spawnPoint = spawnPoints[spawnIndex];
            
            // Save spawn information
            Vector3 spawnPosition = spawnPoint.position;
            Quaternion spawnRotation = spawnPoint.rotation;
            
            // Get the hero prefab based on player's selection
            GameObject heroPrefab = GetHeroPrefabForPlayer(clientId);
            if (heroPrefab == null) {
                Debug.LogError($"[MOBAGameManager] heroPrefab es NULL para cliente {clientId}");
                heroPrefab = defaultPlayerPrefab; // Usar el prefab por defecto como último recurso
            }
            
            Debug.Log($"[MOBAGameManager] Spawning player {clientId} as team {teamId} with hero '{GetHeroNameForPlayer(clientId)}' at position {spawnPosition}");
            
            // IMPORTANT: Instantiate directly at the spawn point position
            GameObject playerInstance = Instantiate(heroPrefab, spawnPosition, spawnRotation);
            if (playerInstance == null) {
                Debug.LogError("[MOBAGameManager] ¡Instancia del jugador es NULL después de Instantiate!");
                return;
            }
            
            NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();
            if (networkObject == null) {
                Debug.LogError("[MOBAGameManager] ¡Prefab no tiene componente NetworkObject!");
                Destroy(playerInstance);
                return;
            }
            
            // Antes de hacer spawn, configurar propiedades básicas
            if (playerHeroSelections.TryGetValue(clientId, out int heroIndex) && 
                heroIndex >= 0 && availableHeroes != null && heroIndex < availableHeroes.Length)
            {
                HeroDefinition heroDefinition = availableHeroes[heroIndex];
                if (heroDefinition != null)
                {
                    // Obtener el componente Hero (debe existir en el prefab)
                    Hero heroComponent = playerInstance.GetComponent<Hero>();
                    if (heroComponent != null)
                    {
                        heroComponent.heroName = heroDefinition.heroName;
                        heroComponent.heroClass = heroDefinition.heroClass;
                        
                        // Configurar PlayerStats si es posible
                        PlayerStats playerStats = playerInstance.GetComponent<PlayerStats>();
                        if (playerStats != null)
                        {
                            // Establecer valores directamente (evitando RPC)
                            playerStats.InitializeStatsDirectly(heroDefinition.baseHealth, 
                                                              heroDefinition.baseMana,
                                                              heroDefinition.healthRegen,
                                                              heroDefinition.manaRegen);
                        }
                    }
                }
            }
            
            // Increment counter after successful spawn
            playerSpawnCount++;
            
            // Add unique player ID for better identification in logs
            playerInstance.name = $"Player_{clientId}_Team{teamId}_Hero_{GetHeroNameForPlayer(clientId)}";
            
            // IMPORTANT: Verify the position before spawn
            Debug.Log($"[MOBAGameManager] Pre-Spawn Position: {playerInstance.transform.position}");
            
            // IMPORTANT: Set the position again to ensure
            playerInstance.transform.position = spawnPosition;
            playerInstance.transform.rotation = spawnRotation;
            
            // Spawn the object in the network
            networkObject.SpawnAsPlayerObject(clientId);
            
            // IMPORTANT: Force the position again after spawn
            playerInstance.transform.position = spawnPosition;
            playerInstance.transform.rotation = spawnRotation;
            
            Debug.Log($"[MOBAGameManager] Post-Spawn Position: {playerInstance.transform.position}");
            
            // IMPORTANT: Force synchronization across the network
            PlayerNetwork playerNetwork = playerInstance.GetComponent<PlayerNetwork>();
            if (playerNetwork != null)
            {
                playerNetwork.SyncInitialTransformServerRpc(spawnPosition, spawnRotation);
            }
            
            // Inicializar habilidades después de spawn
            StartCoroutine(InitializeHeroAfterSpawn(playerInstance));
            
            // Notify the client about their team and position
            InitializePlayerClientRpc(clientId, teamId, spawnPosition, spawnRotation);
        }
        catch (System.Exception e) {
            Debug.LogError($"[MOBAGameManager] Error al hacer spawn del jugador: {e.Message}\n{e.StackTrace}");
        }
    }
    
    // Inicializar el héroe después de un breve delay para asegurar sincronización
    private IEnumerator InitializeHeroAfterSpawn(GameObject playerInstance)
    {
        // Esperar para asegurar que el objeto está completamente spawneado
        yield return new WaitForSeconds(0.5f);
        
        try {
            if (playerInstance == null) {
                Debug.LogError("[MOBAGameManager] playerInstance es NULL en InitializeHeroAfterSpawn");
                yield break;
            }
            
            Hero heroComponent = playerInstance.GetComponent<Hero>();
            if (heroComponent != null)
            {
                Debug.Log($"[MOBAGameManager] Inicializando habilidades para héroe {heroComponent.heroName}");
                heroComponent.InitializeHeroAbilities();
            }
            else {
                Debug.LogError("[MOBAGameManager] No se encontró componente Hero en playerInstance");
            }
        }
        catch (System.Exception e) {
            Debug.LogError($"[MOBAGameManager] Error en InitializeHeroAfterSpawn: {e.Message}");
        }
    }
}