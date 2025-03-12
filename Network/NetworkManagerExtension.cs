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
            // Ocultar panel de conexión
            if (connectionPanel != null)
            {
                connectionPanel.SetActive(false);
            }
            
            // Primero iniciamos el host en modo red pero sin spawneo de jugador
            networkManager.StartHost();
            
            // IMPORTANTE: ahora debemos desactivar la spawneada automática del jugador
            MOBAGameManager gameManager = FindObjectOfType<MOBAGameManager>();
            if (gameManager != null)
            {
                // Informar al GameManager que estamos en fase de selección
                gameManager.SetHeroSelectionMode(true);
            }
            
            // Mostrar la UI de selección de héroes
            if (heroSelectionUI != null)
            {
                heroSelectionUI.gameObject.SetActive(true);
                heroSelectionUI.Show();
                
                Debug.Log("Host iniciado con fase de selección de héroes");
            }
            else
            {
                Debug.LogError("¡heroSelectionUI no está asignado! No se puede mostrar la UI de selección.");
            }
        }
    }
    
    // Call this from your UI instead of directly calling NetworkManager.StartClient()
    public void StartClientWithHeroSelection()
    {
        if (networkManager != null)
        {
            // Ocultar panel de conexión
            if (connectionPanel != null)
            {
                connectionPanel.SetActive(false);
            }
            
            // Start client as normal
            networkManager.StartClient();
            
            Debug.Log("Client started with hero selection phase");
        }
    }
}