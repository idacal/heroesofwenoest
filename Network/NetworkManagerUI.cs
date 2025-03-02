using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class NetworkManagerUI : MonoBehaviour
{
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private Button serverButton;
    [SerializeField] private GameObject connectionButtonsPanel;

    // Bandera para rastrear si ya nos hemos suscrito a eventos
    private bool subscribedToEvents = false;
    
    private void Awake()
    {
        // Verificar que todas las referencias estén correctamente asignadas
        bool referencesOk = true;
        
        if (hostButton == null) {
            Debug.LogError("NetworkManagerUI: hostButton no está asignado en el inspector");
            referencesOk = false;
        }
        
        if (clientButton == null) {
            Debug.LogError("NetworkManagerUI: clientButton no está asignado en el inspector");
            referencesOk = false;
        }
        
        if (serverButton == null) {
            Debug.LogError("NetworkManagerUI: serverButton no está asignado en el inspector");
            referencesOk = false;
        }
        
        if (!referencesOk) return;
        
        // Configurar listeners para los botones
        SetupButtonListeners();
    }

    private void Start()
    {
        // Intentar suscribirse a eventos en Start, que es después de Awake
        TrySubscribeToEvents();
    }
    
    private void SetupButtonListeners()
    {
        hostButton.onClick.AddListener(() => {
            // Encontrar el NetworkManager en tiempo de ejecución
            NetworkManager networkManager = FindObjectOfType<NetworkManager>();
            if (networkManager != null)
            {
                networkManager.StartHost();
                HideConnectionUI();
                
                // Intentar suscribirse si aún no lo hemos hecho
                if (!subscribedToEvents)
                {
                    TrySubscribeToEvents();
                }
            }
            else
            {
                Debug.LogError("No se pudo encontrar NetworkManager en la escena");
            }
        });
        
        clientButton.onClick.AddListener(() => {
            NetworkManager networkManager = FindObjectOfType<NetworkManager>();
            if (networkManager != null)
            {
                networkManager.StartClient();
                HideConnectionUI();
                
                if (!subscribedToEvents)
                {
                    TrySubscribeToEvents();
                }
            }
            else
            {
                Debug.LogError("No se pudo encontrar NetworkManager en la escena");
            }
        });
        
        serverButton.onClick.AddListener(() => {
            NetworkManager networkManager = FindObjectOfType<NetworkManager>();
            if (networkManager != null)
            {
                networkManager.StartServer();
                HideConnectionUI();
                
                if (!subscribedToEvents)
                {
                    TrySubscribeToEvents();
                }
            }
            else
            {
                Debug.LogError("No se pudo encontrar NetworkManager en la escena");
            }
        });
    }
    
    private void TrySubscribeToEvents()
    {
        // Intentar encontrar NetworkManager y suscribirse a eventos
        NetworkManager networkManager = FindObjectOfType<NetworkManager>();
        if (networkManager != null)
        {
            // Desuscribirse primero para evitar suscripciones duplicadas
            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            
            // Suscribirse a eventos
            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            
            subscribedToEvents = true;
            Debug.Log("NetworkManagerUI: Suscrito a eventos de NetworkManager correctamente");
        }
        else
        {
            Debug.LogWarning("NetworkManagerUI: No se puede encontrar NetworkManager para suscribirse a eventos");
        }
    }
    
    private void OnDestroy()
    {
        // Desuscribirse de eventos al destruir
        if (subscribedToEvents)
        {
            NetworkManager networkManager = FindObjectOfType<NetworkManager>();
            if (networkManager != null)
            {
                networkManager.OnClientConnectedCallback -= OnClientConnected;
                networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
        }
    }
    
    private void OnClientConnected(ulong clientId)
    {
        NetworkManager networkManager = FindObjectOfType<NetworkManager>();
        if (networkManager != null && clientId == networkManager.LocalClientId)
        {
            HideConnectionUI();
        }
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        NetworkManager networkManager = FindObjectOfType<NetworkManager>();
        if (networkManager != null && clientId == networkManager.LocalClientId)
        {
            ShowConnectionUI();
        }
    }
    
    private void HideConnectionUI()
    {
        if (connectionButtonsPanel != null)
        {
            connectionButtonsPanel.SetActive(false);
        }
        else
        {
            if (hostButton != null) hostButton.gameObject.SetActive(false);
            if (clientButton != null) clientButton.gameObject.SetActive(false);
            if (serverButton != null) serverButton.gameObject.SetActive(false);
        }
    }
    
    private void ShowConnectionUI()
    {
        if (connectionButtonsPanel != null)
        {
            connectionButtonsPanel.SetActive(true);
        }
        else
        {
            if (hostButton != null) hostButton.gameObject.SetActive(true);
            if (clientButton != null) clientButton.gameObject.SetActive(true);
            if (serverButton != null) serverButton.gameObject.SetActive(true);
        }
    }
}