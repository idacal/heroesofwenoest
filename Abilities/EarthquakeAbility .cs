using Unity.Netcode;
using UnityEngine;
using System.Collections;

namespace PlayerAbilities
{
public class EarthquakeAbility : BaseAbility
{
    [Header("Configuración de Terremoto")]
    [SerializeField] private float jumpHeight = 5f;           // Altura máxima del salto
    [SerializeField] private float riseTime = 0.8f;           // Tiempo de subida del salto (más lento)
    [SerializeField] private float fallTime = 0.4f;           // Tiempo de caída (más rápido)
    [SerializeField] private float impactPauseTime = 0.5f;    // Tiempo de pausa después del impacto
    [SerializeField] private float earthquakeRadius = 5f;     // Radio del efecto de terremoto
    [SerializeField] private float earthquakeForce = 10f;     // Fuerza de empuje
    [SerializeField] private GameObject earthquakeEffectPrefab; // Efecto visual del terremoto
    [SerializeField] private LayerMask affectedLayers;        // Capas afectadas por el terremoto
    
    [Header("Requisitos de Activación")]
    [SerializeField] private bool requireMovement = true;     // Requiere que el jugador esté en movimiento
    [SerializeField] private float minMovementSpeed = 1.0f;   // Velocidad mínima para activar

    [Header("Salto Direccional")]
    [SerializeField] private bool enableDirectionalJump = true;     // Habilitar salto en dirección del clic
    [SerializeField] private float horizontalJumpDistance = 3.0f;   // Distancia horizontal máxima del salto
    [SerializeField] private AnimationCurve horizontalMovementCurve = new AnimationCurve(
        new Keyframe(0, 0, 0, 0),
        new Keyframe(0.5f, 1.2f, 0, 0),  // Añadimos un "sobresalto" en el medio
        new Keyframe(1, 1, 0, 0)
    );

    // Estado del terremoto - NetworkVariables para sincronización estricta
    private NetworkVariable<bool> networkIsJumping = new NetworkVariable<bool>(false, 
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> networkIsFalling = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> networkIsInImpactPause = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Vector3> networkJumpStartPosition = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Vector3> networkJumpDirection = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Vector3> networkJumpTargetPosition = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    
    // Variables locales para seguimiento de animación
    private bool isJumping = false;
    private bool isFalling = false;
    private bool isInImpactPause = false;
    private Vector3 jumpStartPosition;
    private Vector3 jumpHighestPosition;
    private float jumpTime = 0f;
    
    // Variables para el salto direccional
    private Vector3 jumpDirection = Vector3.zero;
    private Vector3 jumpTargetPosition;
    
    // Control de posición manual
    private NetworkVariable<Vector3> networkCurrentPosition = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
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
        abilityName = "Terremoto";
        activationKey = KeyCode.W;
        manaCost = 60f;
        cooldown = 10f;
        
        abilityController = owner.GetComponent<PlayerAbilityController>();
        netObj = owner.GetComponent<NetworkObject>();
        
        // Suscribirse a los cambios en las NetworkVariables
        networkIsJumping.OnValueChanged += OnJumpingChanged;
        networkIsFalling.OnValueChanged += OnFallingChanged;
        networkIsInImpactPause.OnValueChanged += OnImpactPauseChanged;
        networkJumpStartPosition.OnValueChanged += OnJumpStartPositionChanged;
        networkCurrentPosition.OnValueChanged += OnCurrentPositionChanged;
        networkJumpDirection.OnValueChanged += OnJumpDirectionChanged;
        networkJumpTargetPosition.OnValueChanged += OnJumpTargetPositionChanged;
        
        // Establecer posición inicial
        if (networkOwner.IsOwner)
        {
            networkCurrentPosition.Value = networkOwner.transform.position;
        }
        
        // Inicializar variables de seguimiento de movimiento
        lastPosition = networkOwner.transform.position;
        lastSpeedUpdateTime = Time.time;
        
        Debug.Log($"[EarthquakeAbility] Inicializada en {owner.name}, IsOwner: {owner.IsOwner}, IsServer: {owner.IsServer}");
    }
    
