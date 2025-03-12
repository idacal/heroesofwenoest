using Unity.Netcode;
using UnityEngine;

public class NetworkManagerExtension : MonoBehaviour
{
    [Header("Hero Selection")]
    [SerializeField] private HeroSelectionUI heroSelectionUI;
    [SerializeField] private GameObject connectionPanel; // Original connection UI
    
    private NetworkManager networkManager;
    
    private void Awake()
    {
        networkManager = GetComponent<NetworkManager>();
        
        if (networkManager == null)
        {
            Debug.LogError("NetworkManager component not found on the same GameObject!");
            enabled = false;
            return;
        }
    }
    
    private void Start()
    {
        // Subscribe to network events
        networkManager.OnClientConnectedCallback += OnClientConnected;
        networkManager.OnServerStarted += OnServerStarted;
        
        // Initially hide the hero selection UI
        if (heroSelectionUI != null)
        {
            heroSelectionUI.gameObject.SetActive(false);
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (networkManager != null)
        {
            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnServerStarted -= OnServerStarted;
        }
    }
    
    private void OnServerStarted()
    {
        Debug.Log("Server started - preparing hero selection phase");
        
        // Initialize the hero selection system if we're the host
        if (networkManager.IsHost && heroSelectionUI != null)
        {
            // The hero selection manager will handle showing the UI
            // when it spawns in the network
        }
    }
    
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client connected: {clientId}");
        
        // If this is our local client connecting, hide the connection panel
        if (clientId == networkManager.LocalClientId)
        {
            // Hide connection panel - the hero selection UI will be shown
            // by the HeroSelectionManager when it spawns
            if (connectionPanel != null)
            {
                // Don't hide immediately - wait a moment to ensure the HeroSelectionManager is spawned
                Invoke(nameof(HideConnectionPanel), 0.5f);
            }
        }
    }
    
    private void HideConnectionPanel()
    {
        if (connectionPanel != null)
        {
            connectionPanel.SetActive(false);
        }
    }
    
    // Call this from your UI instead of directly calling NetworkManager.StartHost()
    public void StartHostWithHeroSelection()
    {
        if (networkManager != null)
        {
            Debug.Log("[NetworkManagerExtension] StartHostWithHeroSelection called");
            
            // Hide connection panel
            if (connectionPanel != null)
            {
                connectionPanel.SetActive(false);
            }
            
            // IMPORTANT: First find and set hero selection mode BEFORE starting host
            MOBAGameManager gameManager = FindObjectOfType<MOBAGameManager>();
            if (gameManager != null)
            {
                Debug.Log("[NetworkManagerExtension] Setting hero selection mode = true");
                gameManager.SetHeroSelectionMode(true);
            }
            else
            {
                Debug.LogError("[NetworkManagerExtension] No MOBAGameManager found!");
            }
            
            // Start host after setting selection mode
            Debug.Log("[NetworkManagerExtension] Starting host...");
            networkManager.StartHost();
            
            // Show hero selection UI
            if (heroSelectionUI != null)
            {
                Debug.Log("[NetworkManagerExtension] Showing hero selection UI");
                heroSelectionUI.gameObject.SetActive(true);
                heroSelectionUI.Show();
            }
            else
            {
                Debug.LogError("[NetworkManagerExtension] heroSelectionUI is null!");
            }
        }
    }
    
    // Call this from your UI instead of directly calling NetworkManager.StartClient()
    public void StartClientWithHeroSelection()
    {
        if (networkManager != null)
        {
            Debug.Log("[NetworkManagerExtension] StartClientWithHeroSelection called");
            
            // Hide connection panel
            if (connectionPanel != null)
            {
                connectionPanel.SetActive(false);
            }
            
            // Start client as normal
            networkManager.StartClient();
            
            Debug.Log("[NetworkManagerExtension] Client started with hero selection phase");
        }
    }
}