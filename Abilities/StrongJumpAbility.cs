using Unity.Netcode;
using UnityEngine;
using System.Collections;

namespace PlayerAbilities
{
public class StrongJumpAbility : BaseAbility
{
    [Header("Configuración de Salto Fuerte")]
    [SerializeField] private float jumpHeight = 3f;          // Altura máxima del salto (más pequeña que Earthquake)
    [SerializeField] private float riseTime = 0.5f;          // Tiempo de subida del salto (más rápido)
    [SerializeField] private float fallTime = 0.3f;          // Tiempo de caída (más rápido)
    [SerializeField] private float immobilizationTime = 0.2f;  // Tiempo de inmovilización después del aterrizaje
    [SerializeField] private float baseJumpDistance = 6f;   // Distancia base del salto
    [SerializeField] private float maxSpeedMultiplier = 2.5f; // Multiplicador máximo por velocidad
    [SerializeField] private float maxSpeedThreshold = 15f;  // Velocidad a la que se aplica el multiplicador máximo
    [SerializeField] private GameObject landingEffectPrefab;  // Efecto visual al aterrizar
    [SerializeField] private float minMovementSpeed = 0.5f;  // Velocidad mínima para activar

    // Variable para guardar la distancia de salto calculada
    private float jumpDistance = 10f;

