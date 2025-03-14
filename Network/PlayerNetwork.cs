using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using PlayerAbilities; // Añadimos este namespace para acceder a las habilidades

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
    
    // NUEVO: Variable para controlar si el jugador puede moverse (para respawn)
    private NetworkVariable<bool> playerControlEnabled = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
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
    
    // Referencia al controlador de habilidades para verificar estados
    private PlayerAbilityController abilityController;
    
    // Referencia a la habilidad de terremoto para verificar su estado
    private EarthquakeAbility earthquakeAbility;
    
    // Referencia a la habilidad dash
    private DashAbility dashAbility;
    
    // Referencia al StrongJumpAbility
    private StrongJumpAbility strongJumpAbility;
    
    // Nueva referencia para el combate
    private PlayerCombat playerCombat;
    
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
        
        // Obtener referencias al sistema de habilidades
        abilityController = GetComponent<PlayerAbilityController>();
        
        // Obtener referencia al sistema de combate
        playerCombat = GetComponent<PlayerCombat>();
    }
    
    // Override para el spawn en red
    public override void OnNetworkSpawn()
    {
        Debug.Log($"[PLAYER_{playerUniqueId}] OnNetworkSpawn - IsOwner: {IsOwner}, IsLocalPlayer: {IsLocalPlayer}, OwnerClientId: {OwnerClientId}, Position: {transform.position}");
        
        // Inicializar posición y rotación en la red
        if (IsServer)
        {
            // El servidor establece las variables de red iniciales
            networkPosition.Value = transform.position;
            networkRotation.Value = transform.rotation;
            isStunned.Value = false;
            playerControlEnabled.Value = true;
            
            Debug.Log($"[PLAYER_{playerUniqueId}] Servidor inicializó posición de red: {networkPosition.Value}");
        }
        
        // Suscribirse al cambio de la variable isStunned
        isStunned.OnValueChanged += OnStunnedValueChanged;
        
        // Suscribirse al cambio de la variable playerControlEnabled
        playerControlEnabled.OnValueChanged += OnPlayerControlEnabledChanged;
        
        // Inicializar con un pequeño retraso para permitir sincronización 
        StartCoroutine(DelayedInitialization());
    }
    
    private IEnumerator DelayedInitialization()
    {
        // Pequeño retraso para permitir que la sincronización de red ocurra
        yield return new WaitForSeconds(0.2f);
        
        // Solo el cliente local debe crear y gestionar su propia cámara
        if (IsLocalPlayer)
        {
            Debug.Log($"[PLAYER_{playerUniqueId}] Configurando jugador local con ClientId: {OwnerClientId}, Posición: {transform.position}");
            
            // Inicializar posición objetivo con posición actual
            targetPosition = transform.position;
            lastPosition = transform.position;
            
            // Si no somos el servidor, enviar nuestra posición actual para verificar
            if (!IsServer)
            {
                Debug.Log($"[PLAYER_{playerUniqueId}] Cliente validando posición inicial: {transform.position}");
                ValidatePositionServerRpc(transform.position);
            }
            
            // Pequeño delay para asegurar que todo está inicializado correctamente
            yield return new WaitForSeconds(0.2f);
            
            // Crear cámara local
            CreateLocalCamera();
            
            // Crear Indicator si está configurado
            if (clickIndicatorPrefab != null)
            {
                CreateLocalClickIndicator();
            }
            
            // Buscar referencia a las habilidades
            StartCoroutine(FindAbilities());
        }
    }
    
    // NUEVO: Método para sincronizar transform inicial
    [ServerRpc(RequireOwnership = false)]
    public void SyncInitialTransformServerRpc(Vector3 position, Quaternion rotation)
    {
        // Sólo el servidor puede modificar las variables de red
        if (!IsServer) return;
        
        Debug.Log($"[PLAYER_{playerUniqueId}] SyncInitialTransformServerRpc: Servidor recibió solicitud para posición {position}");
        
        // Establecer la posición en el servidor
        transform.position = position;
        transform.rotation = rotation;
        
        // Actualizar las variables de red
        networkPosition.Value = position;
        networkRotation.Value = rotation;
        
        // Notificar a todos los clientes
        SyncTransformClientRpc(position, rotation);
    }
    
    [ClientRpc]
    public void SyncTransformClientRpc(Vector3 position, Quaternion rotation)
    {
        Debug.Log($"[PLAYER_{playerUniqueId}] SyncTransformClientRpc: Cliente recibió posición {position}");
        
        // Establecer la posición en todos los clientes
        transform.position = position;
        transform.rotation = rotation;
        
        // Para el propietario, también actualizar la posición objetivo
        if (IsOwner)
        {
            targetPosition = position;
            lastPosition = position;
            
            // Detener cualquier movimiento en curso
            isMovingToTarget = false;
            
            // Detener cualquier velocidad
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
    
    // NUEVO: Método para validar la posición con el servidor
    [ServerRpc]
    public void ValidatePositionServerRpc(Vector3 clientPosition)
    {
        if (!IsServer) return;
        
        Debug.Log($"[PLAYER_{playerUniqueId}] ValidatePositionServerRpc: Cliente en {clientPosition}, Servidor tiene {networkPosition.Value}");
        
        // Calcular discrepancia
        float discrepancy = Vector3.Distance(clientPosition, networkPosition.Value);
        
        // Si hay una discrepancia significativa, corregir
        if (discrepancy > 0.5f)
        {
            Debug.LogWarning($"[PLAYER_{playerUniqueId}] Discrepancia detectada ({discrepancy}m)! Corrigiendo posición del cliente.");
            SyncTransformClientRpc(networkPosition.Value, networkRotation.Value);
        }
    }
    
    private IEnumerator FindAbilities()
    {
        // Esperar un poco para asegurar que las habilidades estén inicializadas
        yield return new WaitForSeconds(0.3f);
        
        dashAbility = GetComponent<DashAbility>();
        strongJumpAbility = GetComponent<StrongJumpAbility>();
        
        if (strongJumpAbility != null)
        {
            Debug.Log($"[PLAYER_{playerUniqueId}] Encontrada referencia a StrongJumpAbility");
        }
        else
        {
            Debug.LogWarning($"[PLAYER_{playerUniqueId}] No se encontró StrongJumpAbility, reintentando...");
            
            // Intentar una vez más
            yield return new WaitForSeconds(0.5f);
            strongJumpAbility = GetComponent<StrongJumpAbility>();
        }
        
        if (dashAbility != null)
        {
            Debug.Log($"[PLAYER_{playerUniqueId}] Encontrada referencia a DashAbility");
        }
        else
        {
            Debug.LogWarning($"[PLAYER_{playerUniqueId}] No se encontró DashAbility, reintentando...");
            
            // Intentar una vez más
            yield return new WaitForSeconds(0.5f);
            dashAbility = GetComponent<DashAbility>();
        }
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
    
    // NUEVO: Método que se ejecuta cuando cambia el valor de playerControlEnabled
    private void OnPlayerControlEnabledChanged(bool oldValue, bool newValue)
    {
        Debug.Log($"[PLAYER_{playerUniqueId}] Control de jugador cambió de {oldValue} a {newValue}");
        
        if (!newValue)
        {
            // Si se desactiva el control, detener cualquier movimiento
            isMovingToTarget = false;
            
            // Si somos el dueño, detenemos el rigidbody
            if (IsOwner && rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
    
    // NUEVO: Método público para habilitar/deshabilitar el control del jugador
    public void SetPlayerControlEnabled(bool enabled)
    {
        if (IsServer)
        {
            playerControlEnabled.Value = enabled;
        }
        else if (IsOwner)
        {
            SetPlayerControlEnabledServerRpc(enabled);
        }
    }
    
    // NUEVO: ServerRpc para habilitar/deshabilitar el control del jugador
    [ServerRpc]
    public void SetPlayerControlEnabledServerRpc(bool enabled)
    {
        playerControlEnabled.Value = enabled;
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
    
    // Método para verificar si alguna habilidad está controlando la posición del jugador
    private bool IsPositionControlledByAbility()
    {
        bool abilityControllingPosition = false;
        
        // Verificar si hay habilidades activas que controlen la posición
        if (abilityController != null)
        {
            // Verificar StrongJumpAbility
            if (strongJumpAbility != null && (strongJumpAbility.IsJumping || strongJumpAbility.IsFalling || strongJumpAbility.IsImmobilized))
            {
                abilityControllingPosition = true;
            }
            
            // Verificar DashAbility
            if (dashAbility != null && dashAbility.IsDashing)
            {
                abilityControllingPosition = true;
            }
        }
        
        return abilityControllingPosition;
    }
    
    private void Update()
    {
        // MODIFICADO: Verificar primero si el control está habilitado
        if (!playerControlEnabled.Value)
        {
            return; // Salir temprano si el control está deshabilitado
        }
        
        if (IsLocalPlayer)
        {
            // Verificar si hay alguna pausa de habilidad activa
            bool isAbilityPaused = IsInAbilityPause();
            
            // Verificar si alguna habilidad está controlando la posición
            bool abilityControllingPosition = IsPositionControlledByAbility();
            
            // Solo procesar input si no estamos aturdidos, en pausa de habilidad o con posición controlada por habilidad
            if (canMove && !isAbilityPaused && !abilityControllingPosition)
            {
                // NUEVO: Procesar el botón izquierdo para ataques
                HandleMouseAttack();
                
                // Procesar el botón derecho para movimiento
                HandleMouseMovement();
                
                // Manejar movimiento hacia posición objetivo si estamos en modo click-to-move
                if (isMovingToTarget)
                {
                    MoveToTargetPosition();
                }
            }
            else
            {
                // Si hay algún bloqueo (pausa, aturdimiento, control por habilidad), cancelar cualquier movimiento
                isMovingToTarget = false;
                
                // Log para depuración pero solo de vez en cuando
                if (Time.frameCount % 60 == 0) 
                {
                    string reason = "desconocida";
                    if (!canMove) reason = "aturdimiento";
                    else if (isAbilityPaused) reason = "pausa de habilidad";
                    else if (abilityControllingPosition) reason = "control de posición por habilidad";
                    
                    Debug.Log($"[PLAYER_{playerUniqueId}] Movimiento bloqueado por: {reason}");
                }
                
                // Mientras estemos en pausa o controlados por habilidad, actualizar posición al servidor
                UpdatePositionServerRpc(transform.position, transform.rotation);
            }
            
            // Cuando estemos usando físicas, actualizar la posición en el servidor
            // incluso si no estamos controlando el movimiento directamente
            if ((!canMove || isAbilityPaused || abilityControllingPosition) && rb != null && rb.velocity.magnitude > 0.01f)
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
    
    // Nuevo método para verificar si hay alguna pausa de habilidad activa
    private bool IsInAbilityPause()
    {
        // Verificar pausa en el controlador de habilidades
        if (abilityController != null && abilityController.isInImpactPause)
        {
            return true;
        }
        
        // Verificar directamente el estado de inmobilización de StrongJump
        if (strongJumpAbility != null && strongJumpAbility.IsImmobilized)
        {
            return true;
        }
        
        return false;
    }
    
    // Nuevo método para manejar ataques con el botón izquierdo del mouse
    private void HandleMouseAttack()
    {
        // Si estamos aturdidos o en pausa de habilidad, no procesar entrada
        if (!canMove || IsInAbilityPause() || IsPositionControlledByAbility()) 
        {
            return;
        }
        
        // Click izquierdo para atacar
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = playerCamera != null ? playerCamera.ScreenPointToRay(Input.mousePosition) : Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            
            // Verificar si hacemos clic en un jugador enemigo
            if (Physics.Raycast(ray, out hit))
            {
                // Verificar si el objeto golpeado es un jugador enemigo
                NetworkObject hitNetObj = hit.collider.GetComponent<NetworkObject>();
                
                // Si es un NetworkObject y no somos nosotros mismos
                if (hitNetObj != null && hitNetObj.OwnerClientId != OwnerClientId)
                {
                    // Verificar si el otro objeto es un jugador
                    PlayerStats enemyStats = hitNetObj.GetComponent<PlayerStats>();
                    
                    if (enemyStats != null && playerCombat != null)
                    {
                        // Intentar procesar como ataque
                        bool attacked = playerCombat.ProcessClickOnEnemy(hitNetObj);
                        
                        if (attacked)
                        {
                            // Procesado exitosamente como ataque
                            Debug.Log($"[PLAYER_{playerUniqueId}] Atacando a jugador {hitNetObj.OwnerClientId}");
                            return;
                        }
                    }
                }
            }
        }
    }
    
    private void HandleMouseMovement()
    {
        // Si estamos aturdidos o en pausa de habilidad, no procesar movimiento
        if (!canMove || IsInAbilityPause() || IsPositionControlledByAbility()) 
        {
            if (Input.GetMouseButtonDown(1))
            {
                Debug.Log($"[PLAYER_{playerUniqueId}] Movimiento por clic bloqueado por pausa/aturdimiento/habilidad");
            }
            return;
        }
        
        // Click derecho SOLO para mover (ya no detectamos enemigos aquí)
        if (Input.GetMouseButtonDown(1))
        {
            Ray ray = playerCamera != null ? playerCamera.ScreenPointToRay(Input.mousePosition) : Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            
            // Verificar si el raycast golpea en la capa del suelo
            if (Physics.Raycast(ray, out hit, 100f, groundLayer))
            {
                // Verificar otra vez si estamos en pausa o aturdidos o controlados por habilidad
                if (!canMove || IsInAbilityPause() || IsPositionControlledByAbility())
                {
                    Debug.Log($"[PLAYER_{playerUniqueId}] Movimiento bloqueado en último momento por pausa/aturdimiento/habilidad");
                    return;
                }
                
                // NUEVO: Si estábamos siguiendo a un objetivo para atacar, cancelar
                if (playerCombat != null)
                {
                    playerCombat.ClearTarget();
                }
                
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
        // Verificar constantemente si hay pausas o aturdimientos o control por habilidad
        if (!canMove || IsInAbilityPause() || IsPositionControlledByAbility())
        {
            isMovingToTarget = false;
            return;
        }
        
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
    
// En la clase PlayerNetwork.cs, modifica el método HandleCollisionServerRpc:

[ServerRpc]
public void HandleCollisionServerRpc(ulong otherPlayerId)
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
            
            // NUEVO: Aplicar daño a ambos jugadores
            PlayerStats myStats = GetComponent<PlayerStats>();
            PlayerStats otherStats = otherPlayerObj.GetComponent<PlayerStats>();
            
            if (myStats != null && otherStats != null)
            {
                // Calcular daño basado en velocidad relativa para un impacto más realista
                float relativeSpeed = 0f;
                
                // Si ambos tienen rigidbody, usar la velocidad relativa
                Rigidbody myRb = GetComponent<Rigidbody>();
                Rigidbody otherRb = otherPlayerObj.GetComponent<Rigidbody>();
                
                if (myRb != null && otherRb != null)
                {
                    // Calcular velocidad relativa entre los dos jugadores
                    relativeSpeed = Vector3.Distance(myRb.velocity, otherRb.velocity);
                }
                
                // Calcular daño base + componente de velocidad
                float damageAmount = collisionDamage;
                if (relativeSpeed > 1.0f)
                {
                    // Añadir bonus por velocidad (más rápido = más daño)
                    damageAmount += relativeSpeed * 0.5f;
                }
                
                // Log para depuración
                Debug.Log($"[Colisión] Aplicando {damageAmount} de daño entre jugadores {OwnerClientId} y {otherPlayerId}");
                
                // Aplicar daño a ambos
                myStats.TakeDamage(damageAmount);
                otherStats.TakeDamage(damageAmount);
                
                // OPCIONAL: Notificar a los clientes sobre el daño
                NotifyCollisionDamageClientRpc(damageAmount);
                otherPlayer.NotifyCollisionDamageClientRpc(damageAmount);
            }
            
            // Aplicar efecto visual
            SpawnCollisionEffectClientRpc(transform.position + (repulsionDirection * 0.5f));
        }
    }
}

// Añade este nuevo método para notificar al cliente sobre el daño recibido
[ClientRpc]
public void NotifyCollisionDamageClientRpc(float damageAmount)
{
    if (IsOwner)
    {
        // Mostrar al jugador local que ha recibido daño por colisión
        Debug.Log($"¡Has recibido {damageAmount} de daño por colisión!");
        
        // Si tienes un sistema de UI para mostrar daño, podrías usarlo aquí
        // Por ejemplo:
        // damageIndicatorUI.ShowDamage(damageAmount, Color.red);
    }
}
    
    [ServerRpc]
    public void SetStunStatusServerRpc(bool status)
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
    public void ApplyRepulsionClientRpc(Vector3 force)
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
    public void SpawnCollisionEffectClientRpc(Vector3 position)
    {
        // Solo crear el efecto visual si tenemos un prefab asignado
        if (collisionEffectPrefab != null)
        {
            Instantiate(collisionEffectPrefab, position, Quaternion.identity);
        }
    }
    
    // MÉTODOS NUEVOS PARA MANEJO DE IMPACTO DE PROYECTIL //
    
    // Método ServerRpc para aplicar fuerza de impacto (llamado desde CombatProjectile)
    [ServerRpc(RequireOwnership = false)]
    public void ApplyImpactForceServerRpc(Vector3 force)
    {
        // Solo el servidor procesa este método
        if (!IsServer) return;

        Debug.Log($"[PLAYER_{playerUniqueId}] Servidor aplicando fuerza de impacto: {force.magnitude}");
        
        // Notificar a todos los clientes sobre el impacto
        ApplyImpactForceClientRpc(force);
        
        // Detener cualquier movimiento actual
        isMovingToTarget = false;
        
        // Actualizar posición en las variables de red después de aplicar la fuerza
        // Programar actualizaciones de posición continuas por un tiempo
        StartCoroutine(SyncPositionAggressivelyAfterImpact());
    }

    [ClientRpc]
    public void ApplyImpactForceClientRpc(Vector3 force)
    {
        Debug.Log($"[PLAYER_{playerUniqueId}] Cliente recibió fuerza de impacto: {force.magnitude}");
        
        // Aplicar la fuerza al Rigidbody
        if (rb != null)
        {
            // Detener cualquier movimiento actual
            isMovingToTarget = false;
            
            // Detener velocidad actual
            rb.velocity = Vector3.zero;
            
            // Aplicar nueva fuerza
            rb.AddForce(force, ForceMode.Impulse);
            
            // Si somos el propietario, necesitamos sincronizar nuestra posición
            if (IsOwner)
            {
                StartCoroutine(SyncOwnerPositionAfterImpact());
            }
        }
    }

    // Método especial para sincronizar la posición del propietario después de un impacto
    private IEnumerator SyncOwnerPositionAfterImpact()
    {
        // Enviar actualizaciones frecuentes al servidor
        float syncDuration = 2.0f;  // Duración total de la sincronización agresiva
        float syncInterval = 0.05f; // Intervalo entre actualizaciones (50ms = 20 veces por segundo)
        float timer = 0f;
        
        while (timer < syncDuration)
        {
            // Enviar posición al servidor
            UpdatePositionServerRpc(transform.position, transform.rotation);
            
            yield return new WaitForSeconds(syncInterval);
            timer += syncInterval;
        }
    }

    // Método en el servidor para sincronizar la posición con más frecuencia después de un impacto
    private IEnumerator SyncPositionAggressivelyAfterImpact()
    {
        float syncDuration = 2.0f;  // Duración total de la sincronización agresiva
        float syncInterval = 0.05f; // Intervalo entre actualizaciones
        float timer = 0f;
        
        while (timer < syncDuration)
        {
            // Actualizar las variables de red con la posición actual
            networkPosition.Value = transform.position;
            networkRotation.Value = transform.rotation;
            
            yield return new WaitForSeconds(syncInterval);
            timer += syncInterval;
        }
    }
    
    [ServerRpc]
    public void UpdatePositionServerRpc(Vector3 newPosition, Quaternion newRotation)
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
        
        // Desuscribirse del evento de cambio de playerControlEnabled
        playerControlEnabled.OnValueChanged -= OnPlayerControlEnabledChanged;
        
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
    
    // Método público para verificar si el jugador tiene control actualmente
    public bool IsPlayerControlEnabled()
    {
        return playerControlEnabled.Value;
    }
    
    // Método público para obtener el estado de movimiento
    public bool IsMoving()
    {
        return isMovingToTarget;
    }
    
    // Método público para obtener la posición objetivo
    public Vector3 GetTargetPosition()
    {
        return targetPosition;
    }
    
    // Nuevos métodos para funcionalidad de combate
    public void MoveToPositionCommand(Vector3 position)
    {
        // Verificar si podemos movernos
        if (!canMove || IsInAbilityPause() || IsPositionControlledByAbility())
            return;
        
        // Establecer destino
        targetPosition = position;
        isMovingToTarget = true;
        
        // Reiniciar timer de atasco
        stuckTimer = 0f;
        lastPosition = transform.position;
        
        // Notificar al servidor
        UpdatePositionServerRpc(transform.position, transform.rotation);
    }

    public void StopMovement()
    {
        isMovingToTarget = false;
        
        // Detener físicamente al personaje
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
        }
    }

    public bool CanUpdateMovement()
    {
        return canMove && !IsInAbilityPause() && !IsPositionControlledByAbility();
    }
}