    // Cuando se destruye la habilidad, desuscribir de eventos
    private void OnDestroy()
    {
        networkIsJumping.OnValueChanged -= OnJumpingChanged;
        networkIsFalling.OnValueChanged -= OnFallingChanged;
        networkIsInImpactPause.OnValueChanged -= OnImpactPauseChanged;
        networkJumpStartPosition.OnValueChanged -= OnJumpStartPositionChanged;
        networkCurrentPosition.OnValueChanged -= OnCurrentPositionChanged;
        networkJumpDirection.OnValueChanged -= OnJumpDirectionChanged;
        networkJumpTargetPosition.OnValueChanged -= OnJumpTargetPositionChanged;
    }
    
    // Handlers para las NetworkVariables
    private void OnJumpingChanged(bool previousValue, bool newValue)
    {
        Debug.Log($"[EarthquakeAbility] OnJumpingChanged: {previousValue} -> {newValue}, IsOwner: {networkOwner.IsOwner}");
        
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
                
                // Desactivar controladores físicos para la animación
                DisablePhysicsControllers();
            }
            
            Debug.Log($"[EarthquakeAbility] Iniciando salto para {(networkOwner.IsOwner ? "local" : "remoto")}");
        }
    }
    
    private void OnFallingChanged(bool previousValue, bool newValue)
    {
        Debug.Log($"[EarthquakeAbility] OnFallingChanged: {previousValue} -> {newValue}, IsOwner: {networkOwner.IsOwner}");
        
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
                        jumpStartPosition.x,
                        jumpStartPosition.y + jumpHeight,
                        jumpStartPosition.z
                    );
                }
            }
            
            Debug.Log($"[EarthquakeAbility] Iniciando caída para {(networkOwner.IsOwner ? "local" : "remoto")}");
        }
    }
    
    private void OnImpactPauseChanged(bool previousValue, bool newValue)
    {
        Debug.Log($"[EarthquakeAbility] OnImpactPauseChanged: {previousValue} -> {newValue}, IsOwner: {networkOwner.IsOwner}");
        
        isInImpactPause = newValue;
        
        // Informar al controlador de habilidades sobre el estado de pausa
        if (abilityController != null)
        {
            abilityController.SetInImpactPause(isInImpactPause);
        }
        
        if (isInImpactPause && !networkOwner.IsOwner)
        {
            // Definir la posición de impacto (ahora puede ser diferente de la inicial)
            Vector3 impactPosition = new Vector3(
                jumpHighestPosition.x,
                jumpStartPosition.y,
                jumpHighestPosition.z
            );
            
            // Restaurar posición para jugadores remotos
            if (jumpHighestPosition != Vector3.zero)
            {
                networkOwner.transform.position = impactPosition;
                
                // Mostrar efecto de impacto (también para no propietarios)
                if (earthquakeEffectPrefab != null)
                {
                    Instantiate(earthquakeEffectPrefab, impactPosition, Quaternion.identity);
                }
            }
            
            // Para jugadores remotos, también iniciar la pausa
            StartCoroutine(ImpactPause());
        }
        
        if (!newValue)
        {
            // Desactivar modo de sincronización manual cuando termina la pausa
            syncingTransform = false;
        }
    }
    
    private void OnJumpStartPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        Debug.Log($"[EarthquakeAbility] OnJumpStartPositionChanged: {previousValue} -> {newValue}, IsOwner: {networkOwner.IsOwner}");
        
        // Siempre actualizar localmente
        jumpStartPosition = newValue;
    }
    
    private void OnCurrentPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        // Solo actualizar la posición si estamos en modo de sincronización y no somos el propietario
        if (syncingTransform && !networkOwner.IsOwner)
        {
            networkOwner.transform.position = newValue;
        }
    }
    
    private void OnJumpDirectionChanged(Vector3 previousValue, Vector3 newValue)
    {
        Debug.Log($"[EarthquakeAbility] OnJumpDirectionChanged: {previousValue} -> {newValue}, IsOwner: {networkOwner.IsOwner}");
        jumpDirection = newValue;
    }

    private void OnJumpTargetPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        Debug.Log($"[EarthquakeAbility] OnJumpTargetPositionChanged: {previousValue} -> {newValue}, IsOwner: {networkOwner.IsOwner}");
        jumpTargetPosition = newValue;
    }
    
    // Método para verificar si se puede activar la habilidad
    public override bool CanActivate()
    {
        // No permitir activar si ya estamos en medio de un salto o caída
        if (isJumping || isFalling || isInImpactPause)
        {
            if (networkOwner.IsOwner)
            {
                Debug.Log("[EarthquakeAbility] No se puede activar: ya está en uso");
            }
            return false;
        }
        
        // IMPORTANTE: Solo verificar requisito de movimiento para el propietario local
        if (requireMovement && networkOwner.IsOwner)
        {
            // Actualizar velocidad actual
            UpdateCurrentSpeed();
            
            if (currentSpeed < minMovementSpeed)
            {
                Debug.Log($"[EarthquakeAbility] No se puede activar: velocidad insuficiente ({currentSpeed:F2} < {minMovementSpeed:F2})");
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
                Debug.Log($"[EarthquakeAbility] Velocidad actual: {currentSpeed:F2} unidades/seg");
            }
        }
    }
    