    // Estado del salto - NetworkVariables para sincronización estricta
    private NetworkVariable<bool> networkIsJumping = new NetworkVariable<bool>(false, 
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> networkIsFalling = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> networkIsImmobilized = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector3> networkJumpStartPosition = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector3> networkJumpDirection = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector3> networkCurrentPosition = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Nueva variable para sincronizar la distancia de salto 
    private NetworkVariable<float> networkJumpDistance = new NetworkVariable<float>(
        10f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Variables locales para seguimiento de animación
    private bool isJumping = false;
    private bool isFalling = false;
    private bool isImmobilized = false;
    private Vector3 jumpStartPosition;
    private Vector3 jumpHighestPosition;
    private float jumpTime = 0f;
    private Vector3 jumpDirection = Vector3.zero;
    private bool syncingTransform = false;
    
    // Variables para seguimiento de movimiento
    private Vector3 lastPosition;
    private float currentSpeed = 0f;
    private float lastSpeedUpdateTime = 0f;
    private float speedUpdateInterval = 0.1f; // Actualizar cada 0.1 segundos
    
    // Referencias a componentes externos
    private PlayerAbilityController abilityController;
    private NetworkObject netObj;
    
    public override void Initialize(NetworkBehaviour owner)
    {
        base.Initialize(owner);
        abilityName = "Salto Fuerte";
        activationKey = KeyCode.W;  // Tecla W para esta habilidad
        manaCost = 20f;
        cooldown = 8f;
        
        abilityController = owner.GetComponent<PlayerAbilityController>();
        netObj = owner.GetComponent<NetworkObject>();
        
        // Suscribirse a los cambios en las NetworkVariables
        networkIsJumping.OnValueChanged += OnJumpingChanged;
        networkIsFalling.OnValueChanged += OnFallingChanged;
        networkIsImmobilized.OnValueChanged += OnImmobilizedChanged;
        networkJumpStartPosition.OnValueChanged += OnJumpStartPositionChanged;
        networkCurrentPosition.OnValueChanged += OnCurrentPositionChanged;
        networkJumpDirection.OnValueChanged += OnJumpDirectionChanged;
        networkJumpDistance.OnValueChanged += OnJumpDistanceChanged;
        
        // Establecer posición inicial
        if (networkOwner.IsOwner)
        {
            networkCurrentPosition.Value = networkOwner.transform.position;
        }
        
        // Inicializar variables de seguimiento de movimiento
        lastPosition = networkOwner.transform.position;
        lastSpeedUpdateTime = Time.time;
        
        Debug.Log($"[StrongJumpAbility] Inicializada en {owner.name}, IsOwner: {owner.IsOwner}, IsServer: {owner.IsServer}, ID: {owner.OwnerClientId}");
    }
    
    // Cuando se destruye la habilidad, desuscribir de eventos
    private void OnDestroy()
    {
        networkIsJumping.OnValueChanged -= OnJumpingChanged;
        networkIsFalling.OnValueChanged -= OnFallingChanged;
        networkIsImmobilized.OnValueChanged -= OnImmobilizedChanged;
        networkJumpStartPosition.OnValueChanged -= OnJumpStartPositionChanged;
        networkCurrentPosition.OnValueChanged -= OnCurrentPositionChanged;
        networkJumpDirection.OnValueChanged -= OnJumpDirectionChanged;
        networkJumpDistance.OnValueChanged -= OnJumpDistanceChanged;
    }
    
    // Nuevo handler para la distancia de salto
    private void OnJumpDistanceChanged(float previousValue, float newValue)
    {
        jumpDistance = newValue;
        Debug.Log($"[StrongJumpAbility] Distancia de salto actualizada: {jumpDistance}");
    }
    
    // Handlers para las NetworkVariables
    private void OnJumpingChanged(bool previousValue, bool newValue)
    {
        Debug.Log($"[StrongJumpAbility] OnJumpingChanged: {previousValue} -> {newValue}, IsOwner: {networkOwner.IsOwner}, IsServer: {networkOwner.IsServer}");
        
        // Si somos el host y la variable ya está actualizada localmente, no sobreescribir
        if (networkOwner.IsOwner && networkOwner.IsServer && isJumping == newValue)
        {
            Debug.Log("[StrongJumpAbility] Host ignora cambio de NetworkVariable porque ya fue actualizado localmente");
            return;
        }
        
        isJumping = newValue;
        
        if (isJumping)
        {
            // Activar modo de sincronización manual de transform
            syncingTransform = true;
            
            if (!networkOwner.IsOwner)
            {
                // Para no propietarios, iniciar la animación
                jumpTime = 0f;
                jumpStartPosition = networkJumpStartPosition.Value;
                jumpDistance = networkJumpDistance.Value;
                
                // Desactivar controladores físicos para la animación
                DisablePhysicsControllers();
            }
            
            Debug.Log($"[StrongJumpAbility] Iniciando salto para {(networkOwner.IsOwner ? "local" : "remoto")}");
        }
    }
    
    private void OnFallingChanged(bool previousValue, bool newValue)
    {
        Debug.Log($"[StrongJumpAbility] OnFallingChanged: {previousValue} -> {newValue}, IsOwner: {networkOwner.IsOwner}");
        
        isFalling = newValue;
        
        if (isFalling)
        {
            if (!networkOwner.IsOwner)
            {
                // Para no propietarios, iniciar la animación
                jumpTime = 0f;
                
                // Si no tenemos la posición más alta, calcularla
                if (jumpHighestPosition == Vector3.zero && jumpStartPosition != Vector3.zero)
                {
                    jumpHighestPosition = new Vector3(
                        jumpStartPosition.x + jumpDirection.x * (jumpDistance * 0.5f),
                        jumpStartPosition.y + jumpHeight,
                        jumpStartPosition.z + jumpDirection.z * (jumpDistance * 0.5f)
                    );
                }
            }
            
            Debug.Log($"[StrongJumpAbility] Iniciando caída para {(networkOwner.IsOwner ? "local" : "remoto")}");
        }
    }
    
    private void OnImmobilizedChanged(bool previousValue, bool newValue)
    {
        Debug.Log($"[StrongJumpAbility] OnImmobilizedChanged: {previousValue} -> {newValue}, IsOwner: {networkOwner.IsOwner}");
        
        isImmobilized = newValue;
        
        // Informar al controlador de habilidades sobre el estado de pausa
        if (abilityController != null)
        {
            abilityController.SetInImpactPause(isImmobilized);
        }
        
        if (isImmobilized && !networkOwner.IsOwner)
        {
            // Definir la posición de aterrizaje (destino final del salto)
            Vector3 landingPosition = new Vector3(
                jumpStartPosition.x + jumpDirection.x * jumpDistance,
                jumpStartPosition.y,
                jumpStartPosition.z + jumpDirection.z * jumpDistance
            );
            
            // Restaurar posición para jugadores remotos
            if (jumpStartPosition != Vector3.zero)
            {
                networkOwner.transform.position = landingPosition;
                
                // Mostrar efecto de aterrizaje (también para no propietarios)
                if (landingEffectPrefab != null)
                {
                    Instantiate(landingEffectPrefab, landingPosition, Quaternion.identity);
                }
            }
            
            // Para jugadores remotos, también iniciar la inmovilización
            StartCoroutine(ImmobilizationPause());
        }
        
        if (!newValue)
        {
            // Desactivar modo de sincronización manual cuando termina la inmovilización
            syncingTransform = false;
        }
    }
    
    private void OnJumpStartPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        Debug.Log($"[StrongJumpAbility] OnJumpStartPositionChanged: {previousValue} -> {newValue}, IsOwner: {networkOwner.IsOwner}");
        
        // Siempre actualizar localmente
        jumpStartPosition = newValue;
    }
    
    private void OnCurrentPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        // Solo actualizar la posición si estamos en modo de sincronización y no somos el propietario
        if (syncingTransform && !networkOwner.IsOwner)
        {
            // Aplicar suavizado para evitar tiritón
            networkOwner.transform.position = Vector3.Lerp(
                networkOwner.transform.position, 
                newValue, 
                Time.deltaTime * 20f); // Mayor velocidad para reducir tiritón
        }
    }
    
