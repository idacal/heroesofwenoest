using Unity.Netcode;
using UnityEngine;

public class PlayerNetwork : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float movementSpeed = 7f;
    [SerializeField] private float rotationSpeed = 10f;
    
    [Header("Camera")]
    [SerializeField] private GameObject mobaCameraPrefab;
    
    // Variables de red para sincronizar
    private NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
        
    private NetworkVariable<Quaternion> networkRotation = new NetworkVariable<Quaternion>(
        Quaternion.identity,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    
    // Referencias de componentes - estrictamente locales, no sincronizadas
    private Camera playerCamera;
    private MOBACamera mobaCameraComponent;
    
    // Eliminamos la variable estática que causaba el problema
    // private static bool localCameraCreated = false;
    
    // Nueva variable para controlar que este jugador específico ya ha creado su cámara
    private bool hasCameraBeenCreated = false;
    
    public override void OnNetworkSpawn()
    {
        // Inicializar posición y rotación en la red al spawnearse
        if (IsServer)
        {
            networkPosition.Value = transform.position;
            networkRotation.Value = transform.rotation;
        }
        
        if (IsOwner && IsLocalPlayer)
        {
            Debug.Log($"[PLAYER] Configurando jugador local con ID: {OwnerClientId}");
            
            // Verificar si ESTE jugador específico ya tiene cámara
            if (!hasCameraBeenCreated)
            {
                SetupLocalCamera();
                hasCameraBeenCreated = true;
            }
        }
    }
    
    private void SetupLocalCamera()
    {
        // Primero, destruimos cualquier cámara existente que pertenezca a este jugador
        // (esto es una precaución adicional)
        MOBACamera[] existingCameras = FindObjectsOfType<MOBACamera>();
        foreach (MOBACamera cam in existingCameras)
        {
            // Si la cámara ya está siguiendo a este jugador, la destruimos
            if (cam.GetTarget() == transform)
            {
                Destroy(cam.gameObject);
                Debug.Log("[CAMERA] Destruyendo cámara duplicada");
            }
        }
        
        // Crear la cámara solo para el jugador local
        if (mobaCameraPrefab != null)
        {
            GameObject cameraObject = Instantiate(mobaCameraPrefab);
            if (cameraObject == null)
            {
                Debug.LogError("No se pudo instanciar el prefab de la cámara");
                return;
            }
            
            // Asegurar que este objeto no se sincronice por red
            NetworkObject networkObject = cameraObject.GetComponent<NetworkObject>();
            if (networkObject != null)
            {
                // Si por alguna razón tiene NetworkObject, lo desactivamos
                Destroy(networkObject);
                Debug.LogWarning("Se eliminó el componente NetworkObject de la cámara para evitar sincronización");
            }
            
            playerCamera = cameraObject.GetComponent<Camera>();
            mobaCameraComponent = cameraObject.GetComponent<MOBACamera>();
            
            if (mobaCameraComponent != null)
            {
                // Configurar la cámara para conocer al jugador, pero NO seguirlo automáticamente
                mobaCameraComponent.SetTarget(transform);
                
                // Centrar la cámara en el jugador inicialmente
                mobaCameraComponent.CenterOnPlayer();
                
                // Identificar claramente la cámara para depuración
                cameraObject.name = $"MOBACamera_Player_{OwnerClientId}";
                
                Debug.Log($"[CAMERA] Cámara MOBA configurada correctamente para el jugador local ID: {OwnerClientId}");
            }
            else
            {
                Debug.LogError("El prefab de la cámara no tiene el componente MOBACamera");
            }
        }
        else
        {
            Debug.LogError("mobaCameraPrefab no está asignado en el inspector");
        }
    }
    
    private void Update()
    {
        if (IsOwner)
        {
            // Control de movimiento para el propietario del objeto
            HandleMovement();
        }
        else
        {
            // Interpolación para otros clientes
            transform.position = Vector3.Lerp(transform.position, networkPosition.Value, Time.deltaTime * 10f);
            transform.rotation = Quaternion.Lerp(transform.rotation, networkRotation.Value, Time.deltaTime * 10f);
        }
    }
    
    private void HandleMovement()
    {
        // Movimiento basado en entrada WASD o flechas
        float horizontalInput = Input.GetAxis("Horizontal");
        float verticalInput = Input.GetAxis("Vertical");
        
        if (horizontalInput != 0 || verticalInput != 0)
        {
            // Crear vector de movimiento
            Vector3 movementDirection = new Vector3(horizontalInput, 0f, verticalInput).normalized;
            
            // Convertir el movimiento según la perspectiva de la cámara isométrica
            // (Buscar la cámara local si no la tenemos ya)
            if (playerCamera == null && Camera.main != null)
            {
                playerCamera = Camera.main;
            }
            
            if (playerCamera != null)
            {
                Vector3 cameraForward = playerCamera.transform.forward;
                Vector3 cameraRight = playerCamera.transform.right;
                
                // Aplanar los vectores a XZ
                cameraForward.y = 0;
                cameraRight.y = 0;
                cameraForward.Normalize();
                cameraRight.Normalize();
                
                // Recalcular la dirección del movimiento relativa a la cámara
                movementDirection = cameraRight * horizontalInput + cameraForward * verticalInput;
                movementDirection.Normalize();
            }
            
            // Aplicar velocidad y tiempo delta
            Vector3 movement = movementDirection * movementSpeed * Time.deltaTime;
            
            // Mover el personaje
            transform.Translate(movement, Space.World);
            
            // Rotar el personaje hacia la dirección del movimiento
            if (movementDirection != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(movementDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
            
            // Enviar la posición y rotación actualizadas al servidor
            UpdateTransformServerRpc(transform.position, transform.rotation);
        }
    }
    
    [ServerRpc]
    private void UpdateTransformServerRpc(Vector3 newPosition, Quaternion newRotation)
    {
        // Actualizar variables de red (esto se ejecuta solo en el servidor)
        networkPosition.Value = newPosition;
        networkRotation.Value = newRotation;
    }
    
    // Método para reiniciar la bandera cuando se destruye el jugador
    private void OnDestroy()
    {
        // Ya no necesitamos manipular una variable estática
        // Solo asegúrate de que la cámara también se destruya si pertenece a este jugador
        if (mobaCameraComponent != null && IsOwner)
        {
            Destroy(mobaCameraComponent.gameObject);
        }
    }
}