// 1. Arreglar el problema del host modificando el método Activate():

public override void Activate()
{
    Debug.Log($"[EarthquakeAbility] Método Activate llamado, isOwner: {networkOwner.IsOwner}, posición actual: {networkOwner.transform.position}");
    
    // Para el propietario (sea host o cliente), enviamos RPC y dejamos que el servidor inicie el proceso
    if (networkOwner.IsOwner)
    {
        // IMPORTANTE: Para el host (que es servidor + cliente), necesitamos iniciar el salto directamente
        if (networkOwner.IsServer)
        {
            // Si somos host, calculamos directamente la dirección y activamos la habilidad sin RPC
            SetupDirectionalJump();
            
            // Asegurarse de que el maná se consume
            if (playerStats.UseMana(manaCost))
            {
                StartJump();
                
                // Notificar a los demás clientes (no al host, que ya inició el salto)
                ActivateClientRpc(netObj.OwnerClientId, jumpDirection, jumpTargetPosition);
            }
        }
        else
        {
            // Si somos cliente normal, enviamos RPC al servidor
            ActivateServerRpc();
        }
    }
    else
    {
        // Para clientes remotos que reciben la llamada desde otros métodos, iniciar el salto directamente
        StartJump();
    }
}

// 2. Agregar este nuevo método para configurar el salto direccional correctamente:

private void SetupDirectionalJump()
{
    jumpStartPosition = networkOwner.transform.position;
    jumpDirection = Vector3.zero;
    jumpTargetPosition = jumpStartPosition;

    if (enableDirectionalJump && abilityController != null)
    {
        // Obtener posición objetivo actual (último punto de clic)
        Vector3 targetPosition = abilityController.GetTargetPosition();
        
        // Calcular vector dirección completo (incluida diagonal)
        Vector3 directionToTarget = targetPosition - jumpStartPosition;
        directionToTarget.y = 0; // Ignoramos la altura
        
        if (directionToTarget.magnitude > 0.1f)
        {
            // IMPORTANTE: Normalizar pero mantener proporción X/Z para diagonales
            jumpDirection = directionToTarget.normalized;
            
            // Calcular distancia real (limitada por el máximo)
            float actualDistance = Mathf.Min(directionToTarget.magnitude, horizontalJumpDistance);
            
            // La posición objetivo es la posición inicial más la dirección * distancia
            jumpTargetPosition = jumpStartPosition + jumpDirection * actualDistance;
            
            Debug.Log($"[EarthquakeAbility] Salto direccional configurado: Dirección={jumpDirection}, Posición objetivo={jumpTargetPosition}");
        }
    }
}