    private void OnJumpDirectionChanged(Vector3 previousValue, Vector3 newValue)
    {
        Debug.Log($"[StrongJumpAbility] OnJumpDirectionChanged: {previousValue} -> {newValue}, IsOwner: {networkOwner.IsOwner}");
        jumpDirection = newValue;
    }
    
    // Método para verificar si se puede activar la habilidad
    public override bool CanActivate()
    {
        // No permitir activar si ya estamos en medio de un salto, caída o inmovilizado
        if (isJumping || isFalling || isImmobilized)
        {
            if (networkOwner.IsOwner)
            {
                Debug.Log("[StrongJumpAbility] No se puede activar: ya está en uso");
            }
            return false;
        }
        
        // Verificar si estamos en movimiento (necesario para esta habilidad)
        if (networkOwner.IsOwner)
        {
            // Actualizar velocidad actual
            UpdateCurrentSpeed();
            
            if (currentSpeed < minMovementSpeed)
            {
                Debug.Log($"[StrongJumpAbility] No se puede activar: velocidad insuficiente ({currentSpeed:F2} < {minMovementSpeed:F2})");
                return false;
            }
        }
        
        // Verificar si hay suficiente maná y la habilidad está lista
        return isReady && playerStats.CurrentMana >= manaCost;
    }
    
    // Método para actualizar la velocidad actual
    private void UpdateCurrentSpeed()
    {
        // Solo el propietario necesita calcular su velocidad actual
        if (!networkOwner.IsOwner) return;
        
        // Actualizar velocidad cada cierto intervalo para evitar cálculos excesivos
        if (Time.time - lastSpeedUpdateTime >= speedUpdateInterval)
        {
            // Calcular la distancia recorrida desde la última actualización
            float distance = Vector3.Distance(networkOwner.transform.position, lastPosition);
            
            // Calcular velocidad (unidades por segundo)
            currentSpeed = distance / speedUpdateInterval;
            
            // Actualizar última posición y tiempo
            lastPosition = networkOwner.transform.position;
            lastSpeedUpdateTime = Time.time;
            
            // Debug cada pocos segundos para no saturar
            if (Time.frameCount % 300 == 0)
            {
                Debug.Log($"[StrongJumpAbility] Velocidad actual: {currentSpeed:F2} unidades/seg");
            }
        }
    }
    
    public override void Activate()
    {
        Debug.Log($"[StrongJumpAbility] Método Activate llamado, isOwner: {networkOwner.IsOwner}, isServer: {networkOwner.IsServer}, velocidad: {currentSpeed:F2}");
        
        if (networkOwner.IsOwner)
        {
            if (networkOwner.IsServer) // CASO HOST (servidor + cliente local)
            {
                Debug.Log("[StrongJumpAbility] Activando como HOST");
                
                // Configurar dirección de salto basada en la dirección de movimiento actual
                SetupJumpDirection();
                
                // Asegurarse de que el maná se consume
                if (playerStats.UseMana(manaCost))
                {
                    // Para el host, debemos iniciar manualmente el salto
                    // sin depender de los callbacks de las NetworkVariables
                    
                    // Establecer estados explícitamente para asegurar que el salto ocurra
                    jumpStartPosition = networkOwner.transform.position;
                    jumpTime = 0f;
                    isJumping = true;
                    isFalling = false;
                    isImmobilized = false;
                    syncingTransform = true;
                    
                    // Desactivar física para control manual
                    DisablePhysicsControllers();
                    
                    // Configurar variables de red para que otros clientes lo vean
                    networkJumpStartPosition.Value = jumpStartPosition;
                    networkJumpDirection.Value = jumpDirection;
                    networkJumpDistance.Value = jumpDistance;
                    networkIsJumping.Value = true;
                    networkIsFalling.Value = false;
                    networkIsImmobilized.Value = false;
                    
                    // Forzar actualización para que se vea inmediatamente
                    networkCurrentPosition.Value = jumpStartPosition;
                    
                    // Notificar solo a los demás clientes
                    ActivateClientRpc(netObj.OwnerClientId, jumpDirection, jumpDistance);
                    
                    Debug.Log($"[StrongJumpAbility] Host ha iniciado el salto DIRECTAMENTE con distancia: {jumpDistance:F2}");
                }
            }
            else // Cliente normal no-host
            {
                // Cliente normal, enviar RPC al servidor
                ActivateServerRpc();
            }
        }
        else
        {
            // Cliente remoto viendo otro jugador
            StartJump();
        }
    }
    
