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
        }
    }
    
    public override void OnNetworkDespawn()
    {
        // Unsubscribe from events
        playerSelections.OnListChanged -= OnPlayerSelectionsChanged;
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
            }
            
            // Start the selection timer if using time limit
            if (selectionTimeLimit > 0)
            {
                selectionTimeRemaining.Value = selectionTimeLimit;
                InvokeRepeating(nameof(UpdateSelectionTimer), 1f, 1f);
            }
        }
    }
    
    private void UpdateSelectionTimer()
    {
        if (IsServer)
        {
            selectionTimeRemaining.Value -= 1f;
            
            if (selectionTimeRemaining.Value <= 0)
            {
                // Time's up, force all players to confirm with current selections
                ForceConfirmAllPlayers();
                CancelInvoke(nameof(UpdateSelectionTimer));
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
            for (int i = 0; i < playerSelections.Count; i++)
            {
                PlayerHeroSelection selection = playerSelections[i];
                
                // If no hero selected, assign random one
                if (selection.selectedHeroIndex < 0)
                {
                    selection.selectedHeroIndex = UnityEngine.Random.Range(0, availableHeroes.Length);
                    Debug.Log($"[HeroSelectionManager] Assigned random hero {selection.selectedHeroIndex} to player {selection.clientId}");
                }
                
                // Force ready
                selection.isReady = true;
                
                // Update in the list
                playerSelections[i] = selection;
            }
            
            // Check if we should start the game
            CheckAllPlayersReady();
        }
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
            // Could send a notification to the client here
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
                }
                break;
            }
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
    }
    
    private void StartGame()
    {
        if (!IsServer) return;
        
        Debug.Log("[HeroSelectionManager] Starting game with hero selections");
        
        // Stop the timer if it's running
        if (selectionTimeLimit > 0)
        {
            CancelInvoke(nameof(UpdateSelectionTimer));
        }
        
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
        if (index >= 0 && index < availableHeroes.Length)
        {
            return availableHeroes[index];
        }
        return null;
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
}