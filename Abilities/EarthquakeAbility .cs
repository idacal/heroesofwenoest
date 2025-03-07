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

    // Estado del terremoto - NetworkVariables para sincronización estricta
    private NetworkVariable<bool> networkIsJumping = new NetworkVariable<bool>(false, 
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> networkIsFalling = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> networkIsInImpactPause = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Vector3> networkJumpStartPosition = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    
    // Variables locales para seguimiento de animación
    private bool isJumping = false;
    private bool isFalling = false;
    private bool isInImpactPause = false;
    private Vector3 jumpStartPosition;
    private Vector3 jumpHighestPosition;
    private float jumpTime = 0f;
    
    // Control de posición manual
    private NetworkVariable<Vector3> networkCurrentPosition = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private bool syncingTransform = false;
    
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
        
        // Establecer posición inicial
        if (networkOwner.IsOwner)
        {
            networkCurrentPosition.Value = networkOwner.transform.position;
        }
        
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
            // Restaurar posición exactamente a la inicial para jugadores remotos
            if (jumpStartPosition != Vector3.zero)
            {
                networkOwner.transform.position = jumpStartPosition;
                
                // Mostrar efecto de impacto (también para no propietarios)
                if (earthquakeEffectPrefab != null)
                {
                    Instantiate(earthquakeEffectPrefab, jumpStartPosition, Quaternion.identity);
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
    
    public override bool CanActivate()
    {
        // No permitir activar si ya estamos en medio de un salto o caída
        if (isJumping || isFalling || isInImpactPause)
        {
            return false;
        }
        
        return isReady && playerStats.CurrentMana >= manaCost;
    }
    
    public override void Activate()
    {
        Debug.Log($"[EarthquakeAbility] Método Activate llamado, isOwner: {networkOwner.IsOwner}, posición actual: {networkOwner.transform.position}");
        
        // Enviar evento de activación explícito (crucialmente importante para sincronización)
        if (networkOwner.IsOwner)
        {
            ActivateServerRpc();
        }
        
        StartJump();
    }
    
    [ServerRpc(RequireOwnership = true)]
    private void ActivateServerRpc()
    {
        // Nota: este RPC solo se usa para asegurar que el servidor registre la activación
        Debug.Log($"[EarthquakeAbility] ActivateServerRpc recibido, servidor notificado de activación");
        
        // Notificar a todos los clientes que alguien activó la habilidad
        ActivateClientRpc(netObj.OwnerClientId);
    }
    
    [ClientRpc]
    private void ActivateClientRpc(ulong ownerClientId)
    {
        // Evitar que el propietario lo procese dos veces
        if (netObj.OwnerClientId == ownerClientId && NetworkManager.Singleton.LocalClientId != ownerClientId)
        {
            Debug.Log($"[EarthquakeAbility] ActivateClientRpc: Cliente {NetworkManager.Singleton.LocalClientId} notificado que cliente {ownerClientId} activó la habilidad");
            
            // Los clientes no-propietarios no necesitan hacer nada aquí
            // Los estados se sincronizan a través de NetworkVariables
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
    
    private void StartJump()
    {
        if (isJumping || isFalling) return;
        
        Debug.Log($"[EarthquakeAbility] StartJump - Iniciando salto en {(networkOwner.IsOwner ? "PROPIETARIO" : "NO PROPIETARIO")}");
        
        // Guardar posición inicial para cálculos
        jumpStartPosition = networkOwner.transform.position;
        
        // Desactivar física para control manual
        DisablePhysicsControllers();
        
        // Si somos el propietario, actualizar las variables de red
        if (networkOwner.IsOwner)
        {
            Debug.Log($"[EarthquakeAbility] Propietario actualizando variables de red, posición inicial: {jumpStartPosition}");
            networkJumpStartPosition.Value = jumpStartPosition;
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
                float height = jumpHeight * Mathf.Sin(progress * Mathf.PI / 2);
                
                // Calcular nueva posición
                Vector3 newPosition = new Vector3(
                    jumpStartPosition.x,
                    jumpStartPosition.y + height,
                    jumpStartPosition.z
                );
                
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
                
                // La altura al inicio de la caída debe ser jumpHeight
                float fallDistance = jumpHighestPosition.y - jumpStartPosition.y;
                
                // Calcular nueva posición (descendiendo desde altura máxima)
                Vector3 newPosition = new Vector3(
                    jumpStartPosition.x,
                    jumpStartPosition.y + (fallDistance * heightFactor),
                    jumpStartPosition.z
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
                    // Nos acercamos al final, asegurarnos de que terminaremos en la posición inicial
                    Vector3 finalPosition = new Vector3(
                        jumpStartPosition.x,
                        jumpStartPosition.y + 0.01f, // Pequeño offset para evitar problemas de física
                        jumpStartPosition.z
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
                
                // Restaurar posición exactamente a la inicial
                networkOwner.transform.position = jumpStartPosition;
                
                // Si somos el propietario, actualizar la posición en la red
                if (networkOwner.IsOwner)
                {
                    networkCurrentPosition.Value = jumpStartPosition;
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
}
}