    private void SetupJumpDirection()
    {
        jumpStartPosition = networkOwner.transform.position;
        
        // Calcular la dirección de movimiento actual
        Vector3 movementDirection = (networkOwner.transform.position - lastPosition).normalized;
        
        // Si no hay una dirección clara, usar la dirección hacia donde mira el jugador
        if (movementDirection.magnitude < 0.1f)
        {
            movementDirection = networkOwner.transform.forward;
        }
        
        // Ignorar componente vertical
        movementDirection.y = 0;
        movementDirection.Normalize();
        
        // Guardar la dirección
        jumpDirection = movementDirection;
        
        // Calcular multiplicador de distancia basado en velocidad actual
        float speedMultiplier = 1.0f; // Valor base
        
        // Asegurar que tenemos la velocidad actualizada
        UpdateCurrentSpeed();
        
        // Calcular multiplicador según la velocidad
        if (currentSpeed > minMovementSpeed)
        {
            // Proporción relativa entre la velocidad actual y la máxima
            float speedRatio = Mathf.Clamp01((currentSpeed - minMovementSpeed) / (maxSpeedThreshold - minMovementSpeed));
            
            // Aplicar curva para que el efecto sea más pronunciado a mayores velocidades
            speedMultiplier = 1.0f + ((maxSpeedMultiplier - 1.0f) * speedRatio * speedRatio);
            
            // Verificar si estamos en Dash para un bonus adicional
            if (IsDashing())
            {
                // Bonus adicional del 20% en Dash
                speedMultiplier *= 1.2f;
            }
        }
        
        // Aplicar el multiplicador a la distancia base
        jumpDistance = baseJumpDistance * speedMultiplier;
        
        Debug.Log($"[StrongJumpAbility] Dirección de salto: {jumpDirection}, Velocidad: {currentSpeed:F2}, " +
                  $"Multiplicador: {speedMultiplier:F2}, Distancia: {jumpDistance:F2}");
    }
    
    // Método para verificar si estamos en Dash
    private bool IsDashing()
    {
        DashAbility dashAbility = networkOwner.GetComponent<DashAbility>();
        if (dashAbility != null)
        {
            return dashAbility.IsDashing;
        }
        return false;
    }

    [ServerRpc(RequireOwnership = true)]
    private void ActivateServerRpc()
    {
        Debug.Log($"[StrongJumpAbility] ActivateServerRpc recibido, servidor notificado de activación");
        
        // Configurar la dirección de salto
        SetupJumpDirection();
        
        // El servidor verifica que se cumplen todas las condiciones
        if (CanActivate())
        {
            Debug.Log($"[StrongJumpAbility] ActivateServerRpc aprobado, notificando a todos los clientes");
            
            // Consumir maná en el servidor
            if (playerStats != null)
            {
                playerStats.UseMana(manaCost);
            }
            
            // Activar el salto en todos los clientes
            ActivateClientRpc(netObj.OwnerClientId, jumpDirection, jumpDistance);
            
            // Si somos servidor pero no propietario, iniciar el salto explícitamente
            if (!networkOwner.IsOwner)
            {
                StartJump();
            }
        }
        else
        {
            Debug.Log($"[StrongJumpAbility] ActivateServerRpc rechazado, no se cumplen las condiciones");
        }
    }

