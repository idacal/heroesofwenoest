using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public class PlayerNetwork : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float clickMovementSpeed = 7f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float stoppingDistance = 0.2f;
    [SerializeField] private LayerMask groundLayer; // Capa para detectar el suelo con raycasts
    
    [Header("Collision Settings")]
    [SerializeField] private float repulsionForce = 15f;          // Fuerza de repulsión cuando colisionan
    [SerializeField] private float collisionDamage = 10f;         // Daño base por colisión
    [SerializeField] private float collisionStunDuration = 1.0f;  // Duración del aturdimiento tras colisión
    [SerializeField] private GameObject collisionEffectPrefab;    // Efecto visual opcional
    
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
    
    // Nueva variable para sincronizar el estado de aturdimiento
    private NetworkVariable<bool> isStunned = new NetworkVariable<bool>(
        false,
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
    
    // Nuevas variables para colisión
    private Rigidbody rb;
    private bool canMove = true;
    
    // Referencia al componente de estadísticas del jugador (si existe)
    private PlayerStats playerStats;
    
    private void Awake()
    {
        // Generar ID único para este jugador para propósitos de depuración
        playerUniqueId = System.Guid.NewGuid().ToString().Substring(0, 8);
        
        // Obtener referencias
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            // Si no hay Rigidbody, lo creamos
            rb = gameObject.AddComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }
        
        // Obtener PlayerStats si existe
        playerStats = GetComponent<PlayerStats>();
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
            isStunned.Value = false;
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
        
        // Suscribirse al cambio de la variable isStunned
        isStunned.OnValueChanged += OnStunnedValueChanged;
    }
    
    // Método que se ejecuta cuando cambia el valor de isStunned
    private void OnStunnedValueChanged(bool oldValue, bool newValue)
    {
        // Actualizar el estado local de movimiento
        canMove = !newValue;
        
        // Si está aturdido, detener cualquier movimiento
        if (newValue)
        {
            isMovingToTarget = false;
            
            // Si somos el dueño, detenemos el rigidbody
            if (IsOwner && rb != null)
            {
                rb.velocity = Vector3.zero;
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
            // Solo procesar input si no estamos aturdidos
            if (canMove)
            {
                // Procesar input de movimiento solo para el jugador local
                HandleMouseMovement();
                
                // Manejar movimiento hacia posición objetivo si estamos en modo click-to-move
                if (isMovingToTarget)
                {
                    MoveToTargetPosition();
                }
            }
            
            // Cuando estemos usando físicas, actualizar la posición en el servidor
            // incluso si no estamos controlando el movimiento directamente
            if (!canMove && rb != null && rb.velocity.magnitude > 0.01f)
            {
                UpdatePositionServerRpc(transform.position, transform.rotation);
            }
        }
        else
        {
            // Para otros jugadores, interpolar suavemente posición y rotación
            // Use una interpolación más rápida después de una colisión
            float lerpSpeed = isStunned.Value ? 20f : 10f;
            transform.position = Vector3.Lerp(transform.position, networkPosition.Value, Time.deltaTime * lerpSpeed);
            transform.rotation = Quaternion.Lerp(transform.rotation, networkRotation.Value, Time.deltaTime * lerpSpeed);
        }
    }
    
    private void HandleMouseMovement()
    {
        // Si estamos aturdidos, no procesar movimiento
        if (!canMove) return;
        
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
        // Si estamos aturdidos, no procesar movimiento
        if (!canMove) return;
        
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
    
    // Método nuevo para colisiones
    private void OnCollisionEnter(Collision collision)
    {
        // Verificar si es otro jugador
        PlayerNetwork otherPlayer = collision.gameObject.GetComponent<PlayerNetwork>();
        if (otherPlayer != null)
        {
            Debug.Log($"[PLAYER_{playerUniqueId}] ¡COLISIÓN! con {collision.gameObject.name}");
            
            // Solicitar al servidor que procese la colisión (solo desde el propietario del objeto)
            if (IsOwner)
            {
                HandleCollisionServerRpc(otherPlayer.OwnerClientId);
            }
        }
    }
    
    [ServerRpc]
    private void HandleCollisionServerRpc(ulong otherPlayerId)
    {
        // Obtener el objeto del otro jugador usando el NetworkManager
        NetworkObject otherPlayerObj = NetworkManager.Singleton.ConnectedClients[otherPlayerId].PlayerObject;
        
        if (otherPlayerObj != null)
        {
            PlayerNetwork otherPlayer = otherPlayerObj.GetComponent<PlayerNetwork>();
            
            if (otherPlayer != null)
            {
                // Cálculo de dirección de repulsión
                Vector3 repulsionDirection = (transform.position - otherPlayerObj.transform.position).normalized;
                repulsionDirection.y = 0; // Mantener la repulsión en el plano horizontal
                
                // Aplicar repulsión
                ApplyRepulsionClientRpc(repulsionDirection * repulsionForce);
                otherPlayer.ApplyRepulsionClientRpc(-repulsionDirection * repulsionForce);
                
                // Aturdir a ambos jugadores
                SetStunStatusServerRpc(true);
                otherPlayer.SetStunStatusServerRpc(true);
                
                // Aplicar daño si existe el componente PlayerStats
                // Aquí podrías agregar lógica adicional para ajustar el daño según habilidades, etc.
                float damage = collisionDamage;
                
                // Aplicar efecto visual (opcional)
                SpawnCollisionEffectClientRpc(transform.position + (repulsionDirection * 0.5f));
            }
        }
    }
    
    [ServerRpc]
    private void SetStunStatusServerRpc(bool status)
    {
        // Actualizar la variable de red
        isStunned.Value = status;
        
        // Si estamos aturdiendo al jugador
        if (status)
        {
            // Iniciar temporizador para quitar el aturdimiento después de un tiempo
            StartCoroutine(RemoveStunAfterDelay(collisionStunDuration));
        }
    }
    
    private IEnumerator RemoveStunAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // Solo ejecutar si todavía estamos aturdidos
        if (isStunned.Value)
        {
            isStunned.Value = false;
        }
    }
    
    [ClientRpc]
    private void ApplyRepulsionClientRpc(Vector3 force)
    {
        // Si soy el propietario, aplicar la fuerza al rigidbody
        if (IsOwner && rb != null)
        {
            // Detener el movimiento actual
            isMovingToTarget = false;
            
            // Aplicar la fuerza de repulsión
            rb.velocity = Vector3.zero;
            rb.AddForce(force, ForceMode.Impulse);
            
            Debug.Log($"[PLAYER_{playerUniqueId}] Aplicando fuerza de repulsión: {force.magnitude} en dirección {force.normalized}");
            
            // Comenzar a sincronizar posición continuamente después de la colisión
            StartCoroutine(SyncPositionAfterCollision());
        }
    }
    
    private IEnumerator SyncPositionAfterCollision()
    {
        float syncDuration = 3.0f; // Sincronizar durante 3 segundos después de la colisión
        float syncInterval = 0.05f; // Intervalo de sincronización en segundos
        float timer = 0f;
        
        while (timer < syncDuration)
        {
            // Sincronizar posición y rotación actual con el servidor
            if (IsOwner)
            {
                UpdatePositionServerRpc(transform.position, transform.rotation);
            }
            
            yield return new WaitForSeconds(syncInterval);
            timer += syncInterval;
        }
    }
    
    [ClientRpc]
    private void SpawnCollisionEffectClientRpc(Vector3 position)
    {
        // Solo crear el efecto visual si tenemos un prefab asignado
        if (collisionEffectPrefab != null)
        {
            Instantiate(collisionEffectPrefab, position, Quaternion.identity);
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
        
        // Desuscribirse del evento de cambio de isStunned
        isStunned.OnValueChanged -= OnStunnedValueChanged;
        
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