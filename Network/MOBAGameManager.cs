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
        // Assign to team 1 or 2 based on even/odd count
        int teamId = (playerSpawnCount % 2) + 1;
        playerTeams[clientId] = teamId;
        
        // Select spawn point
        Transform[] spawnPoints = (teamId == 1) ? team1SpawnPoints : team2SpawnPoints;
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError($"[MANAGER] Error: No spawn points configured for team {teamId}");
            return;
        }
        
        // Use modulo to select spawn point (cycle through available points)
        int spawnIndex = (playerSpawnCount / 2) % spawnPoints.Length;
        Transform spawnPoint = spawnPoints[spawnIndex];
        
        Debug.Log($"[MANAGER] Spawning player {clientId} as team {teamId} at position {spawnPoint.position}");
        
        // Instantiate player
        GameObject playerInstance = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
        NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();
        
        if (networkObject != null)
        {
            // Increment counter after successful spawn
            playerSpawnCount++;
            
            // Add custom player ID for better identification in logs
            playerInstance.name = $"Player_{clientId}_Team{teamId}";
            
            // Spawn the object on the network
            networkObject.SpawnAsPlayerObject(clientId);
            
            // Notify client about their team
            NotifyPlayerTeamClientRpc(clientId, teamId);
        }
        else
        {
            Debug.LogError("[MANAGER] Player prefab does not have NetworkObject component");
            Destroy(playerInstance);
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
    
    [ClientRpc]
    private void NotifyPlayerTeamClientRpc(ulong clientId, int teamId)
    {
        // Only act if we are the client this message was sent to
        if (NetworkManager.Singleton.LocalClientId != clientId)
        {
            return;
        }
        
        Debug.Log($"[MANAGER] You are on team {teamId}");
        
        // Find the local player object
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
            // Change color based on team
            Renderer renderer = localPlayerObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Apply appropriate team color
                Color teamColor = (teamId == 1) ? team1Color : team2Color;
                renderer.material.color = teamColor;
                
                Debug.Log($"[MANAGER] Player color updated for team {teamId}");
            }
        }
        else
        {
            Debug.LogWarning("[MANAGER] Could not find local player object");
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