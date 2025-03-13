using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using System;

public class HeroSelectionManager : NetworkBehaviour
{
    [Header("UI References")]
    [SerializeField] private HeroSelectionUI heroSelectionUI;
    [SerializeField] private MOBAGameManager gameManager;
    [SerializeField] private GameObject connectionPanel; // Reference to hide connection UI during selection
    
    [Header("Hero Data")]
    [SerializeField] private HeroData[] availableHeroes;
    
    [Header("Settings")]
    [SerializeField] private float selectionTimeLimit = 30f; // Optional time limit for selection
    [SerializeField] private bool allowDuplicateHeroes = true; // Whether multiple players can select the same hero
    [SerializeField] private bool debugMode = true; // Enable additional debug logs
    
    // Network variables for syncing hero selections
    private NetworkVariable<float> selectionTimeRemaining = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Dictionary to track player selections, using NetworkList for sync
    private readonly NetworkList<PlayerHeroSelection> playerSelections;
    
    // Local tracking of our selection and ready state
    private int localSelectedHeroIndex = -1;
    private bool localPlayerReady = false;
    
    // Constructor for NetworkList initialization
    public HeroSelectionManager()
    {
        playerSelections = new NetworkList<PlayerHeroSelection>();
    }
    
    public override void OnNetworkSpawn()
    {
        // Subscribe to selection changes
        playerSelections.OnListChanged += OnPlayerSelectionsChanged;
        
        if (IsServer)
        {
            Debug.Log("[HeroSelectionManager] Initializing selection phase as server");
            // Server initializes the selection phase
            InitializeSelectionPhase();
        }
        
        if (IsClient)
        {
            Debug.Log("[HeroSelectionManager] Client connected to selection phase");
            // Hide connection panel and show hero selection
            if (connectionPanel != null)
            {
                connectionPanel.SetActive(false);
            }
            
            if (heroSelectionUI != null)
            {
                heroSelectionUI.Show();
            }
            else
            {
                Debug.LogError("[HeroSelectionManager] heroSelectionUI is null!");
                // Try to find it
                heroSelectionUI = FindObjectOfType<HeroSelectionUI>();
                if (heroSelectionUI != null)
                {
                    heroSelectionUI.Show();
                }
            }
            
            // Reset local state
            localSelectedHeroIndex = -1;
            localPlayerReady = false;
            
            // Log available heroes for debugging
            if (debugMode)
            {
                LogAvailableHeroes();
            }
        }
    }
    
    private void LogAvailableHeroes()
    {
        Debug.Log($"[HeroSelectionManager] Available Heroes: {availableHeroes?.Length ?? 0}");
        
        if (availableHeroes != null)
        {
            for (int i = 0; i < availableHeroes.Length; i++)
            {
                if (availableHeroes[i] != null)
                {
                    Debug.Log($"[Hero {i}] Name: {availableHeroes[i].heroName}, Class: {availableHeroes[i].heroClass}");
                }
                else
                {
                    Debug.LogWarning($"[Hero {i}] NULL REFERENCE!");
                }
            }
        }
    }
    
    public override void OnNetworkDespawn()
    {
        // Unsubscribe from events
        playerSelections.OnListChanged -= OnPlayerSelectionsChanged;
    }
    
    private void Update()
    {
        // Only the server needs to update the timer
        if (IsServer && selectionTimeLimit > 0)
        {
            // Update timer each frame for more precise timing
            selectionTimeRemaining.Value = Mathf.Max(0, selectionTimeRemaining.Value - Time.deltaTime);
            
            // If time's up, force all players to confirm
            if (selectionTimeRemaining.Value <= 0)
            {
                ForceConfirmAllPlayers();
            }
            // Notify players when time is running low
            else if (selectionTimeRemaining.Value <= 10 && selectionTimeRemaining.Value % 1 < 0.1f)
            {
                int secondsLeft = Mathf.CeilToInt(selectionTimeRemaining.Value);
                NotifyTimeRemainingClientRpc(secondsLeft);
            }
        }
    }
    
    [ClientRpc]
    private void NotifyTimeRemainingClientRpc(int secondsLeft)
    {
        if (secondsLeft <= 5)
        {
            Debug.Log($"<color=red>WARNING: Only {secondsLeft} seconds left to select a hero!</color>");
        }
        else if (secondsLeft <= 10)
        {
            Debug.Log($"<color=yellow>Time running out: {secondsLeft} seconds left for hero selection</color>");
        }
    }
    