// 3. Modificar ActivateServerRpc para usar el nuevo método:

[ServerRpc(RequireOwnership = true)]
private void ActivateServerRpc()
{
    Debug.Log($"[EarthquakeAbility] ActivateServerRpc recibido, servidor notificado de activación");
    
    // Configurar el salto direccional
    SetupDirectionalJump();
    
    // El servidor verifica que se cumplen todas las condiciones
    if (CanActivate())
    {
        Debug.Log($"[EarthquakeAbility] ActivateServerRpc aprobado, notificando a todos los clientes");
        
        // Consumir maná en el servidor
        if (playerStats != null)
        {
            playerStats.UseMana(manaCost);
        }
        
        // Activar el salto en todos los clientes
        ActivateClientRpc(netObj.OwnerClientId, jumpDirection, jumpTargetPosition);
        
        // Si somos servidor pero no propietario (por ejemplo, un servidor dedicado), iniciar el salto explícitamente
        if (!networkOwner.IsOwner)
        {
            StartJump();
        }
    }
    else
    {
        Debug.Log($"[EarthquakeAbility] ActivateServerRpc rechazado, no se cumplen las condiciones");
    }
}

// 4. Simplificar ActivateClientRpc:

[ClientRpc]
private void ActivateClientRpc(ulong ownerClientId, Vector3 direction, Vector3 targetPos)
{
    Debug.Log($"[EarthquakeAbility] ActivateClientRpc: Cliente {NetworkManager.Singleton.LocalClientId} notificado. Dirección={direction}");
    
    // Si somos el host (servidor + cliente local), ignoramos este mensaje porque ya iniciamos el salto
    if (networkOwner.IsServer && networkOwner.IsOwner)
    {
        Debug.Log("[EarthquakeAbility] Host ignora ClientRpc porque ya inició el salto");
        return;
    }
    
    // Para todos los demás clientes, configurar dirección y posición objetivo
    jumpDirection = direction;
    jumpTargetPosition = targetPos;
    
    // Solo iniciar el salto si somos el propietario o un cliente remoto (no propietario)
    // El servidor no propietario ya lo inició en ActivateServerRpc
    if ((networkOwner.IsOwner && NetworkManager.Singleton.LocalClientId == ownerClientId) || 
        (!networkOwner.IsOwner && !networkOwner.IsServer))
    {
        StartJump();
    }
}

// 5. Ajustar StartJump para garantizar que la dirección se mantenga intacta:

