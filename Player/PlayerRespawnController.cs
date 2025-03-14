using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

public class PlayerRespawnController : NetworkBehaviour
{
    [Header("Respawn Configuration")]
    [SerializeField] private float fallThreshold = -10f; // Height at which player is considered fallen
    [SerializeField] private float respawnDelay = 1.5f; // Time before respawning
    [SerializeField] private float invulnerabilityTime = 2.0f; // Time of invulnerability after respawn
    
    [Header("Spawn References")]
    [SerializeField] private Transform[] team1SpawnPoints; // Team 1 spawn points
    [SerializeField] private Transform[] team2SpawnPoints; // Team 2 spawn points
    
    [Header("Effects")]
    [SerializeField] private GameObject deathEffectPrefab; // Effect shown when player dies
    [SerializeField] private GameObject respawnEffectPrefab; // Effect shown when player respawns
    [SerializeField] private Material ghostMaterial; // Semi-transparent material for invulnerability
    
    [Header("Visibility Checks")]
    [SerializeField] private bool autoFixVisibility = true; // Auto-fix visibility issues
    
    // Network variables
    private NetworkVariable<bool> isRespawning = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    private NetworkVariable<int> playerTeam = new NetworkVariable<int>(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Component references
    private PlayerNetwork playerNetwork;
    private PlayerAbilityManager abilityManager;
    private PlayerStats playerStats;
    private Collider playerCollider;
    private Renderer[] playerRenderers;
    private Material[] originalMaterials;
    
    // State tracking
    private Vector3 lastValidPosition;
    private bool isInvulnerable = false;
    private int fallCount = 0;
    private bool isDeathRespawn = false;
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Get component references
        playerNetwork = GetComponent<PlayerNetwork>();
        abilityManager = GetComponent<PlayerAbilityManager>();
        playerStats = GetComponent<PlayerStats>();
        playerCollider = GetComponent<Collider>();
        
        // Get all renderers, including inactive ones
        playerRenderers = GetComponentsInChildren<Renderer>(true);
        
        // Save original materials
        if (playerRenderers.Length > 0)
        {
            originalMaterials = new Material[playerRenderers.Length];
            for (int i = 0; i < playerRenderers.Length; i++)
            {
                if (playerRenderers[i] != null && playerRenderers[i].material != null)
                {
                    originalMaterials[i] = playerRenderers[i].material;
                }
            }
        }
        
        // Subscribe to respawn state changes
        isRespawning.OnValueChanged += OnRespawningChanged;
        
        // Initialize last valid position
        lastValidPosition = transform.position;
        
        Debug.Log($"[RespawnController] Initialized for player {OwnerClientId}, Team {playerTeam.Value}");
        
        // Force visibility immediately
        ForceVisibility();
        
        // Start periodic visibility checks
        if (autoFixVisibility)
        {
            StartCoroutine(PeriodicVisibilityChecks());
        }
    }
    
    private IEnumerator PeriodicVisibilityChecks()
    {
        // Initial checks more frequently
        for (int i = 0; i < 5; i++)
        {
            yield return new WaitForSeconds(0.5f);
            ForceVisibility();
        }
        
        // Then less frequent checks
        while (true)
        {
            yield return new WaitForSeconds(2.0f);
            
            if (IsOwner && autoFixVisibility)
            {
                CheckAndFixVisibility();
            }
        }
    }
    
    private void CheckAndFixVisibility()
    {
        // Count visible renderers
        int visibleCount = 0;
        
        // Update renderer list
        playerRenderers = GetComponentsInChildren<Renderer>(true);
        
        foreach (var renderer in playerRenderers)
        {
            if (renderer != null && renderer.enabled && renderer.isVisible)
            {
                visibleCount++;
            }
        }
        
        // If no visible renderers, force visibility
        if (visibleCount == 0 && playerRenderers.Length > 0)
        {
            Debug.LogWarning($"[RespawnController] Player {OwnerClientId} has no visible renderers. Forcing visibility.");
            ForceVisibility();
        }
    }
    
