using System.Collections;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Componente para asegurar que cada jugador tenga su propia cámara
/// independiente, elimina cualquier otra cámara en la escena.
/// </summary>
public class OneCameraPerPlayer : NetworkBehaviour
{
    [SerializeField] private GameObject cameraPrefab;
    [SerializeField] private float cameraSpawnDelay = 0.2f;
    
    // Variable para almacenar la instancia de la cámara creada
    private GameObject myCameraInstance;
    
    // Identificador único para esta cámara
    private string cameraId;
    
    public override void OnNetworkSpawn()
    {
        if (!IsLocalPlayer) return;
        
        // Generamos un ID único para esta cámara basado en datos del sistema y tiempo
        cameraId = $"PlayerCamera_{OwnerClientId}_{System.DateTime.Now.Ticks}";
        
        // Esperamos un poco antes de inicializar la cámara para evitar conflictos
        StartCoroutine(InitializeCameraDelayed());
    }
    
    private IEnumerator InitializeCameraDelayed()
    {
        // Esperamos para dar tiempo a que la escena se estabilice
        yield return new WaitForSeconds(cameraSpawnDelay);
        
        // Destruimos todas las cámaras que existen en la escena
        DestroyAllExistingCameras();
        
        // Creamos nuestra propia cámara
        CreatePlayerCamera();
    }
    
    private void DestroyAllExistingCameras()
    {
        // Buscar todas las cámaras en la escena
        Camera[] allCameras = FindObjectsOfType<Camera>();
        Debug.Log($"[CAMERA_FIXER] Encontradas {allCameras.Length} cámaras en la escena, eliminándolas...");
        
        foreach (Camera cam in allCameras)
        {
            // Si la cámara pertenece a un objeto con NetworkObject, la desactivamos
            // en lugar de destruirla para evitar problemas de red
            NetworkObject netObj = cam.GetComponentInParent<NetworkObject>();
            if (netObj != null)
            {
                Debug.Log($"[CAMERA_FIXER] Desactivando cámara de red: {cam.gameObject.name}");
                cam.gameObject.SetActive(false);
            }
            else
            {
                Debug.Log($"[CAMERA_FIXER] Destruyendo cámara: {cam.gameObject.name}");
                Destroy(cam.gameObject);
            }
        }
    }
    
    private void CreatePlayerCamera()
    {
        if (cameraPrefab == null)
        {
            Debug.LogError("[CAMERA_FIXER] Error: No hay prefab de cámara asignado");
            return;
        }
        
        // Crear una nueva cámara completamente independiente
        myCameraInstance = Instantiate(cameraPrefab);
        myCameraInstance.name = cameraId;
        
        Debug.Log($"[CAMERA_FIXER] Nueva cámara creada: {cameraId}");
        
        // Eliminar cualquier componente de red que pudiera causar sincronización
        CleanupNetworkComponents(myCameraInstance);
        
        // Configurar la cámara para seguir a este jugador
        ConfigureCamera(myCameraInstance);
        
        // No destruir al cambiar de escena
        DontDestroyOnLoad(myCameraInstance);
    }
    
    private void CleanupNetworkComponents(GameObject obj)
    {
        // Eliminar cualquier componente NetworkObject
        NetworkObject netObj = obj.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            Debug.Log($"[CAMERA_FIXER] Eliminando NetworkObject de la cámara {cameraId}");
            Destroy(netObj);
        }
        
        // Eliminar cualquier NetworkBehaviour
        NetworkBehaviour[] netBehaviours = obj.GetComponents<NetworkBehaviour>();
        foreach (NetworkBehaviour nb in netBehaviours)
        {
            Debug.Log($"[CAMERA_FIXER] Eliminando NetworkBehaviour de la cámara {cameraId}");
            Destroy(nb);
        }
        
        // Aplicar lo mismo a los hijos
        foreach (Transform child in obj.transform)
        {
            CleanupNetworkComponents(child.gameObject);
        }
    }
    
    private void ConfigureCamera(GameObject cameraObj)
    {
        // Configurar la cámara como la principal
        Camera cam = cameraObj.GetComponent<Camera>();
        if (cam != null)
        {
            cam.tag = "MainCamera";
        }
        
        // Si tiene un componente MOBACamera, configurarlo
        MOBACamera mobaCam = cameraObj.GetComponent<MOBACamera>();
        if (mobaCam != null)
        {
            // Configurar el objetivo como este jugador
            mobaCam.SetTarget(this.transform);
            
            // Centrar la cámara en el jugador
            mobaCam.CenterOnPlayer();
            
            Debug.Log($"[CAMERA_FIXER] Cámara {cameraId} configurada para seguir al jugador local");
        }
    }
    
    public override void OnNetworkDespawn()
    {
        if (!IsLocalPlayer) return;
        
        // Limpiar cuando el jugador se desconecta
        if (myCameraInstance != null)
        {
            Debug.Log($"[CAMERA_FIXER] Destruyendo cámara {cameraId} al desconectar");
            Destroy(myCameraInstance);
            myCameraInstance = null;
        }
    }
    
    public override void OnDestroy()
    {
        base.OnDestroy();
        
        // Limpieza adicional por si acaso
        if (myCameraInstance != null)
        {
            Destroy(myCameraInstance);
        }
    }
}