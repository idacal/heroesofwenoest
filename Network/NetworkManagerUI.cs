using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class NetworkManagerUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private Button serverButton;
    [SerializeField] private GameObject connectionPanel;
    [SerializeField] private InputField ipAddressInput;
    
    [Header("Network Manager")]
    [SerializeField] private NetworkManager networkManager;
    
    [Header("Connection Settings")]
    [SerializeField] private string defaultAddress = "127.0.0.1"; // Default to localhost
    [SerializeField] private ushort defaultPort = 7777; // Default Unity Netcode port
    
    private bool isSubscribedToEvents = false;
    
    private void Awake()
    {
        // Check for missing references
        if (!ValidateReferences())
        {
            return;
        }
        
        // Find NetworkManager if not assigned
        if (networkManager == null)
        {
            networkManager = FindObjectOfType<NetworkManager>();
            if (networkManager == null)
            {
                Debug.LogError("[UI] No NetworkManager found in scene!");
                return;
            }
        }
        
        // Initialize default IP in the input field if it's empty
        if (ipAddressInput != null && string.IsNullOrWhiteSpace(ipAddressInput.text))
        {
            ipAddressInput.text = defaultAddress;
        }
        
        // Setup button listeners
        SetupButtonListeners();
    }
    
    private bool ValidateReferences()
    {
        bool referencesValid = true;
        
        if (hostButton == null)
        {
            Debug.LogError("[UI] Host button not assigned in inspector!");
            referencesValid = false;
        }
        
        if (clientButton == null)
        {
            Debug.LogError("[UI] Client button not assigned in inspector!");
            referencesValid = false;
        }
        
        if (serverButton == null)
        {
            Debug.LogError("[UI] Server button not assigned in inspector!");
            referencesValid = false;
        }
        
        return referencesValid;
    }
    
    private void Start()
    {
        // Try to subscribe to events in Start (after Awake)
        TrySubscribeToEvents();
    }
    
    private void SetupButtonListeners()
    {
        // Host Button
        hostButton.onClick.AddListener(() => {
            Debug.Log("[UI] Starting Host");
            
            // Update transport address if provided
            UpdateTransportAddress();
            
            // Start host
            networkManager.StartHost();
            
            // Hide connection UI
            HideConnectionUI();
            
            // Subscribe to events if needed
            if (!isSubscribedToEvents)
            {
                TrySubscribeToEvents();
            }
        });
        
        // Client Button
        clientButton.onClick.AddListener(() => {
            Debug.Log("[UI] Starting Client");
            
            // Update transport address if provided
            UpdateTransportAddress();
            
            // Start client
            networkManager.StartClient();
            
            // Hide connection UI
            HideConnectionUI();
            
            // Subscribe to events if needed
            if (!isSubscribedToEvents)
            {
                TrySubscribeToEvents();
            }
        });
        
        // Server Button
        serverButton.onClick.AddListener(() => {
            Debug.Log("[UI] Starting Server");
            
            // Start server
            networkManager.StartServer();
            
            // Hide connection UI
            HideConnectionUI();
            
            // Subscribe to events if needed
            if (!isSubscribedToEvents)
            {
                TrySubscribeToEvents();
            }
        });
    }
    
    private void UpdateTransportAddress()
    {
        // Get transport
        var transport = networkManager.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            // Use input field text if available, otherwise use default address
            string address = defaultAddress;
            
            if (ipAddressInput != null && !string.IsNullOrWhiteSpace(ipAddressInput.text))
            {
                address = ipAddressInput.text;
            }
            
            // Set connection data
            transport.ConnectionData.Address = address;
            transport.ConnectionData.Port = defaultPort;
            
            Debug.Log($"[UI] Set connection address to: {address}:{defaultPort}");
        }
        else
        {
            Debug.LogError("[UI] UnityTransport component not found on the NetworkManager!");
        }
    }
    
    private void TrySubscribeToEvents()
    {
        if (networkManager != null)
        {
            // Unsubscribe first to avoid duplicate subscriptions
            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            
            // Subscribe to events
            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            
            isSubscribedToEvents = true;
            Debug.Log("[UI] Successfully subscribed to NetworkManager events");
        }
        else
        {
            Debug.LogWarning("[UI] Cannot find NetworkManager to subscribe to events");
        }
    }
    
    private void OnDestroy()
    {
        // Clean up event subscriptions
        if (isSubscribedToEvents && networkManager != null)
        {
            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
    
    private void OnClientConnected(ulong clientId)
    {
        if (networkManager != null && clientId == networkManager.LocalClientId)
        {
            HideConnectionUI();
            Debug.Log($"[UI] Connected as client ID: {clientId}");
        }
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        if (networkManager != null && clientId == networkManager.LocalClientId)
        {
            ShowConnectionUI();
            Debug.Log($"[UI] Disconnected client ID: {clientId}");
        }
    }
    
    private void HideConnectionUI()
    {
        if (connectionPanel != null)
        {
            connectionPanel.SetActive(false);
        }
        else
        {
            // Fall back to hiding individual buttons if no panel
            if (hostButton != null) hostButton.gameObject.SetActive(false);
            if (clientButton != null) clientButton.gameObject.SetActive(false);
            if (serverButton != null) serverButton.gameObject.SetActive(false);
            if (ipAddressInput != null) ipAddressInput.gameObject.SetActive(false);
        }
    }
    
    private void ShowConnectionUI()
    {
        if (connectionPanel != null)
        {
            connectionPanel.SetActive(true);
        }
        else
        {
            // Fall back to showing individual buttons if no panel
            if (hostButton != null) hostButton.gameObject.SetActive(true);
            if (clientButton != null) clientButton.gameObject.SetActive(true);
            if (serverButton != null) serverButton.gameObject.SetActive(true);
            if (ipAddressInput != null) ipAddressInput.gameObject.SetActive(true);
        }
    }
}