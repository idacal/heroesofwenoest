using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public class PlayerNetwork : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float movementSpeed = 7f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float clickMovementSpeed = 7f;
    [SerializeField] private float stoppingDistance = 0.2f;
    [SerializeField] private LayerMask groundLayer; // Capa para detectar el suelo con raycasts
    
    [Header("References")]
    [SerializeField] private GameObject cameraPrefab; // Referencia al prefab MOBACamera
    [SerializeField] private GameObject clickIndicatorPrefab; // Prefab para el indicador de clic
    
    // Variables de red para sincronizar
    private NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
        
    private NetworkVariable<Quaternion> networkRotation = new NetworkVariable<Quaternion>(
        Quaternion.identity,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    
    // Referencias locales - NO se sincronizan por red
    private GameObject localCameraObject;
    private MOBACamera mobaCameraComponent;
    private Camera playerCamera;
    private string playerUniqueId;
    
    // Click Indicator
    private GameObject localClickIndicator;
    
    // Variables para movimiento por clic
    private Vector3 targetPosition;
    private bool isMovingToTarget = false;
    private Vector3 lastPosition;
    private float stuckTimer = 0f;
    private float stuckThreshold = 0.5f; // Tiempo antes de considerar que está atascado
    
    private void Awake()
    {
        // Generar ID único para este jugador para propósitos de depuración
        playerUniqueId = System.Guid.NewGuid().ToString().Substring(0, 8);
    }
    
    // Override para el spawn en red
    public override void OnNetworkSpawn()
    {
        Debug.Log($"[PLAYER_{playerUniqueId}] OnNetworkSpawn - IsOwner: {IsOwner}, IsLocalPlayer: {IsLocalPlayer}, OwnerClientId: {OwnerClientId}");
        
        // Inicializar posición y rotación en la red
        if (IsServer)
        {
            networkPosition.Value = transform.position;
            networkRotation.Value = transform.rotation;
        }
        
        // Solo el cliente local debe crear y gestionar su propia cámara
        if (IsLocalPlayer)
        {
            Debug.Log($"[PLAYER_{playerUniqueId}] Configurando jugador local con ClientId: {OwnerClientId}");
            
            // Inicializar posición objetivo con posición actual
            targetPosition = transform.position;
            lastPosition = transform.position;
            
            // Pequeño delay para asegurar que todo está inicializado correctamente
            Invoke("CreateLocalCamera", 0.2f);
            
            // Crear Indicator si está configurado
            if (clickIndicatorPrefab != null)
            {
                CreateLocalClickIndicator();
            }
        }
    }
    
    private void CreateLocalClickIndicator()
    {
        if (localClickIndicator != null)
        {
            Destroy(localClickIndicator);
        }
        
        localClickIndicator = Instantiate(clickIndicatorPrefab);
        localClickIndicator.name = $"ClickIndicator_{playerUniqueId}";
        
        // Asegurar que no tenga componentes de red
        NetworkObject indicatorNetObj = localClickIndicator.GetComponent<NetworkObject>();
        if (indicatorNetObj != null)
        {
            Destroy(indicatorNetObj);
        }
        
        // No destruir al cambiar de escena
        DontDestroyOnLoad(localClickIndicator);
        
        // Desactivar inicialmente
        localClickIndicator.SetActive(false);
    }
    
    private void CreateLocalCamera()
    {
        Debug.Log($"[PLAYER_{playerUniqueId}] Iniciando creación de cámara local");
        
        // Verificar si tenemos un prefab de cámara asignado
        if (cameraPrefab == null)
        {
            Debug.LogError($"[PLAYER_{playerUniqueId}] ERROR: No hay prefab de cámara asignado!");
            return;
        }
        
        // Primero, destruimos cualquier cámara existente que pertenezca a este jugador
        MOBACamera[] existingCameras = FindObjectsOfType<MOBACamera>();
        foreach (MOBACamera cam in existingCameras)
        {
            if (cam.GetTarget() == transform)
            {
                Debug.Log($"[PLAYER_{playerUniqueId}] Destruyendo cámara duplicada para este jugador");
                Destroy(cam.gameObject);
            }
        }
        
        // Crear nueva cámara completamente independiente
        localCameraObject = Instantiate(cameraPrefab);
        if (localCameraObject == null)
        {
            Debug.LogError($"[PLAYER_{playerUniqueId}] ERROR: No se pudo instanciar el prefab de cámara!");
            return;
        }
        
        // Asignar nombre único para depuración
        localCameraObject.name = $"PlayerCamera_{OwnerClientId}_{playerUniqueId}";
        
        // Obtener componentes de cámara
        mobaCameraComponent = localCameraObject.GetComponent<MOBACamera>();
        playerCamera = localCameraObject.GetComponent<Camera>();
        
        if (mobaCameraComponent == null || playerCamera == null)
        {
            Debug.LogError($"[PLAYER_{playerUniqueId}] ERROR: El prefab de cámara no tiene los componentes necesarios!");
            Destroy(localCameraObject);
            return;
        }
        
        // Configurar la cámara
        mobaCameraComponent.SetTarget(transform);
        
        // Asegurar que no hay componentes de red en la cámara
        NetworkObject cameraNetObj = localCameraObject.GetComponent<NetworkObject>();
        if (cameraNetObj != null)
        {
            Debug.LogWarning($"[PLAYER_{playerUniqueId}] Eliminando NetworkObject de la cámara para evitar sincronización!");
            Destroy(cameraNetObj);
        }
        
        // Evitar que Unity destruya la cámara al cambiar de escena
        DontDestroyOnLoad(localCameraObject);
        
        Debug.Log($"[PLAYER_{playerUniqueId}] Cámara creada exitosamente: {localCameraObject.name}");
    }
    
    private void Update()
    {
        if (IsLocalPlayer)
        {
            // Procesar input de movimiento solo para el jugador local
            HandleKeyboardMovement();
            HandleMouseMovement();
            
            // Manejar movimiento hacia posición objetivo si estamos en modo click-to-move
            if (isMovingToTarget)
            {
                MoveToTargetPosition();
            }
        }
        else
        {
            // Para otros jugadores, interpolar suavemente posición y rotación
            transform.position = Vector3.Lerp(transform.position, networkPosition.Value, Time.deltaTime * 10f);
            transform.rotation = Quaternion.Lerp(transform.rotation, networkRotation.Value, Time.deltaTime * 10f);
        }
    }
    
    private void HandleKeyboardMovement()
    {
        // Si estamos moviendo con teclado, cancelar cualquier movimiento por clic
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        
        if (horizontal != 0 || vertical != 0)
        {
            // Cancelar movimiento por clic si estamos usando teclado
            isMovingToTarget = false;
            
            // Vector normalizado para movimiento
            Vector3 moveDirection = new Vector3(horizontal, 0f, vertical).normalized;
            
            // Ajustar dirección según orientación de la cámara
            if (playerCamera != null)
            {
                // Obtener vectores de la cámara
                Vector3 camForward = playerCamera.transform.forward;
                Vector3 camRight = playerCamera.transform.right;
                
                // Aplanar a plano XZ
                camForward.y = 0;
                camRight.y = 0;
                camForward.Normalize();
                camRight.Normalize();
                
                // Recalcular dirección relativa a la cámara
                moveDirection = camRight * horizontal + camForward * vertical;
                moveDirection.Normalize();
            }
            else if (Camera.main != null)
            {
                // Fallback a la cámara principal
                Vector3 camForward = Camera.main.transform.forward;
                Vector3 camRight = Camera.main.transform.right;
                
                camForward.y = 0;
                camRight.y = 0;
                camForward.Normalize();
                camRight.Normalize();
                
                moveDirection = camRight * horizontal + camForward * vertical;
                moveDirection.Normalize();
            }
            
            // Aplicar movimiento
            Vector3 movement = moveDirection * movementSpeed * Time.deltaTime;
            transform.Translate(movement, Space.World);
            
            // Rotar hacia la dirección del movimiento
            if (moveDirection != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
            
            // Sincronizar posición y rotación con el servidor
            UpdatePositionServerRpc(transform.position, transform.rotation);
        }
    }
    
    private void HandleMouseMovement()
    {
        // Click derecho para mover
        if (Input.GetMouseButtonDown(1))
        {
            Ray ray = playerCamera != null ? playerCamera.ScreenPointToRay(Input.mousePosition) : Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            
            // Verificar si el raycast golpea en la capa del suelo
            if (Physics.Raycast(ray, out hit, 100f, groundLayer))
            {
                // Establecer la posición objetivo y activar movimiento
                targetPosition = hit.point;
                isMovingToTarget = true;
                
                // Reiniciar timer de atasco
                stuckTimer = 0f;
                lastPosition = transform.position;
                
                // Mostrar indicador de clic si está configurado
                if (localClickIndicator != null)
                {
                    ClickIndicator indicator = localClickIndicator.GetComponent<ClickIndicator>();
                    if (indicator != null)
                    {
                        indicator.ShowAt(hit.point);
                    }
                }
                
                Debug.Log($"[PLAYER_{playerUniqueId}] Moviendo a posición: {targetPosition}");
            }
        }
    }
    
    private void MoveToTargetPosition()
    {
        // Calcular dirección hacia el objetivo
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0; // Ignorar diferencia de altura
        
        // Calcular distancia al objetivo
        float distance = direction.magnitude;
        
        // Si estamos lo suficientemente cerca del objetivo, detenerse
        if (distance <= stoppingDistance)
        {
            isMovingToTarget = false;
            return;
        }
        
        // Normalizar dirección
        direction.Normalize();
        
        // Rotar hacia la dirección objetivo
        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        
        // Mover hacia el objetivo
        Vector3 movement = direction * clickMovementSpeed * Time.deltaTime;
        transform.Translate(movement, Space.World);
        
        // Sincronizar con el servidor
        UpdatePositionServerRpc(transform.position, transform.rotation);
        
        // Detectar si estamos atascados
        DetectIfStuck();
    }
    
    private void DetectIfStuck()
    {
        // Si no nos hemos movido significativamente en un tiempo, cancelar movimiento
        float movedDistance = Vector3.Distance(transform.position, lastPosition);
        if (movedDistance < 0.01f)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer > stuckThreshold)
            {
                Debug.Log($"[PLAYER_{playerUniqueId}] Detectado atasco, cancelando movimiento");
                isMovingToTarget = false;
                stuckTimer = 0f;
            }
        }
        else
        {
            // Reiniciar timer si nos movemos
            stuckTimer = 0f;
            lastPosition = transform.position;
        }
    }
    
    [ServerRpc]
    private void UpdatePositionServerRpc(Vector3 newPosition, Quaternion newRotation)
    {
        // Actualizar variables de red (solo en el servidor)
        networkPosition.Value = newPosition;
        networkRotation.Value = newRotation;
    }
    
    // Importante: Detener sincronización al desconectar
    public override void OnNetworkDespawn()
    {
        Debug.Log($"[PLAYER_{playerUniqueId}] OnNetworkDespawn - IsLocalPlayer: {IsLocalPlayer}");
        
        if (IsLocalPlayer)
        {
            if (localCameraObject != null)
            {
                Debug.Log($"[PLAYER_{playerUniqueId}] Destruyendo cámara al despawnear jugador");
                Destroy(localCameraObject);
                localCameraObject = null;
                mobaCameraComponent = null;
            }
            
            // Destruir indicador de clic
            if (localClickIndicator != null)
            {
                Debug.Log($"[PLAYER_{playerUniqueId}] Destruyendo indicador de clic al despawnear jugador");
                Destroy(localClickIndicator);
                localClickIndicator = null;
            }
        }
    }
    
    // Limpieza al destruir el objeto
    private void OnDestroy()
    {
        Debug.Log($"[PLAYER_{playerUniqueId}] OnDestroy - IsLocalPlayer: {(IsLocalPlayer ? "Sí" : "No")}");
        
        if (IsLocalPlayer)
        {
            if (localCameraObject != null)
            {
                Debug.Log($"[PLAYER_{playerUniqueId}] Destruyendo cámara del jugador");
                Destroy(localCameraObject);
            }
            
            // Destruir indicador de clic
            if (localClickIndicator != null)
            {
                Debug.Log($"[PLAYER_{playerUniqueId}] Destruyendo indicador de clic");
                Destroy(localClickIndicator);
            }
        }
    }
}