    [ClientRpc]
    private void ActivateClientRpc(ulong ownerClientId, Vector3 direction, float distance)
    {
        Debug.Log($"[StrongJumpAbility] ActivateClientRpc: Cliente {NetworkManager.Singleton.LocalClientId} notificado. " +
                 $"Dirección={direction}, Distancia={distance:F2}");
        
        // Si somos el host (servidor + cliente local), ignoramos este mensaje porque ya iniciamos el salto
        if (networkOwner.IsServer && networkOwner.IsOwner)
        {
            Debug.Log("[StrongJumpAbility] Host ignora ClientRpc porque ya inició el salto");
            return;
        }
        
        // Para todos los demás clientes, configurar dirección y distancia
        jumpDirection = direction;
        jumpDistance = distance;
        
        // Solo iniciar el salto si somos el propietario o un cliente remoto (no propietario)
        if ((networkOwner.IsOwner && NetworkManager.Singleton.LocalClientId == ownerClientId) || 
            (!networkOwner.IsOwner && !networkOwner.IsServer))
        {
            StartJump();
        }
    }

    private void StartJump()
    {
        if (isJumping || isFalling) return;
        
        Debug.Log($"[StrongJumpAbility] StartJump - Iniciando salto. Posición={networkOwner.transform.position}, " +
                 $"Dirección={jumpDirection}, Distancia={jumpDistance:F2}");
        
        // Guardar posición inicial para cálculos
        jumpStartPosition = networkOwner.transform.position;
        
        // VERIFICAR que no sean valores cero o nulos
        if (jumpDirection.magnitude < 0.01f)
        {
            Debug.LogWarning("[StrongJumpAbility] ¡Dirección de salto es cero! Estableciendo dirección por defecto");
            jumpDirection = networkOwner.transform.forward;
        }
        
        // Desactivar física para control manual
        DisablePhysicsControllers();
        
        // Si somos el propietario, actualizar las variables de red
        if (networkOwner.IsOwner)
        {
            networkJumpStartPosition.Value = jumpStartPosition;
            networkJumpDirection.Value = jumpDirection;
            networkJumpDistance.Value = jumpDistance;
            networkIsJumping.Value = true;
            networkIsFalling.Value = false;
            networkIsImmobilized.Value = false;
            
            // Forzar actualización inmediata de transform
            networkCurrentPosition.Value = jumpStartPosition;
        }
        
        // SIEMPRE actualizar variables locales
        isJumping = true;
        isFalling = false;
        isImmobilized = false;
        jumpTime = 0f;
        syncingTransform = true;
    }
    
    // Método para resetear completamente la habilidad
    public void ResetAbility()
    {
        if (networkOwner.IsOwner)
        {
            // Resetear variables de red
            networkIsJumping.Value = false;
            networkIsFalling.Value = false;
            networkIsImmobilized.Value = false;
        }
        
        // Resetear variables locales
        isJumping = false;
        isFalling = false;
        isImmobilized = false;
        jumpHighestPosition = Vector3.zero;
        jumpTime = 0f;
        syncingTransform = false;
        
        // Resetear estado de cooldown
        isReady = true;
        cooldownEndTime = 0f;
        
        // Asegurar que los controladores físicos estén habilitados
        EnablePhysicsControllers();
        
        Debug.Log($"[StrongJumpAbility] Habilidad reseteada completamente");
    }
    
    public override void UpdateAbility()
    {
        // Debug periódico (cada ~5 segundos)
        if (Time.frameCount % 300 == 0)
        {
            Debug.Log($"[StrongJumpAbility] Estado actual: isJumping={isJumping}, isFalling={isFalling}, " +
                     $"isImmobilized={isImmobilized}, isReady={isReady}, syncingTransform={syncingTransform}");
        }
        
        // Actualizar velocidad de movimiento cuando sea el propietario
        if (networkOwner.IsOwner)
        {
            UpdateCurrentSpeed();
        }
        
        // Actualizar lógica del salto si está activo
        if (isJumping || isFalling)
        {
            UpdateJump();
        }
        
        // Si somos el propietario y estamos en modo de sincronización, actualizar posición en la red
        if (networkOwner.IsOwner && syncingTransform)
        {
            if (Vector3.Distance(networkCurrentPosition.Value, networkOwner.transform.position) > 0.01f)
            {
                networkCurrentPosition.Value = networkOwner.transform.position;
            }
        }
    }
    
    private void DisablePhysicsControllers()
    {
        // Desactivar gravedad y preparar para el salto
        if (rb != null)
        {
            rb.useGravity = false;
            rb.velocity = Vector3.zero; // Detener cualquier movimiento previo
        }
        
        // Si hay character controller, desactivarlo durante el salto para control manual
        if (characterController != null && characterController.enabled)
        {
            characterController.enabled = false;
        }
    }
    
