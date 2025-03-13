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
            HideConnectionPanel();
        }
    }
    
    private void HideConnectionPanel()
    {
        if (connectionPanel != null)
        {
            Debug.Log("[NetworkManagerExtension] Hiding connection panel");
            connectionPanel.SetActive(false);
            
            // Force the connection panel to stay hidden by setting it inactive in the next frame
            StartCoroutine(EnsureConnectionPanelHidden());
        }
        else
        {
            Debug.LogWarning("[NetworkManagerExtension] Connection panel reference is null!");
            
            // Try to find connection panel by name if reference is missing
            GameObject panel = GameObject.Find("ConnectionPanel");
            if (panel != null)
            {
                Debug.Log("[NetworkManagerExtension] Found connection panel by name, hiding it");
                panel.SetActive(false);
            }
        }
    }
    
    // New method to ensure connection panel stays hidden
    private System.Collections.IEnumerator EnsureConnectionPanelHidden()
    {
        // Wait for end of frame
        yield return new WaitForEndOfFrame();
        
        // Hide the panel again
        if (connectionPanel != null && connectionPanel.activeSelf)
        {
            Debug.Log("[NetworkManagerExtension] Connection panel was reactivated, hiding it again");
            connectionPanel.SetActive(false);
        }
        
        // Also try to find any panels by tag
        GameObject[] connectionPanels = GameObject.FindGameObjectsWithTag("ConnectionPanel");
        foreach (var panel in connectionPanels)
        {
            if (panel.activeSelf)
            {
                Debug.Log($"[NetworkManagerExtension] Found active connection panel with tag: {panel.name}, hiding it");
                panel.SetActive(false);
            }
        }
    }
    
    // Call this from your UI instead of directly calling NetworkManager.StartHost()
    public void StartHostWithHeroSelection()
{
    if (networkManager != null)
    {
        Debug.Log("[NetworkManagerExtension] StartHostWithHeroSelection called");
        
        // CRITICAL: Hide connection panel FIRST
        HideConnectionPanel();
        
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
        
        // Make sure connection panel is hidden again
        HideConnectionPanel();
        
        // Ensure hero selection UI is displayed
        if (heroSelectionUI != null)
        {
            Debug.Log("[NetworkManagerExtension] Showing hero selection UI");
            heroSelectionUI.gameObject.SetActive(true);
            heroSelectionUI.Show();
            
            // Get HeroSelectionManager and make sure it updates the UI
            HeroSelectionManager selectionManager = FindObjectOfType<HeroSelectionManager>();
            if (selectionManager != null)
            {
                // Tell the manager to update the UI with available heroes
                selectionManager.UpdateHeroSelectionUI();
            }
            else
            {
                Debug.LogError("[NetworkManagerExtension] HeroSelectionManager not found!");
            }
            
            // Set sorting order higher than connection panel
            Canvas heroSelectionCanvas = heroSelectionUI.GetComponentInParent<Canvas>();
            if (heroSelectionCanvas != null)
            {
                heroSelectionCanvas.sortingOrder = 100; // Higher value = in front
                Debug.Log($"[NetworkManagerExtension] Set hero selection canvas sorting order to {heroSelectionCanvas.sortingOrder}");
            }
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
            
            // Hide connection panel FIRST
            HideConnectionPanel();
            
            // Start client as normal
            networkManager.StartClient();
            
            // Make sure connection panel is hidden again
            HideConnectionPanel();
            
            Debug.Log("[NetworkManagerExtension] Client started with hero selection phase");
        }
    }
}