private void StartJump()
{
    if (isJumping || isFalling) return;
    
    Debug.Log($"[EarthquakeAbility] StartJump - Iniciando salto. Posición={networkOwner.transform.position}, Dirección={jumpDirection}");
    
    // Guardar posición inicial para cálculos
    jumpStartPosition = networkOwner.transform.position;
    
    // IMPORTANTE: No recalcular la dirección o posición objetivo aquí, usar los valores ya establecidos
    
    // Desactivar física para control manual
    DisablePhysicsControllers();
    
    // Si somos el propietario, actualizar las variables de red
    if (networkOwner.IsOwner)
    {
        networkJumpStartPosition.Value = jumpStartPosition;
        networkJumpDirection.Value = jumpDirection;
        networkJumpTargetPosition.Value = jumpTargetPosition;
        networkIsJumping.Value = true;
        networkIsFalling.Value = false;
        networkIsInImpactPause.Value = false;
        
        // Forzar actualización inmediata de transform
        networkCurrentPosition.Value = jumpStartPosition;
    }
    
    // Iniciar fase de salto
    isJumping = true;
    jumpTime = 0f;
    
    // Nuevo: Notificar visualmente que se inició el salto
    if (networkOwner.IsOwner)
    {
        TriggerJumpStartVisualEffectServerRpc();
    }
}
    
    // Método para resetear completamente la habilidad
    public void ResetAbility()
    {
        if (networkOwner.IsOwner)
        {
            // Resetear variables de red
            networkIsJumping.Value = false;
            networkIsFalling.Value = false;
            networkIsInImpactPause.Value = false;
        }
        
        // Resetear variables locales
        isJumping = false;
        isFalling = false;
        isInImpactPause = false;
        jumpHighestPosition = Vector3.zero;
        jumpTime = 0f;
        syncingTransform = false;
        
        // Resetear estado de cooldown
        isReady = true;
        cooldownEndTime = 0f;
        
        // Asegurar que los controladores físicos estén habilitados
        EnablePhysicsControllers();
        
        Debug.Log($"[EarthquakeAbility] Habilidad reseteada completamente");
    }
    
    public override void UpdateAbility()
    {
        // Debug periódico (cada ~5 segundos)
        if (Time.frameCount % 300 == 0)
        {
            Debug.Log($"[EarthquakeAbility] Estado actual: isJumping={isJumping}, isFalling={isFalling}, " +
                     $"isInImpactPause={isInImpactPause}, isReady={isReady}, syncingTransform={syncingTransform}");
        }
        
        // Actualizar velocidad de movimiento cuando sea el propietario
        if (networkOwner.IsOwner)
        {
            UpdateCurrentSpeed();
        }
        
        // Actualizar lógica del salto/terremoto si está activo
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
        jumpTime += Time.deltaTime;
        
        if (isJumping)
        {
            // Fase de ascenso (con tiempo ajustable)
            if (jumpTime <= riseTime)
            {
                // Usamos una curva sinusoidal para un movimiento más natural
                float progress = jumpTime / riseTime; // Normalizado de 0 a 1
                
                // MODIFICADO: Curva de altura más pronunciada al principio y final para mayor efecto visual
                float height = jumpHeight * Mathf.Sin(progress * Mathf.PI / 2);
                
                // NUEVO: Calcular movimiento horizontal basado en la curva personalizada
                float horizontalProgress = horizontalMovementCurve.Evaluate(progress);
                
                // Calcular nueva posición combinando vertical y horizontal
                Vector3 verticalOffset = new Vector3(0, height, 0);
                Vector3 horizontalOffset = Vector3.zero;
                
                // Si hay dirección de salto, aplicar desplazamiento horizontal
                if (jumpDirection.magnitude > 0.01f)
                {
                    // MODIFICADO: Usar curva para hacer que el movimiento horizontal sea más balístico
                    horizontalOffset = (jumpTargetPosition - jumpStartPosition) * horizontalProgress;
                    
                    // Debug para verificar que esto se ejecuta en todos los clientes
                    if (Time.frameCount % 60 == 0) {
                        Debug.Log($"[EarthquakeAbility] Salto direccional progreso: {progress:F2}, dirección: {jumpDirection}, offset horizontal: {horizontalOffset.magnitude:F2}");
                    }
                }
                
                Vector3 newPosition = jumpStartPosition + verticalOffset + horizontalOffset;
                
                // Aplicar posición
                networkOwner.transform.position = newPosition;
                
                // Si somos el propietario, actualizar la posición en la red
                if (networkOwner.IsOwner)
                {
                    networkCurrentPosition.Value = newPosition;
                }
                
                // Si estamos en el punto más alto, guardarlo para referencia
                if (progress >= 0.99f)
                {
                    jumpHighestPosition = newPosition;
                }
            }
            else
            {
                // Cambiar a fase de caída
                isJumping = false;
                isFalling = true;
                
                // Si somos el propietario, actualizar variables de red
                if (networkOwner.IsOwner)
                {
                    Debug.Log("[EarthquakeAbility] Cambiando de salto a caída");
                    networkIsJumping.Value = false;
                    networkIsFalling.Value = true;
                }
                
                // Guardar la posición más alta alcanzada para iniciar la caída desde ahí
                if (jumpHighestPosition == Vector3.zero)
                {
                    // Si por alguna razón no se guardó, usamos la posición actual
                    jumpHighestPosition = networkOwner.transform.position;
                }
                
                jumpTime = 0f; // Reiniciar tiempo para la fase de caída
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
                
                // La altura al inicio de la caída debe basarse en la posición más alta
                float fallHeight = jumpHighestPosition.y;
                
                // NUEVO: Para el movimiento horizontal durante la caída, queremos mantener la posición X/Z
                // de la posición más alta pero caer verticalmente hacia la posición objetivo final
                
                // Mantener X/Z de la posición más alta durante la caída
                Vector3 newPosition = new Vector3(
                    jumpHighestPosition.x,
                    jumpStartPosition.y + (fallHeight - jumpStartPosition.y) * heightFactor,
                    jumpHighestPosition.z
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
                    // Nos acercamos al final, asegurarnos de que terminaremos en la posición X/Z final
                    // pero con la altura inicial más un pequeño offset
                    Vector3 finalPosition = new Vector3(
                        jumpHighestPosition.x,
                        jumpStartPosition.y + 0.01f, // Pequeño offset para evitar problemas de física
                        jumpHighestPosition.z
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
                // El salto ha terminado, producir efecto de terremoto
                isFalling = false;
                isInImpactPause = true;
                
                // Si somos el propietario, actualizar variables de red
                if (networkOwner.IsOwner)
                {
                    Debug.Log("[EarthquakeAbility] Cambiando de caída a impacto");
                    networkIsFalling.Value = false;
                    networkIsInImpactPause.Value = true;
                }
                
                // Restaurar posición exactamente a la final (X/Z de la posición más alta, Y de la inicial)
                Vector3 impactPosition = new Vector3(
                    jumpHighestPosition.x,
                    jumpStartPosition.y,
                    jumpHighestPosition.z
                );
                
                networkOwner.transform.position = impactPosition;
                
                // Si somos el propietario, actualizar la posición en la red
                if (networkOwner.IsOwner)
                {
                    networkCurrentPosition.Value = impactPosition;
                }
                
                // Configurar Rigidbody para la pausa
                if (rb != null)
                {
                    rb.useGravity = true;
                    rb.velocity = Vector3.zero; // Detener al impactar
                    rb.isKinematic = true; // Evitar que fuerzas externas lo muevan durante la pausa
                }
                
                // Solo el propietario debe triggerear el efecto en el servidor
                if (networkOwner.IsOwner)
                {
                    // Activar efecto de terremoto
                    TriggerEarthquakeEffectServerRpc();
                }
                
                // Iniciar la pausa después del impacto
                StartCoroutine(ImpactPause());
            }
        }
    }
    
private IEnumerator ImpactPause()
    {
        // Asegurarse de que estamos en modo pausa
        isInImpactPause = true;
        
        // NUEVO: La posición final ahora es diferente si hubo movimiento horizontal
        Vector3 finalPosition = new Vector3(
            jumpHighestPosition.x,
            jumpStartPosition.y,
            jumpHighestPosition.z
        );
        
        // Informar al controlador de habilidades sobre el estado de pausa si existe
        if (abilityController != null)
        {
            abilityController.SetInImpactPause(true);
        }
        
        Debug.Log($"[EarthquakeAbility] Iniciando pausa de impacto - {(networkOwner.IsOwner ? "PROPIETARIO" : "NO PROPIETARIO")}");
        
        // Esperar el tiempo de pausa exacto
        yield return new WaitForSeconds(impactPauseTime);
        
        Debug.Log($"[EarthquakeAbility] Finalizando pausa de impacto - {(networkOwner.IsOwner ? "PROPIETARIO" : "NO PROPIETARIO")}");
        
        if (networkOwner.IsOwner)
        {
            // Solo el propietario debe actualizar la variable de red
            networkIsInImpactPause.Value = false;
        }
        
        // Restaurar estado después de la pausa
        isInImpactPause = false;
        syncingTransform = false;
        
        // Informar al controlador de habilidades que la pausa ha terminado
        if (abilityController != null)
        {
            abilityController.SetInImpactPause(false);
        }
        
        // Restaurar controladores y física
        EnablePhysicsControllers();
        
        // Resetear las variables de salto
        jumpHighestPosition = Vector3.zero;
    }
    
    // Método para efectos visuales al inicio del salto
    [ServerRpc(RequireOwnership = false)]
    private void TriggerJumpStartVisualEffectServerRpc()
    {
        // Enviar a todos los clientes
        TriggerJumpStartVisualEffectClientRpc(networkOwner.transform.position);
    }
    
    [ClientRpc]
    private void TriggerJumpStartVisualEffectClientRpc(Vector3 position)
    {
        Debug.Log($"[EarthquakeAbility] Mostrando efecto visual de inicio de salto en posición {position}");
        
        // Efecto de partículas pequeño para indicar el inicio del salto
        // Este es opcional, puedes implementar un efecto visual sencillo aquí
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void TriggerEarthquakeEffectServerRpc()
    {
        Debug.Log("[EarthquakeAbility] ServerRpc para efecto de terremoto llamado");
        
        // Sincronizar el efecto de terremoto en todos los clientes
        TriggerEarthquakeEffectClientRpc(networkOwner.transform.position);
    }
    
    [ClientRpc]
    private void TriggerEarthquakeEffectClientRpc(Vector3 position)
    {
        Debug.Log($"[EarthquakeAbility] ClientRpc para mostrar efecto visual en posición {position}");
        
        // Mostrar efecto visual
        if (earthquakeEffectPrefab != null)
        {
            GameObject effect = Instantiate(earthquakeEffectPrefab, position, Quaternion.identity);
            
            // Escalar el efecto según el radio
            effect.transform.localScale = new Vector3(earthquakeRadius * 2, 1, earthquakeRadius * 2);
            
            // Destruir después de unos segundos
            Destroy(effect, 3.0f);
            
            Debug.Log($"[EarthquakeAbility] Efecto visual instanciado con éxito en {position}");
        }
        else
        {
            Debug.LogWarning("[EarthquakeAbility] earthquakeEffectPrefab es null, no se puede mostrar efecto visual");
        }
        
        // Aplicar cámara shake si estamos en el cliente dueño
        if (networkOwner.IsOwner)
        {
            Debug.Log("[EarthquakeAbility] Aplicando efectos adicionales para el propietario");
            // Aquí podrías añadir un efecto de sacudida de cámara si tienes uno implementado
        }
        
        // Aplicar efecto de física y daño solo en el servidor
        if (networkOwner.IsServer)
        {
            ApplyEarthquakeEffects(position);
        }
    }
    
    private void ApplyEarthquakeEffects(Vector3 position)
    {
        Debug.Log($"[EarthquakeAbility] Aplicando efectos físicos en posición {position}");
        
        // Buscar objetos en un radio
        Collider[] hitColliders = Physics.OverlapSphere(position, earthquakeRadius, affectedLayers);
        
        int affectedObjects = 0;
        foreach (var hit in hitColliders)
        {
            // Ignorar a nosotros mismos
            if (hit.transform == networkOwner.transform)
                continue;
                
            // Aplicar fuerza de empuje si tiene Rigidbody
            Rigidbody targetRb = hit.GetComponent<Rigidbody>();
            if (targetRb != null)
            {
                Vector3 direction = (hit.transform.position - position).normalized;
                direction.y = 0.5f; // Agregar componente hacia arriba para que sea más interesante
                
                // Aplicar fuerza explosiva
                targetRb.AddForce(direction * earthquakeForce, ForceMode.Impulse);
                affectedObjects++;
            }
        }
        
        Debug.Log($"[EarthquakeAbility] Afectados {affectedObjects} objetos con física");
    }
    
    // Propiedades públicas para que otros sistemas puedan consultar el estado
    public bool IsJumping => isJumping;
    public bool IsFalling => isFalling;
    public bool IsInImpactPause => isInImpactPause;
    public float CurrentSpeed => currentSpeed; // Exposición de la velocidad actual
    
    // Método público para verificar si se está moviendo lo suficientemente rápido
    public bool IsMovingFastEnough()
    {
        return currentSpeed >= minMovementSpeed;
    }
}
}