    private void EnablePhysicsControllers()
    {
        // Restaurar controladores
        if (rb != null)
        {
            rb.useGravity = true;
            rb.isKinematic = false;
        }
        
        if (characterController != null && !characterController.enabled)
        {
            characterController.enabled = true;
        }
    }
    
    private void UpdateJump()
    {
        // Incrementar el tiempo del salto
        jumpTime += Time.deltaTime;
        
        if (isJumping)
        {
            // Phase de ascenso (con tiempo ajustable)
            if (jumpTime <= riseTime)
            {
                // Usamos una curva sinusoidal para un movimiento más natural
                float progress = jumpTime / riseTime; // Normalizado de 0 a 1
                
                // Calcular altura usando función seno
                float height = jumpHeight * Mathf.Sin(progress * Mathf.PI / 2);
                
                // Calcular desplazamiento horizontal (mitad de la distancia total durante la subida)
                float horizontalProgress = progress;
                Vector3 horizontalOffset = jumpDirection * (jumpDistance * 0.5f) * horizontalProgress;
                
                // Vectores de offset
                Vector3 verticalOffset = new Vector3(0, height, 0);
                
                // Calcular nueva posición combinando vertical y horizontal
                Vector3 newPosition = jumpStartPosition + verticalOffset + horizontalOffset;
                
                // IMPORTANTE: Siempre mover al jugador visualmente
                networkOwner.transform.position = newPosition;
                
                // Si somos propietario, actualizar posición de red
                if (networkOwner.IsOwner)
                {
                    networkCurrentPosition.Value = newPosition;
                }
                
                // Guardar posición más alta cuando estemos cerca del pico
                if (progress >= 0.99f)
                {
                    jumpHighestPosition = newPosition;
                }
            }
            else
            {
                // Cambiar a fase de caída y reiniciar timer
                Debug.Log("[StrongJumpAbility] Cambiando de salto a caída");
                
                isJumping = false;
                isFalling = true;
                jumpTime = 0f;
                
                // Guardar la posición más alta alcanzada
                if (jumpHighestPosition == Vector3.zero)
                {
                    jumpHighestPosition = networkOwner.transform.position;
                }
                
                // Si somos el propietario, actualizar variables de red
                if (networkOwner.IsOwner)
                {
                    networkIsJumping.Value = false;
                    networkIsFalling.Value = true;
                }
            }
        }
        else if (isFalling)
        {
            // Fase de descenso (más rápida que la subida)
            if (jumpTime <= fallTime)
            {
                // Curva de caída acelerada
                float progress = jumpTime / fallTime; // Normalizado de 0 a 1
                
                // Utilizar una curva cuadrática para que la caída sea más rápida hacia el final
                float t = progress;
                float heightFactor = 1 - (t * t); // Caída acelerada
                
                // Calcular la posición actual
                // Calcular desplazamiento horizontal (segunda mitad durante la caída)
                float horizontalEndProgress = 0.5f + progress * 0.5f; // 0.5 a 1.0
                
                // Posición final interpolada
                Vector3 newPosition = new Vector3(
                    jumpStartPosition.x + jumpDirection.x * jumpDistance * horizontalEndProgress,
                    jumpStartPosition.y + jumpHeight * heightFactor,
                    jumpStartPosition.z + jumpDirection.z * jumpDistance * horizontalEndProgress
                );
                
                // Aplicar posición
                networkOwner.transform.position = newPosition;
                
                // Si somos el propietario, actualizar la posición en la red
                if (networkOwner.IsOwner)
                {
                    networkCurrentPosition.Value = newPosition;
                }
                
                // Debug para verificar la caída
                if (progress >= 0.98f)
                {
                    // Nos acercamos al final, asegurarnos de que terminaremos en la posición final correcta
                    Vector3 finalPosition = new Vector3(
                        jumpStartPosition.x + jumpDirection.x * jumpDistance,
                        jumpStartPosition.y,
                        jumpStartPosition.z + jumpDirection.z * jumpDistance
                    );
                    
                    networkOwner.transform.position = finalPosition;
                    
                    // Si somos el propietario, actualizar la posición en la red
                    if (networkOwner.IsOwner)
                    {
                        networkCurrentPosition.Value = finalPosition;
                    }
                }
            }
            else
            {
                // El salto ha terminado, entrar en fase de inmovilización
                isFalling = false;
                isImmobilized = true;
                
                // Si somos el propietario, actualizar variables de red
                if (networkOwner.IsOwner)
                {
                    Debug.Log("[StrongJumpAbility] Cambiando de caída a inmovilización");
                    networkIsFalling.Value = false;
                    networkIsImmobilized.Value = true;
                }
                
                // Restaurar posición exactamente a la final 
                Vector3 landingPosition = new Vector3(
                    jumpStartPosition.x + jumpDirection.x * jumpDistance,
                    jumpStartPosition.y,
                    jumpStartPosition.z + jumpDirection.z * jumpDistance
                );
                
                networkOwner.transform.position = landingPosition;
                
                // Si somos el propietario, actualizar la posición en la red
                if (networkOwner.IsOwner)
                {
                    networkCurrentPosition.Value = landingPosition;
                }
                
                // Configurar Rigidbody para la inmovilización
                if (rb != null)
                {
                    rb.useGravity = true;
                    rb.velocity = Vector3.zero; // Detener al aterrizar
                    rb.isKinematic = true; // Evitar que fuerzas externas lo muevan durante la inmovilización
                }
                
                // Mostrar efecto de aterrizaje
                if (networkOwner.IsOwner && landingEffectPrefab != null)
                {
                    TriggerLandingEffectServerRpc(landingPosition);
                }
                
                // Iniciar la inmovilización después del aterrizaje
                StartCoroutine(ImmobilizationPause());
            }
        }
    }
    
