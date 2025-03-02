using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Controlador de cámara completamente independiente para cada jugador local.
/// Este script debe agregarse a un objeto vacío en la escena.
/// </summary>
public class LocalCameraController : MonoBehaviour
{
    [SerializeField] private GameObject cameraPrefab;
    
    // Referencia a la cámara creada
    private GameObject cameraInstance;
    
    // Referencia al jugador local
    private PlayerNetwork localPlayer;
    
    // Bandera para evitar múltiples inicializaciones
    private bool isInitialized = false;
    
    // Singleton para acceso fácil
    public static LocalCameraController Instance { get; private set; }
    
    private void Awake()
    {
        // Configuración singleton
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    private void Start()
    {
        // Registrarse para eventos de red (en Start para asegurar que NetworkManager.Singleton exista)
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientStarted += HandleClientStarted;
            NetworkManager.Singleton.OnClientStopped += HandleClientStopped;
            
            // Si ya estamos conectados, empezamos a buscar al jugador
            if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsHost)
            {
                HandleClientStarted();
            }
        }
        else
        {
            Debug.LogWarning("[LOCAL_CAMERA] NetworkManager.Singleton es nulo. Verificando periódicamente...");
            InvokeRepeating(nameof(TryConnectToNetworkManager), 0.5f, 0.5f);
        }
    }
    
    private void TryConnectToNetworkManager()
    {
        if (NetworkManager.Singleton != null)
        {
            Debug.Log("[LOCAL_CAMERA] NetworkManager.Singleton encontrado. Registrando eventos...");
            
            NetworkManager.Singleton.OnClientStarted += HandleClientStarted;
            NetworkManager.Singleton.OnClientStopped += HandleClientStopped;
            
            if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsHost)
            {
                HandleClientStarted();
            }
            
            CancelInvoke(nameof(TryConnectToNetworkManager));
        }
    }
    
    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientStarted -= HandleClientStarted;
            NetworkManager.Singleton.OnClientStopped -= HandleClientStopped;
        }
    }
    
    // Cuando el cliente se inicia, empezamos a buscar al jugador local
    private void HandleClientStarted()
    {
        Debug.Log("[LOCAL_CAMERA] Cliente iniciado - Buscando jugador local...");
        
        // Desactivamos esta funcionalidad en MOBACamera
        DisableAllMOBACameras();
        
        // Empezamos a buscar al jugador local
        InvokeRepeating(nameof(TryFindLocalPlayer), 0.5f, 0.5f);
    }
    
    // Cuando el cliente se detiene, limpiamos todo
    private void HandleClientStopped(bool _)
    {
        Debug.Log("[LOCAL_CAMERA] Cliente detenido - Limpiando recursos");
        CleanupCamera();
        CancelInvoke(nameof(TryFindLocalPlayer));
        isInitialized = false;
    }
    
    // Buscamos al jugador local periódicamente hasta encontrarlo
    private void TryFindLocalPlayer()
    {
        if (isInitialized)
        {
            CancelInvoke(nameof(TryFindLocalPlayer));
            return;
        }
        
        PlayerNetwork[] players = FindObjectsOfType<PlayerNetwork>();
        foreach (PlayerNetwork player in players)
        {
            if (player.IsLocalPlayer)
            {
                Debug.Log($"[LOCAL_CAMERA] Jugador local encontrado con ID: {player.OwnerClientId}");
                localPlayer = player;
                InitializeCamera();
                CancelInvoke(nameof(TryFindLocalPlayer));
                break;
            }
        }
    }
    
    // Inicializa la cámara para el jugador local
    private void InitializeCamera()
    {
        if (isInitialized || localPlayer == null) return;
        
        Debug.Log("[LOCAL_CAMERA] Inicializando cámara...");
        
        // Desactivar todas las cámaras existentes
        DisableAllCameras();
        
        // Crear nueva cámara
        if (cameraPrefab != null)
        {
            cameraInstance = Instantiate(cameraPrefab);
            cameraInstance.name = $"LocalCamera_Player_{localPlayer.OwnerClientId}";
            
            // Eliminar cualquier componente de red
            NetworkObject netObj = cameraInstance.GetComponent<NetworkObject>();
            if (netObj != null) Destroy(netObj);
            
            // Configurar componentes
            Camera cam = cameraInstance.GetComponent<Camera>();
            if (cam != null) cam.tag = "MainCamera";
            
            // Desactivar MOBACamera si existe y usar nuestro propio controlador
            MOBACamera mobaCam = cameraInstance.GetComponent<MOBACamera>();
            if (mobaCam != null)
            {
                // Establecer el objetivo
                mobaCam.SetTarget(localPlayer.transform);
                
                // Impedir que busque automáticamente
                SetAutoFindTarget(mobaCam, false);
            }
            
            // No destruir al cargar nuevas escenas
            DontDestroyOnLoad(cameraInstance);
            
            Debug.Log($"[LOCAL_CAMERA] Cámara inicializada para jugador {localPlayer.OwnerClientId}");
            isInitialized = true;
        }
        else
        {
            Debug.LogError("[LOCAL_CAMERA] ¡Error! No hay prefab de cámara asignado");
        }
    }
    
    // Limpia la cámara y recursos relacionados
    private void CleanupCamera()
    {
        if (cameraInstance != null)
        {
            Destroy(cameraInstance);
            cameraInstance = null;
        }
        
        localPlayer = null;
        isInitialized = false;
    }
    
    // Desactivar todas las cámaras existentes
    private void DisableAllCameras()
    {
        Camera[] cameras = FindObjectsOfType<Camera>();
        
        foreach (Camera cam in cameras)
        {
            // No desactivar nuestra propia cámara
            if (cameraInstance != null && cam.gameObject == cameraInstance)
                continue;
            
            Debug.Log($"[LOCAL_CAMERA] Desactivando cámara: {cam.gameObject.name}");
            cam.gameObject.SetActive(false);
        }
    }
    
    // Desactivar funcionalidad automática en todas las MOBACamera
    private void DisableAllMOBACameras()
    {
        MOBACamera[] mobaCameras = FindObjectsOfType<MOBACamera>();
        
        foreach (MOBACamera cam in mobaCameras)
        {
            SetAutoFindTarget(cam, false);
        }
    }
    
    // Método para cambiar la bandera findTargetAutomatically mediante Reflection
    // ya que es un campo privado en MOBACamera
    private void SetAutoFindTarget(MOBACamera camera, bool value)
    {
        var field = typeof(MOBACamera).GetField("findTargetAutomatically", 
                                               System.Reflection.BindingFlags.Instance | 
                                               System.Reflection.BindingFlags.NonPublic);
        
        if (field != null)
        {
            field.SetValue(camera, value);
        }
        else
        {
            Debug.LogWarning("[LOCAL_CAMERA] No se pudo encontrar el campo 'findTargetAutomatically' en MOBACamera");
        }
    }
    
    // Método público para reiniciar la cámara si es necesario
    public void ResetCamera()
    {
        CleanupCamera();
        TryFindLocalPlayer();
    }
}