    private void InitializeSelectionPhase()
    {
        if (IsServer)
        {
            Debug.Log("Initializing hero selection phase...");
            
            // Clear previous selections
            playerSelections.Clear();
            
            // Add initial entries for all connected players
            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                playerSelections.Add(new PlayerHeroSelection 
                { 
                    clientId = clientId,
                    selectedHeroIndex = -1, // -1 means no selection yet
                    isReady = false
                });
                
                Debug.Log($"[HeroSelectionManager] Added player {clientId} to selection tracker");
            }
            
            // Start the selection timer
            if (selectionTimeLimit > 0)
            {
                selectionTimeRemaining.Value = selectionTimeLimit;
                Debug.Log($"[HeroSelectionManager] Started selection timer: {selectionTimeLimit}s");
            }
        }
    }
    
    private void ForceConfirmAllPlayers()
    {
        if (IsServer)
        {
            Debug.Log("[HeroSelectionManager] Time's up! Forcing player confirmations");
            // For each player that hasn't made a selection, assign a random hero
            // And for each player that hasn't confirmed, force confirm
            bool anyConfirmationForced = false;
            
            for (int i = 0; i < playerSelections.Count; i++)
            {
                PlayerHeroSelection selection = playerSelections[i];
                
                // If no hero selected, assign random one
                if (selection.selectedHeroIndex < 0)
                {
                    selection.selectedHeroIndex = UnityEngine.Random.Range(0, availableHeroes.Length);
                    Debug.Log($"[HeroSelectionManager] Assigned random hero {selection.selectedHeroIndex} to player {selection.clientId}");
                    anyConfirmationForced = true;
                }
                
                // If not ready, force ready
                if (!selection.isReady)
                {
                    selection.isReady = true;
                    anyConfirmationForced = true;
                    NotifyForcedSelectionClientRpc(selection.clientId, selection.selectedHeroIndex);
                }
                
                // Update in the list
                playerSelections[i] = selection;
            }
            
            if (anyConfirmationForced)
            {
                // Allow a moment for clients to see the forced selection notifications
                StartCoroutine(DelayedCheckAllPlayersReady(1.0f));
            }
            else
            {
                // Check if we should start the game
                CheckAllPlayersReady();
            }
        }
    }
    
    [ClientRpc]
    private void NotifyForcedSelectionClientRpc(ulong clientId, int heroIndex)
    {
        // Only show notification to the affected player
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            if (availableHeroes != null && heroIndex >= 0 && heroIndex < availableHeroes.Length)
            {
                Debug.Log($"<color=orange>Time's up! You've been automatically assigned {availableHeroes[heroIndex].heroName}</color>");
            }
            else
            {
                Debug.Log("<color=orange>Time's up! A hero has been automatically selected for you</color>");
            }
        }
    }
    
    private System.Collections.IEnumerator DelayedCheckAllPlayersReady(float delay)
    {
        yield return new UnityEngine.WaitForSeconds(delay);
        CheckAllPlayersReady();
    }
    
    // Called by UI when player selects a hero (but doesn't confirm yet)
    public void SelectHero(int heroIndex)
    {
        if (!IsClient) return;
        
        if (heroIndex < 0 || heroIndex >= availableHeroes.Length)
        {
            Debug.LogError($"[HeroSelectionManager] Invalid hero index: {heroIndex}");
            return;
        }
        
        // Save local selection
        localSelectedHeroIndex = heroIndex;
        
        // Send selection to server
        UpdateHeroSelectionServerRpc(heroIndex);
        
        Debug.Log($"[HeroSelectionManager] Selected hero: {availableHeroes[heroIndex].heroName}");
    }
    
    // Called by UI when player confirms their selection
    public void ConfirmHeroSelection()
    {
        if (!IsClient || localSelectedHeroIndex < 0) return;
        
        Debug.Log($"[HeroSelectionManager] Confirming hero selection: {availableHeroes[localSelectedHeroIndex].heroName}");
        
        // Update local state
        localPlayerReady = true;
        
        // Send confirmation to server
        ConfirmHeroSelectionServerRpc();
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void UpdateHeroSelectionServerRpc(int heroIndex, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        
        // Validate hero index
        if (heroIndex < 0 || heroIndex >= availableHeroes.Length)
        {
            Debug.LogWarning($"Invalid hero index: {heroIndex}");
            return;
        }
        
        // Check if this hero is available (if we don't allow duplicates)
        if (!allowDuplicateHeroes && IsHeroSelected(heroIndex, clientId))
        {
            Debug.LogWarning($"Hero {availableHeroes[heroIndex].heroName} already selected by another player");
            NotifyHeroUnavailableClientRpc(clientId, heroIndex);
            return;
        }
        
        // Find this player's selection entry
        for (int i = 0; i < playerSelections.Count; i++)
        {
            if (playerSelections[i].clientId == clientId)
            {
                // Update the selection
                PlayerHeroSelection selection = playerSelections[i];
                selection.selectedHeroIndex = heroIndex;
                playerSelections[i] = selection;
                Debug.Log($"[HeroSelectionManager] Player {clientId} selected hero: {availableHeroes[heroIndex].heroName}");
                break;
            }
        }
    }
    
    [ClientRpc]
    private void NotifyHeroUnavailableClientRpc(ulong clientId, int heroIndex)
    {
        // Only show notification to the affected player
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log($"<color=red>Hero {availableHeroes[heroIndex].heroName} is already selected by another player!</color>");
            
            // Reset local selection state
            if (localSelectedHeroIndex == heroIndex)
            {
                localSelectedHeroIndex = -1;
            }
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void ConfirmHeroSelectionServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        Debug.Log($"[HeroSelectionManager] Server received confirmation from client {clientId}");
        
        // Find this player's selection entry
        for (int i = 0; i < playerSelections.Count; i++)
        {
            if (playerSelections[i].clientId == clientId)
            {
                // Make sure they have a valid selection
                if (playerSelections[i].selectedHeroIndex >= 0)
                {
                    // Update the ready state
                    PlayerHeroSelection selection = playerSelections[i];
                    selection.isReady = true;
                    playerSelections[i] = selection;
                    
                    Debug.Log($"[HeroSelectionManager] Client {clientId} confirmed hero: {availableHeroes[selection.selectedHeroIndex].heroName}");
                    
                    // Check if all players are ready
                    CheckAllPlayersReady();
                }
                else
                {
                    Debug.LogWarning($"[HeroSelectionManager] Client {clientId} tried to confirm without selecting a hero");
                    NotifyInvalidConfirmationClientRpc(clientId);
                }
                break;
            }
        }
    }
    
    [ClientRpc]
    private void NotifyInvalidConfirmationClientRpc(ulong clientId)
    {
        // Only show notification to the affected player
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("<color=red>You need to select a hero before confirming!</color>");
            
            // Reset local ready state
            localPlayerReady = false;
        }
    }
    
    private bool IsHeroSelected(int heroIndex, ulong excludeClientId)
    {
        // Check if any other player has already selected this hero
        foreach (var selection in playerSelections)
        {
            if (selection.clientId != excludeClientId && selection.selectedHeroIndex == heroIndex)
            {
                return true;
            }
        }
        return false;
    }
    
    private void CheckAllPlayersReady()
    {
        if (!IsServer) return;
        
        bool allReady = true;
        int readyCount = 0;
        int totalPlayers = playerSelections.Count;
        
        // Check if all players have made a selection and are ready
        foreach (var selection in playerSelections)
        {
            if (!selection.isReady || selection.selectedHeroIndex < 0)
            {
                allReady = false;
            }
            else
            {
                readyCount++;
            }
        }
        
        Debug.Log($"[HeroSelectionManager] Player readiness: {readyCount}/{totalPlayers} ready");
        
        if (allReady)
        {
            Debug.Log("[HeroSelectionManager] All players are ready! Starting the game...");
            StartGame();
        }
        else if (readyCount > 0)
        {
            // Notify about readiness status
            NotifyReadinessStatusClientRpc(readyCount, totalPlayers);
        }
    }
    
    [ClientRpc]
    private void NotifyReadinessStatusClientRpc(int readyCount, int totalPlayers)
    {
        if (IsOwner && !localPlayerReady)
        {
            Debug.Log($"<color=yellow>{readyCount} out of {totalPlayers} players are ready</color>");
        }
    }
    
    private void StartGame()
    {
        if (!IsServer) return;
        
        Debug.Log("[HeroSelectionManager] Starting game with hero selections");
        
        // Stop the timer if it's running
        selectionTimeRemaining.Value = 0;
        
        // Notify clients that the game is starting
        StartGameClientRpc();
        
        // Actual game start logic - pass hero selections to the game manager
        if (gameManager != null)
        {
            // Convert to a simpler format for the game manager
            Dictionary<ulong, int> playerHeroSelections = new Dictionary<ulong, int>();
            foreach (var selection in playerSelections)
            {
                playerHeroSelections[selection.clientId] = selection.selectedHeroIndex;
                Debug.Log($"[HeroSelectionManager] Player {selection.clientId} selected hero index: {selection.selectedHeroIndex}");
            }
            
            // Tell the game manager to spawn players with their selected heroes
            gameManager.StartGameWithHeroSelections(playerHeroSelections);
        }
        else
        {
            Debug.LogError("[HeroSelectionManager] Game Manager reference is missing! Cannot start the game.");
            // Try to find it
            gameManager = FindObjectOfType<MOBAGameManager>();
            if (gameManager != null)
            {
                Dictionary<ulong, int> playerHeroSelections = new Dictionary<ulong, int>();
                foreach (var selection in playerSelections)
                {
                    playerHeroSelections[selection.clientId] = selection.selectedHeroIndex;
                }
                gameManager.StartGameWithHeroSelections(playerHeroSelections);
            }
            else
            {
                Debug.LogError("[HeroSelectionManager] Still couldn't find Game Manager!");
            }
        }
    }
    
    [ClientRpc]
    private void StartGameClientRpc()
    {
        // Hide the hero selection UI
        if (heroSelectionUI != null)
        {
            heroSelectionUI.Hide();
        }
        
        Debug.Log("Game is starting! Hero selection phase complete.");
    }
    
    private void OnPlayerSelectionsChanged(NetworkListEvent<PlayerHeroSelection> changeEvent)
    {
        if (!IsClient) return;
        
        // Update UI to reflect the changes in player selections
        if (heroSelectionUI != null)
        {
            // Update all player indicators
            foreach (var selection in playerSelections)
            {
                heroSelectionUI.UpdatePlayerSelection(
                    selection.clientId, 
                    selection.selectedHeroIndex, 
                    selection.isReady);
            }
        }
    }
    
    // Public getter for hero data
    public HeroData GetHeroData(int index)
    {
        if (availableHeroes != null && index >= 0 && index < availableHeroes.Length)
        {
            return availableHeroes[index];
        }
        return null;
    }
    
    // Public getter for time remaining
    public float GetTimeRemaining()
    {
        return selectionTimeRemaining.Value;
    }
    
    // Structure to track player hero selections in the NetworkList
    public struct PlayerHeroSelection : INetworkSerializable, IEquatable<PlayerHeroSelection>
    {
        public ulong clientId;
        public int selectedHeroIndex;
        public bool isReady;
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref selectedHeroIndex);
            serializer.SerializeValue(ref isReady);
        }
        
        public bool Equals(PlayerHeroSelection other)
        {
            return clientId == other.clientId && 
                   selectedHeroIndex == other.selectedHeroIndex && 
                   isReady == other.isReady;
        }
        
        public override bool Equals(object obj)
        {
            return obj is PlayerHeroSelection selection && Equals(selection);
        }
        
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + clientId.GetHashCode();
                hash = hash * 23 + selectedHeroIndex.GetHashCode();
                hash = hash * 23 + isReady.GetHashCode();
                return hash;
            }
        }
    }

    public void UpdateHeroSelectionUI()
{
    // Update UI to show available heroes
    if (heroSelectionUI != null)
    {
        // Make sure UI is visible
        heroSelectionUI.gameObject.SetActive(true);
        heroSelectionUI.Show();
        
        // Update the UI with available heroes
        heroSelectionUI.UpdateHeroButtonsDisplay(availableHeroes);
    }
    else
    {
        Debug.LogError("[HeroSelectionManager] Hero Selection UI reference is missing!");
    }
}
}