    private IEnumerator ImmobilizationPause()
    {
        // Asegurarse de que estamos en modo inmovilizado
        isImmobilized = true;
        
        // Posición final del salto
        Vector3 landingPosition = new Vector3(
            jumpStartPosition.x + jumpDirection.x * jumpDistance,
            jumpStartPosition.y,
            jumpStartPosition.z + jumpDirection.z * jumpDistance
        );
        
        // Informar al controlador de habilidades sobre el estado de inmovilización
        if (abilityController != null)
        {
            abilityController.SetInImpactPause(true);
        }
        
        Debug.Log($"[StrongJumpAbility] Iniciando inmovilización - {(networkOwner.IsOwner ? "PROPIETARIO" : "NO PROPIETARIO")}");
        
        // Esperar el tiempo de inmovilización exacto
        yield return new WaitForSeconds(immobilizationTime);
        
        Debug.Log($"[StrongJumpAbility] Finalizando inmovilización - {(networkOwner.IsOwner ? "PROPIETARIO" : "NO PROPIETARIO")}");
        
        if (networkOwner.IsOwner)
        {
            // Solo el propietario debe actualizar la variable de red
            networkIsImmobilized.Value = false;
        }
        
        // Restaurar estado después de la inmovilización
        isImmobilized = false;
        syncingTransform = false;
        
        // Informar al controlador de habilidades que la inmovilización ha terminado
        if (abilityController != null)
        {
            abilityController.SetInImpactPause(false);
        }
        
        // Restaurar controladores y física
        EnablePhysicsControllers();
        
        // Iniciar cooldown
        networkOwner.StartCoroutine(StartCooldown());
        
        // Resetear las variables de salto
        jumpHighestPosition = Vector3.zero;
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void TriggerLandingEffectServerRpc(Vector3 position)
    {
        // Enviar a todos los clientes
        TriggerLandingEffectClientRpc(position);
    }
    
    [ClientRpc]
    private void TriggerLandingEffectClientRpc(Vector3 position)
    {
        // Mostrar efecto visual
        if (landingEffectPrefab != null)
        {
            GameObject effect = Instantiate(landingEffectPrefab, position, Quaternion.identity);
            
            // Destruir después de unos segundos
            Destroy(effect, 2.0f);
            
            Debug.Log($"[StrongJumpAbility] Efecto visual de aterrizaje instanciado con éxito en {position}");
        }
    }
    
    // Propiedades públicas para que otros sistemas puedan consultar el estado
    public bool IsJumping => isJumping;
    public bool IsFalling => isFalling;
    public bool IsImmobilized => isImmobilized;
    public float CurrentSpeed => currentSpeed;
    
    // Método público para verificar si se está moviendo lo suficientemente rápido
    public bool IsMovingFastEnough()
    {
        return currentSpeed >= minMovementSpeed;
    }
}
}