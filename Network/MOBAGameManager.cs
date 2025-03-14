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
    
    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;
    [SerializeField] private bool forceClientRespawn = true; // NUEVO: Forzar respawn del cliente
    
    // Variable to control if we're in hero selection phase
    private bool inHeroSelectionMode = false;

    // Dictionary to track players and their teams
    private Dictionary<ulong, int> playerTeams = new Dictionary<ulong, int>();
    // Dictionary to track which hero each player selected
    private Dictionary<ulong, int> playerHeroSelections = new Dictionary<ulong, int>();
    private int playerSpawnCount = 0;
    
    // NUEVO: Variables para control de estado del juego
    private NetworkVariable<bool> gameStarted = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // NUEVO: Variables para controlar el spawn de jugadores
    private NetworkVariable<bool> clientSpawnRequested = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
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
        
        // NUEVO: Suscribirse a cambios de estado del juego
        gameStarted.OnValueChanged += OnGameStartedChanged;
        clientSpawnRequested.OnValueChanged += OnClientSpawnRequestedChanged;
    }
    
    // NUEVO: Método para manejar cambios en el estado del juego
    private void OnGameStartedChanged(bool oldValue, bool newValue)
    {
        if (newValue)
        {
            Debug.Log("[MOBAGameManager] Game has started!");
            
            // Si somos un cliente y el juego acaba de comenzar, asegurarnos de que nuestro jugador esté spawneado
            if (IsClient && !IsServer)
            {
                Debug.Log("[MOBAGameManager] Client detected game start, requesting spawn");
                RequestSpawnPlayerServerRpc();
                
                // NUEVO: Programar verificación adicional para asegurar el spawn
                StartCoroutine(EnsureClientSpawn());
            }
        }
    }
    
    // NUEVO: Método para asegurar el spawn del cliente
    private IEnumerator EnsureClientSpawn()
    {
        // Esperar un poco para dar tiempo al spawn normal
        yield return new WaitForSeconds(3.0f);
        
        if (IsClient && !IsServer && forceClientRespawn)
        {
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            bool playerFound = false;
            
            // Buscar objetos de jugador
            foreach (var netObj in FindObjectsOfType<NetworkObject>())
            {
                if (netObj.IsPlayerObject && netObj.OwnerClientId == localClientId)
                {
                    playerFound = true;
                    Debug.Log("[MOBAGameManager] Found client player object, no emergency respawn needed");
                    break;
                }
            }
            
            if (!playerFound)
            {
                Debug.Log("[MOBAGameManager] No player object found after delay! Requesting emergency spawn");
                EmergencySpawnRequestServerRpc();
                
                // Ocultar interfaz de selección de héroe en el cliente
                HeroSelectionUI selectionUI = FindObjectOfType<HeroSelectionUI>();
                if (selectionUI != null)
                {
                    selectionUI.Hide();
                }
            }
        }
    }
    
    // NUEVO: Método para manejar solicitudes de spawn del cliente
    private void OnClientSpawnRequestedChanged(bool oldValue, bool newValue)
    {
        if (newValue && IsServer)
        {
            Debug.Log("[MOBAGameManager] Client spawn request detected");
            
            // Iterar a través de todos los clientes conectados
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                // Saltar el servidor/host
                if (clientId == NetworkManager.ServerClientId)
                    continue;
                
                // Verificar si ya tienen un jugador
                bool hasPlayer = false;
                foreach (var netObj in FindObjectsOfType<NetworkObject>())
                {
                    if (netObj.IsPlayerObject && netObj.OwnerClientId == clientId)
                    {
                        hasPlayer = true;
                        break;
                    }
                }
                
                if (!hasPlayer)
                {
                    Debug.Log($"[MOBAGameManager] Spawning player for client {clientId} after client request");
                    
                    // Verificar si tenemos una selección de héroe
                    if (playerHeroSelections.TryGetValue(clientId, out int heroIndex))
                    {
                        SpawnPlayerWithSelectedHero(clientId);
                    }
                    else
                    {
                        // Alternativa segura: usar héroe por defecto
                        SpawnPlayer(clientId);
                    }
                }
            }
            
            // Reiniciar la bandera después de procesar
            clientSpawnRequested.Value = false;
        }
    }
    
    // NUEVO: ServerRpc para emergencias - si el cliente sigue sin aparecer
    [ServerRpc(RequireOwnership = false)]
    private void EmergencySpawnRequestServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        Debug.Log($"[MOBAGameManager] EMERGENCY spawn request from client {clientId}");
        
        // Buscar y eliminar cualquier objeto de jugador incompleto o inválido
        foreach (var netObj in FindObjectsOfType<NetworkObject>())
        {
            if (netObj.IsPlayerObject && netObj.OwnerClientId == clientId)
            {
                Debug.Log($"[MOBAGameManager] Removing potentially corrupted player object for client {clientId}");
                netObj.Despawn(true);
            }
        }
        
        // Forzar spawn con seguridad adicional
        bool success = false;
        
        try
        {
            if (playerHeroSelections.TryGetValue(clientId, out int heroIndex))
            {
                success = EmergencySpawnPlayer(clientId, heroIndex);
            }
            else
            {
                // Sin selección de héroe, usar valor por defecto seguro
                success = EmergencySpawnPlayer(clientId, 0);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[MOBAGameManager] Error en spawning de emergencia: {e.Message}");
        }
        
        // Confirmación de éxito o fracaso
        EmergencySpawnResultClientRpc(success, clientId);
    }
    
    // NUEVO: Método de spawning seguro para emergencias
    private bool EmergencySpawnPlayer(ulong clientId, int heroIndex)
    {
        try
        {
            // Asignar equipo
            int teamId = (playerSpawnCount % 2) + 1;
            playerTeams[clientId] = teamId;
            
            // Punto de spawn seguro (centro del mapa o primera posición)
            Vector3 safePosition = new Vector3(0, 5, 0); // Posición segura por defecto
            Quaternion safeRotation = Quaternion.identity;
            
            // Intentar usar spawn points si están disponibles
            Transform[] spawnPoints = (teamId == 1) ? team1SpawnPoints : team2SpawnPoints;
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                safePosition = spawnPoints[0].position;
                safeRotation = spawnPoints[0].rotation;
            }
            
            // Determinar prefab seguro
            GameObject safePrefab = defaultPlayerPrefab;
            if (availableHeroes != null && heroIndex >= 0 && heroIndex < availableHeroes.Length)
            {
                HeroDefinition heroDef = availableHeroes[heroIndex];
                if (heroDef != null && heroDef.modelPrefab != null)
                {
                    safePrefab = heroDef.modelPrefab;
                }
            }
            
            // Instanciar con seguridad
            GameObject playerInstance = Instantiate(safePrefab, safePosition, safeRotation);
            NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();
            
            if (networkObject != null)
            {
                playerSpawnCount++;
                playerInstance.name = $"EmergencyPlayer_{clientId}_Team{teamId}";
                
                // Spawn como objeto de jugador
                networkObject.SpawnAsPlayerObject(clientId);
                
                // Forzar posición después de spawn
                playerInstance.transform.position = safePosition;
                playerInstance.transform.rotation = safeRotation;
                
                // Notificar al cliente
                InitializePlayerClientRpc(clientId, teamId, safePosition, safeRotation);
                NotifyPlayerReadyClientRpc(clientId);
                
                return true;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[MOBAGameManager] Error crítico en emergency spawn: {e.Message}\n{e.StackTrace}");
        }
        
        return false;
    }
    
    // NUEVO: Notificar resultado del emergency spawn
    [ClientRpc]
    private void EmergencySpawnResultClientRpc(bool success, ulong targetClientId)
    {
        if (NetworkManager.Singleton.LocalClientId != targetClientId)
            return;
            
        if (success)
        {
            Debug.Log("[MOBAGameManager] Emergency spawn successful!");
            
            // Ocultar interfaz de selección de héroe si sigue visible
            HeroSelectionUI selectionUI = FindObjectOfType<HeroSelectionUI>();
            if (selectionUI != null)
            {
                selectionUI.Hide();
            }
        }
        else
        {
            Debug.LogError("[MOBAGameManager] Emergency spawn failed! Try restarting the game.");
        }
    }
    
    // MODIFICADO: Método para verificar que el jugador del cliente esté spawneado
    private IEnumerator CheckClientPlayerSpawn()
    {
        yield return new WaitForSeconds(1.0f); // Esperar un momento para que el servidor procese
        
        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        bool playerFound = false;
        
        // Verificar si ya tenemos un objeto jugador
        foreach (var netObj in FindObjectsOfType<NetworkObject>())
        {
            if (netObj.IsPlayerObject && netObj.OwnerClientId == localClientId)
            {
                playerFound = true;
                Debug.Log("[MOBAGameManager] Client player already exists");
                break;
            }
        }
        
        // Si no encontramos un jugador, solicitar spawn al servidor
        if (!playerFound)
        {
            Debug.Log("[MOBAGameManager] Client player not found, requesting spawn from server");
            RequestSpawnPlayerServerRpc();
            
            // Ocultar interfaz de selección si aún está visible
            HeroSelectionUI selectionUI = FindObjectOfType<HeroSelectionUI>();
            if (selectionUI != null)
            {
                selectionUI.Hide();
            }
        }
    }
    
    // MODIFICADO: ServerRpc para que un cliente solicite ser spawneado
    [ServerRpc(RequireOwnership = false)]
    private void RequestSpawnPlayerServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        Debug.Log($"[MOBAGameManager] Received spawn request from client {clientId}");
        
        // Activar la variable de red que desencadena el spawning
        clientSpawnRequested.Value = true;
        
        // Verificar si ya tiene un jugador spawneado
        bool alreadySpawned = false;
        foreach (var netObj in FindObjectsOfType<NetworkObject>())
        {
            if (netObj.IsPlayerObject && netObj.OwnerClientId == clientId)
            {
                alreadySpawned = true;
                Debug.Log($"[MOBAGameManager] Client {clientId} already has a player spawned");
                break;
            }
        }
        
        // Si no está spawneado, spawnear el jugador
        if (!alreadySpawned)
        {
            // Verificar si tenemos selección de héroe para este cliente
            if (playerHeroSelections.TryGetValue(clientId, out int heroIndex))
            {
                Debug.Log($"[MOBAGameManager] Spawning player for client {clientId} with hero index {heroIndex}");
                SpawnPlayerWithSelectedHero(clientId);
            }
            else
            {
                Debug.Log($"[MOBAGameManager] No hero selection found for client {clientId}, spawning with default");
                SpawnPlayer(clientId);
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
        Debug.Log($"[MOBAGameManager] Current state: inHeroSelectionMode={inHeroSelectionMode}, gameStarted={gameStarted.Value}, playerHeroSelections.Count={playerHeroSelections.Count}");
        
        // If game has started and we have hero selections, spawn with selected hero
        if (gameStarted.Value && playerHeroSelections.Count > 0)
        {
            if (playerHeroSelections.TryGetValue(clientId, out int _))
            {
                Debug.Log($"[MOBAGameManager] Spawning player {clientId} with selected hero");
                SpawnPlayerWithSelectedHero(clientId);
            }
            else
            {
                Debug.Log($"[MOBAGameManager] No hero selection found for client {clientId}, spawning with default");
                SpawnPlayer(clientId);
            }
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
                
                // NUEVO: Notificar explícitamente al cliente que su jugador está listo
                NotifyPlayerReadyClientRpc(clientId);
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
            if (showDebugLogs)
            {
                for (int i = 0; i < availableHeroes.Length; i++) {
                    if (availableHeroes[i] != null) {
                        Debug.Log($"[MOBAGameManager] Héroe {i}: {availableHeroes[i].heroName}, Prefab: {(availableHeroes[i].modelPrefab != null ? "ASIGNADO" : "NULO")}");
                    } else {
                        Debug.LogError($"[MOBAGameManager] ¡Héroe en índice {i} es NULO!");
                    }
                }
            }
        }
        
        // Limpia cualquier registro de evento anterior para evitar duplicados
        NetworkManager.Singleton.OnClientConnectedCallback -= OnPlayerConnected;
        
        // Actualiza el modo de selección
        inHeroSelectionMode = false;
        
        // IMPORTANTE: Almacena las selecciones de héroes
        playerHeroSelections = new Dictionary<ulong, int>(heroSelections);
        
        // Imprimir selecciones para depuración
        if (showDebugLogs)
        {
            foreach (var selection in playerHeroSelections) {
                Debug.Log($"[MOBAGameManager] Jugador {selection.Key} seleccionó héroe índice {selection.Value}");
            }
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
        // Usar una lista temporal para evitar problemas de modificación durante la iteración
        List<ulong> clientesToSpawn = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        
        foreach (var clientId in clientesToSpawn)
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
        
        // NUEVO: Marcar que el juego ha iniciado
        gameStarted.Value = true;
        
        // NUEVO: Avisar a todos los clientes que el juego ha iniciado
        NotifyGameStartedClientRpc();
    }
    
    // NUEVO: Método para notificar a los clientes que el juego ha iniciado
    [ClientRpc]
    private void NotifyGameStartedClientRpc()
    {
        Debug.Log("[MOBAGameManager] Juego iniciado, notificación recibida en el cliente");
        
        // Ocultar interfaz de selección de héroe explícitamente
        HeroSelectionUI selectionUI = FindObjectOfType<HeroSelectionUI>();
        if (selectionUI != null)
        {
            selectionUI.Hide();
        }
        else
        {
            Debug.LogWarning("[MOBAGameManager] No se encontró HeroSelectionUI para ocultar");
        }
        
        // Como cliente, verificar que tenemos un jugador spawneado
        if (IsClient && !IsServer)
        {
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            bool playerFound = false;
            
            foreach (var netObj in FindObjectsOfType<NetworkObject>())
            {
                if (netObj.IsPlayerObject && netObj.OwnerClientId == localClientId)
                {
                    playerFound = true;
                    Debug.Log("[MOBAGameManager] Client already has a player spawned");
                    break;
                }
            }
            
            if (!playerFound)
            {
                Debug.Log("[MOBAGameManager] Client has no player, requesting spawn from server");
                RequestSpawnPlayerServerRpc();
                
                // Programar verificación adicional por si falla
                StartCoroutine(EnsureClientSpawn());
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
        
        // Clean up game state subscriptions
        gameStarted.OnValueChanged -= OnGameStartedChanged;
        clientSpawnRequested.OnValueChanged -= OnClientSpawnRequestedChanged;
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
            
            // NUEVO: Notificar al cliente específico que su jugador está listo
            NotifyPlayerReadyClientRpc(clientId);
        }
        catch (System.Exception e) {
            Debug.LogError($"[MOBAGameManager] Error al hacer spawn del jugador: {e.Message}\n{e.StackTrace}");
        }
    }
    
    // NUEVO: Método para notificar al cliente que su jugador está listo
    [ClientRpc]
    private void NotifyPlayerReadyClientRpc(ulong targetClientId)
    {
        // Solo procesar si somos el cliente objetivo
        if (NetworkManager.Singleton.LocalClientId != targetClientId)
            return;
            
        Debug.Log($"[MOBAGameManager] Cliente {targetClientId} recibió notificación de que su jugador está listo");
        
        // Buscar el objeto del jugador local
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
            Debug.Log("[MOBAGameManager] Jugador local encontrado, forzando sincronización de posición");
            
            // Forzar actualización de posición
            Transform spawnPoint = null;
            int teamId = 1; // Por defecto equipo 1
            
            // Intentar determinar el equipo
            if (playerTeams.TryGetValue(targetClientId, out int team))
            {
                teamId = team;
            }
            
            // Buscar punto de spawn
            Transform[] spawnPoints = (teamId == 1) ? team1SpawnPoints : team2SpawnPoints;
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                int index = 0; // Por defecto el primer punto
                spawnPoint = spawnPoints[index];
            }
            
            // Si tenemos un punto de spawn, forzar la posición
            if (spawnPoint != null)
            {
                Vector3 position = spawnPoint.position;
                Quaternion rotation = spawnPoint.rotation;
                
                // Teletransportar y sincronizar
                localPlayer.transform.position = position;
                localPlayer.transform.rotation = rotation;
                localPlayer.SyncInitialTransformServerRpc(position, rotation);
                
                Debug.Log($"[MOBAGameManager] Forzando posición del cliente a {position}");
            }
            
            // Ocultar interfaz de selección
            HeroSelectionUI selectionUI = FindObjectOfType<HeroSelectionUI>();
            if (selectionUI != null)
            {
                selectionUI.Hide();
            }
        }
        else
        {
            Debug.LogWarning("[MOBAGameManager] No se pudo encontrar el jugador local para sincronizar posición");
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
    
    // NUEVO: Método público para verificar si el juego ha comenzado
    public bool HasGameStarted()
    {
        return gameStarted.Value;
    }
}