    private void Update()
    {
        // Only check for falls if not already respawning
        if (!isRespawning.Value)
        {
            // Update last valid position if above threshold
            if (transform.position.y > fallThreshold + 2.0f)
            {
                lastValidPosition = transform.position;
            }
            
            // Check if player fell below threshold
            if (transform.position.y < fallThreshold)
            {
                if (IsServer)
                {
                    // Server handles respawn directly
                    isDeathRespawn = false; // It's a fall, not a death
                    StartRespawnProcess();
                }
                else if (IsOwner)
                {
                    // Client notifies server
                    NotifyFallServerRpc();
                }
            }
        }
    }
    
    [ServerRpc]
    private void NotifyFallServerRpc()
    {
        // Verify we're not already respawning
        if (!isRespawning.Value)
        {
            isDeathRespawn = false; // Mark as fall, not death
            StartRespawnProcess();
        }
    }
    
    // Public method for PlayerStats to call when player dies
    public void ForceRespawn()
    {
        if (IsServer)
        {
            isDeathRespawn = true; // Mark as death respawn
            StartRespawnProcess();
        }
        else
        {
            ForceRespawnServerRpc();
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void ForceRespawnServerRpc()
    {
        isDeathRespawn = true; // Mark as death respawn
        StartRespawnProcess();
    }
    
    // Force visibility - called to ensure player is visible
    public void ForceVisibility()
    {
        Debug.Log($"[RespawnController] Forcing visibility for player {OwnerClientId}");
        
        try
        {
            // Ensure GameObject is active
            gameObject.SetActive(true);
            
            // Reset scale to normal
            transform.localScale = Vector3.one;
            
            // Activate all renderers
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                
                // Ensure GameObject is active
                renderer.gameObject.SetActive(true);
                
                // Enable renderer
                renderer.enabled = true;
                
                // Fix material transparency if needed
                if (renderer.material != null)
                {
                    Color color = renderer.material.color;
                    color.a = 1.0f;
                    renderer.material.color = color;
                }
            }
            
            // Ensure colliders are active
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                collider.enabled = true;
            }
            
            // Reset Rigidbody if it exists
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = false;
                rb.useGravity = true;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RespawnController] Error in ForceVisibility: {e.Message}");
        }
    }
    
    // Start the respawn process (server-side only)
    private void StartRespawnProcess()
    {
        if (!IsServer) return;
        
        string reason = isDeathRespawn ? "died" : "fell off the platform";
        Debug.Log($"[RespawnController] Player {OwnerClientId} {reason}. Starting respawn...");
        
        // Set respawning state
        isRespawning.Value = true;
        
        // Increment fall counter if it's a fall
        if (!isDeathRespawn)
        {
            fallCount++;
        }
        
        // Show death effect
        SpawnDeathEffectClientRpc(transform.position, isDeathRespawn);
        
        // Start respawn timer
        StartCoroutine(RespawnAfterDelay());
    }
    
    // Respawn after delay
    private IEnumerator RespawnAfterDelay()
    {
        if (!IsServer) yield break;
        
        // Wait for respawn delay
        yield return new WaitForSeconds(respawnDelay);
        
        try
        {
            // Get respawn position
            Vector3 spawnPosition = GetRespawnPosition();
            
            // Teleport player
            transform.position = spawnPosition;
            
            // Reset physics
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            
            // Restore health and mana
            if (playerStats != null)
            {
                playerStats.Heal(playerStats.MaxHealth);
                playerStats.RestoreMana(playerStats.MaxMana);
            }
            
            // Tell all clients to teleport this player
            TeleportPlayerClientRpc(spawnPosition);
            
            // Wait a moment for everything to settle
            yield return new WaitForSeconds(0.5f);
            
            // Exit respawn state
            isRespawning.Value = false;
            
            // Start invulnerability period
            StartInvulnerabilityClientRpc();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RespawnController] Error during respawn: {e.Message}");
            
            // Reset state even if there was an error
            isRespawning.Value = false;
        }
    }
    
    [ClientRpc]
    private void TeleportPlayerClientRpc(Vector3 position)
    {
        Debug.Log($"[RespawnController] Teleporting to {position}");
        
        // Set position
        transform.position = position;
        
        // Reset physics if we have a Rigidbody
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        
        // Ensure player is visible
        ForceVisibility();
        
        // If we're the owner, center the camera
        if (IsOwner)
        {
            // Find and center camera
            MOBACamera camera = FindPlayerCamera();
            if (camera != null)
            {
                camera.CenterOnPlayer();
            }
        }
        
        // Create respawn effect
        if (respawnEffectPrefab != null)
        {
            GameObject effect = Instantiate(respawnEffectPrefab, position, Quaternion.identity);
            Destroy(effect, 3.0f);
        }
    }
    
    [ClientRpc]
    private void SpawnDeathEffectClientRpc(Vector3 position, bool isDeathEffect)
    {
        // Create appropriate effect
        GameObject effect = null;
        
        if (isDeathEffect && deathEffectPrefab != null)
        {
            // Use death effect prefab if available
            effect = Instantiate(deathEffectPrefab, position, Quaternion.identity);
        }
        else
        {
            // Simple particle effect for falls
            ParticleSystem.MainModule main;
            
            // Create basic particle effect
            GameObject particleObj = new GameObject("FallEffect");
            particleObj.transform.position = position;
            
            ParticleSystem particles = particleObj.AddComponent<ParticleSystem>();
            main = particles.main;
            main.startSpeed = 3.0f;
            main.startSize = 0.5f;
            main.startLifetime = 1.0f;
            main.startColor = Color.white;
            
            var emission = particles.emission;
            emission.rateOverTime = 0;
            var burst = new ParticleSystem.Burst(0.0f, 30);
            emission.SetBurst(0, burst);
            
            effect = particleObj;
        }
        
        // If this is our own death, additional effects
        if (IsOwner)
        {
            // Try to shake camera
            MOBACamera camera = FindPlayerCamera();
            if (camera != null)
            {
                camera.ShakeCamera(1.0f, 0.7f);
            }
            
            // Show death message
            if (isDeathEffect)
            {
                Debug.Log("<color=red><size=20>YOU DIED!</size></color>");
            }
            else
            {
                Debug.Log("<color=orange><size=16>You fell off the platform!</size></color>");
            }
        }
        
        // Destroy effect after a few seconds
        if (effect != null)
        {
            Destroy(effect, 3.0f);
        }
    }
    
    [ClientRpc]
    private void StartInvulnerabilityClientRpc()
    {
        // Start invulnerability effect
        StartCoroutine(InvulnerabilityRoutine());
    }
    
    private IEnumerator InvulnerabilityRoutine()
    {
        // Set invulnerable state
        isInvulnerable = true;
        
        // Apply ghost material if available
        if (ghostMaterial != null && playerRenderers != null)
        {
            foreach (var renderer in playerRenderers)
            {
                if (renderer != null)
                {
                    // Save original material if needed
                    int index = System.Array.IndexOf(playerRenderers, renderer);
                    if (index >= 0 && index < originalMaterials.Length)
                    {
                        originalMaterials[index] = renderer.material;
                    }
                    
                    // Apply ghost material
                    renderer.material = ghostMaterial;
                }
            }
        }
        
        // Wait for invulnerability duration
        yield return new WaitForSeconds(invulnerabilityTime);
        
        // Restore original materials
        if (originalMaterials != null && playerRenderers != null)
        {
            for (int i = 0; i < playerRenderers.Length; i++)
            {
                if (playerRenderers[i] != null && i < originalMaterials.Length && originalMaterials[i] != null)
                {
                    playerRenderers[i].material = originalMaterials[i];
                }
            }
        }
        
        // End invulnerability
        isInvulnerable = false;
        
        Debug.Log($"[RespawnController] Invulnerability ended for player {OwnerClientId}");
    }
    
    // Find the player's camera
    private MOBACamera FindPlayerCamera()
    {
        // Look for cameras targeting this player
        MOBACamera[] cameras = FindObjectsOfType<MOBACamera>();
        foreach (var cam in cameras)
        {
            if (cam.GetTarget() == transform)
            {
                return cam;
            }
        }
        
        return null;
    }
    
    // Get the appropriate respawn position
    private Vector3 GetRespawnPosition()
    {
        // Default position in case no spawn points are available
        Vector3 defaultPosition = new Vector3(0, 5, 0);
        
        try
        {
            // If it's a death respawn (not a fall), use map center
            if (isDeathRespawn)
            {
                return defaultPosition;
            }
            
            // Get spawn points for team
            Transform[] spawnPoints = (playerTeam.Value == 1) ? team1SpawnPoints : team2SpawnPoints;
            
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                Debug.LogWarning($"[RespawnController] No spawn points for team {playerTeam.Value}");
                return defaultPosition;
            }
            
            // Get a random spawn point
            int index = UnityEngine.Random.Range(0, spawnPoints.Length);
            Transform spawnPoint = spawnPoints[index];
            
            if (spawnPoint == null)
            {
                Debug.LogWarning("[RespawnController] Selected spawn point is null");
                return defaultPosition;
            }
            
            // Add a small random offset to prevent collisions
            Vector3 offset = new Vector3(
                UnityEngine.Random.Range(-0.5f, 0.5f),
                0.5f,
                UnityEngine.Random.Range(-0.5f, 0.5f)
            );
            
            return spawnPoint.position + offset;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RespawnController] Error finding respawn position: {e.Message}");
            return defaultPosition;
        }
    }
    
    // Handle changes to respawning state
    private void OnRespawningChanged(bool oldValue, bool newValue)
    {
        if (newValue)
        {
            // Entering respawn state
            DisablePlayerControl();
        }
        else
        {
            // Exiting respawn state
            EnablePlayerControl();
        }
    }
    
    // Disable player control during respawn
    private void DisablePlayerControl()
    {
        // Disable collisions
        if (playerCollider != null)
        {
            playerCollider.enabled = false;
        }
        
        // Disable abilities
        if (abilityManager != null)
        {
            abilityManager.enabled = false;
        }
        
        // Disable movement
        if (playerNetwork != null)
        {
            playerNetwork.SetPlayerControlEnabled(false);
        }
        
        // Owner-only UI notification
        if (IsOwner)
        {
            Debug.Log("[RespawnController] You died! Respawning...");
            // Could show UI message here
        }
    }
    
    // Enable player control after respawn
    private void EnablePlayerControl()
    {
        // Force visibility
        ForceVisibility();
        
        // Enable collisions
        if (playerCollider != null)
        {
            playerCollider.enabled = true;
        }
        
        // Reset physics
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.velocity = Vector3.zero;
            
            // Only freeze rotation
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }
        
        // Enable abilities
        if (abilityManager != null)
        {
            abilityManager.enabled = true;
        }
        
        // Enable movement
        if (playerNetwork != null)
        {
            playerNetwork.SetPlayerControlEnabled(true);
            
            // Force sync position with server
            if (IsOwner)
            {
                playerNetwork.UpdatePositionServerRpc(transform.position, transform.rotation);
            }
        }
        
        // Owner-only UI notification
        if (IsOwner)
        {
            Debug.Log("[RespawnController] Control restored! You can move again.");
            // Could show UI message here
        }
    }
    
    // Public getters
    public bool IsInvulnerable() => isInvulnerable;
    public bool IsRespawning() => isRespawning.Value;
    public int GetFallCount() => fallCount;
    
    // Set team
    public void SetTeam(int team)
    {
        if (IsServer)
        {
            playerTeam.Value = team;
        }
        else
        {
            SetTeamServerRpc(team);
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void SetTeamServerRpc(int team)
    {
        playerTeam.Value = team;
    }
    
    public override void OnDestroy()
    {
        // Unsubscribe from events
        isRespawning.OnValueChanged -= OnRespawningChanged;
    }
}