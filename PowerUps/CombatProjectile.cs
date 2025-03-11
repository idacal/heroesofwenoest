using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class CombatProjectile : NetworkBehaviour
{
    [Header("Propiedades del Proyectil")]
    [SerializeField] private float speed = 20f;
    [SerializeField] private float maxDistance = 30f;
    [SerializeField] private bool homing = false;
    [SerializeField] private float homingStrength = 5f;
    [SerializeField] private float damage = 2f;
    
    [Header("Tiempo de Vida")]
    [SerializeField] private float maxLifetime = 3f;
    
    [Header("Efectos Visuales")]
    [SerializeField] private GameObject impactEffectPrefab;
    [SerializeField] private TrailRenderer trailRenderer;
    [SerializeField] private ParticleSystem particleSystem;
    
    // Variables de red para sincronización
    private NetworkVariable<Vector3> networkDirection = new NetworkVariable<Vector3>(
        Vector3.forward, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    private NetworkVariable<ulong> networkOwnerId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    private NetworkVariable<ulong> networkTargetId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Referencias internas
    private Vector3 startPosition;
    private NetworkObject targetObject;
    private float distanceTraveled = 0f;
    private NetworkVariable<bool> hasHit = new NetworkVariable<bool>(false, 
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Variables para dirección local
    private Vector3 localMoveDirection;
    private bool directionInitialized = false;
    private float creationTime;
    
    void Start()
    {
        // Guardar tiempo de creación
        creationTime = Time.time;
        
        // IMPORTANTE: Solo el servidor debe programar la destrucción automática
        if (IsServer)
        {
            Invoke("ServerDestroyProjectile", maxLifetime);
        }
        
        Debug.Log($"[Proyectil] Start llamado - ID: {NetworkObjectId}, IsServer: {IsServer}");
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Guardar posición inicial
        startPosition = transform.position;
        
        // Suscribirse a cambios en networkDirection
        networkDirection.OnValueChanged += OnDirectionChanged;
        
        // Debugging
        Debug.Log($"[Proyectil] OnNetworkSpawn - ID: {NetworkObjectId}, Direction: {networkDirection.Value}");
        
        // Inicializar localmente con la dirección de red
        UpdateLocalDirection(networkDirection.Value);
        
        // Buscar target si es homing
        if (homing && networkTargetId.Value != ulong.MaxValue)
        {
            FindTargetObject();
        }
        
        // Iniciar efectos visuales
        if (particleSystem != null)
        {
            particleSystem.Play();
        }
    }
    
    private void OnDirectionChanged(Vector3 oldValue, Vector3 newValue)
    {
        UpdateLocalDirection(newValue);
        Debug.Log($"[Proyectil] Dirección cambió: {oldValue} -> {newValue}");
    }
    
    private void UpdateLocalDirection(Vector3 dir)
    {
        if (dir.magnitude > 0.01f)
        {
            localMoveDirection = dir.normalized;
            directionInitialized = true;
            transform.forward = localMoveDirection;
        }
        else
        {
            Debug.LogWarning($"[Proyectil] Intento de establecer dirección inválida: {dir}");
        }
    }
    
    private void FindTargetObject()
    {
        if (networkTargetId.Value == ulong.MaxValue) return;
        
        // Buscar NetworkObject por ID de cliente
        NetworkObject[] networkObjects = FindObjectsOfType<NetworkObject>();
        foreach (var netObj in networkObjects)
        {
            if (netObj.OwnerClientId == networkTargetId.Value)
            {
                targetObject = netObj;
                return;
            }
        }
    }
    
    private void Update()
    {
        // No procesar si ya ha colisionado
        if (hasHit.Value) return;
        
        // Verificar tiempo de vida
        if (Time.time - creationTime > maxLifetime)
        {
            // Solo el servidor puede destruir
            if (IsServer)
            {
                ServerDestroyProjectile();
            }
            return;
        }
        
        // Encontrar la dirección de movimiento válida
        Vector3 moveDirection;
        
        if (directionInitialized && localMoveDirection.magnitude > 0.01f)
        {
            moveDirection = localMoveDirection;
        }
        else if (networkDirection.Value.magnitude > 0.01f)
        {
            moveDirection = networkDirection.Value;
            UpdateLocalDirection(moveDirection);
        }
        else
        {
            moveDirection = transform.forward;
        }
        
        // Mover el proyectil
        transform.position += moveDirection * speed * Time.deltaTime;
        transform.forward = moveDirection;
        
        // Actualizar distancia
        distanceTraveled += speed * Time.deltaTime;
        
        // Verificar distancia máxima (solo el servidor)
        if (distanceTraveled >= maxDistance && IsServer)
        {
            ServerDestroyProjectile();
        }
    }
    
    private void OnTriggerEnter(Collider other)
    {
        // CRUCIAL: Solo el servidor procesa colisiones
        if (!IsServer || hasHit.Value) return;
        
        // Log detallado para TODAS las colisiones
        Debug.Log($"[Proyectil] Colisión con: {other.name}, tag: {other.tag}, " +
                $"layer: {LayerMask.LayerToName(other.gameObject.layer)}, " +
                $"isServer: {IsServer}, " +
                $"ID: {NetworkObjectId}");
        
        // Verificar si es un jugador válido para colisionar
        NetworkObject hitNetObj = other.GetComponent<NetworkObject>();
        if (hitNetObj != null)
        {
            // Verificar si es un jugador distinto al emisor
            if (hitNetObj.OwnerClientId != networkOwnerId.Value)
            {
                PlayerStats targetStats = hitNetObj.GetComponent<PlayerStats>();
                if (targetStats != null)
                {
                    // Marcar como golpeado
                    hasHit.Value = true;
                    
                    Debug.Log($"[Proyectil] ¡IMPACTO VÁLIDO! Jugador {hitNetObj.OwnerClientId}. Aplicando {damage} daño");
                    
                    // Aplicar daño
                    targetStats.TakeDamage(damage);
                    
                    // NUEVO: Aplicar repulsión física al jugador impactado
                    ApplyImpactRepulsionToPlayer(hitNetObj);
                    
                    // Mostrar efecto y destruir
                    Vector3 impactPosition = transform.position;
                    SpawnImpactEffectClientRpc(impactPosition);
                    
                    // Destruir con retraso para mostrar efectos
                    StartCoroutine(DelayedServerDestroy(0.1f));
                }
                else
                {
                    Debug.Log($"[Proyectil] El objeto tiene NetworkObject pero no PlayerStats: {other.name}");
                }
            }
            else
            {
                Debug.Log($"[Proyectil] Ignorando colisión con el emisor del proyectil");
            }
        }
        else
        {
            Debug.Log($"[Proyectil] El objeto no tiene NetworkObject: {other.name}");
        }
    }
    
    // NUEVO: Método para aplicar repulsión física al jugador impactado
    private void ApplyImpactRepulsionToPlayer(NetworkObject targetPlayer)
    {
        // Calcular dirección de repulsión (desde el proyectil hacia el jugador)
        Vector3 repulsionDirection = (targetPlayer.transform.position - transform.position).normalized;
        
        // Calcular fuerza en función del daño (esto puede ajustarse)
        float repulsionForce = damage * 1.5f; // Escalar fuerza según el daño
        
        // Aplicar vector de fuerza
        Vector3 impactForce = repulsionDirection * repulsionForce;
        
        // Obtener el componente PlayerNetwork y aplicar la repulsión
        PlayerNetwork playerNetwork = targetPlayer.GetComponent<PlayerNetwork>();
        if (playerNetwork != null)
        {
            // Llamar al nuevo método RPC para aplicar repulsión
            playerNetwork.ApplyImpactForceServerRpc(impactForce);
        }
    }
    
    // IMPORTANTE: Este método solo debe llamarse desde el servidor
    private void ServerDestroyProjectile()
    {
        if (!IsServer)
        {
            Debug.LogError("[Proyectil] Intento de destruir desde un cliente. Esto no está permitido.");
            return;
        }
        
        // Si ya fue marcado como destruido, no hacer nada
        if (this == null || !IsSpawned) return;
        
        Debug.Log($"[Proyectil] Destruyendo desde servidor - ID: {NetworkObjectId}");
        
        // 1. Notificar a los clientes para efectos visuales
        DestroyVisualEffectsClientRpc();
        
        // 2. Despawnear en la red
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }
    
    // Cliente puede solicitar destrucción al servidor
    [ServerRpc(RequireOwnership = false)]
    public void RequestDestroyServerRpc()
    {
        // Solo procesar si no ha sido destruido
        if (!hasHit.Value)
        {
            ServerDestroyProjectile();
        }
    }
    
    [ClientRpc]
    private void DestroyVisualEffectsClientRpc()
    {
        // Detener efectos visuales
        if (particleSystem != null)
        {
            var emission = particleSystem.emission;
            emission.enabled = false;
        }
        
        if (trailRenderer != null)
        {
            trailRenderer.emitting = false;
        }
    }
    
    [ClientRpc]
    private void SpawnImpactEffectClientRpc(Vector3 position)
    {
        // Detener efectos visuales
        if (particleSystem != null)
        {
            var emission = particleSystem.emission;
            emission.enabled = false;
        }
        
        if (trailRenderer != null)
        {
            trailRenderer.emitting = false;
        }
        
        // Instanciar efecto de impacto
        if (impactEffectPrefab != null)
        {
            Instantiate(impactEffectPrefab, position, Quaternion.identity);
        }
    }
    
    private IEnumerator DelayedServerDestroy(float delay)
    {
        // Solo ejecutar en el servidor
        if (!IsServer) yield break;
        
        // Desactivar collider inmediatamente
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        
        // Esperar un momento
        yield return new WaitForSeconds(delay);
        
        // Destruir
        ServerDestroyProjectile();
    }
    
    public void Initialize(Vector3 direction, float damage, ulong ownerId, NetworkObject target = null)
    {
        // Validar dirección
        if (direction.magnitude < 0.01f)
        {
            Debug.LogError("[Proyectil] Dirección inválida. Usando dirección por defecto.");
            direction = Vector3.forward;
        }
        
        // Actualizar localmente
        localMoveDirection = direction.normalized;
        directionInitialized = true;
        transform.forward = direction.normalized;
        
        Debug.Log($"[Proyectil] Initialize - Dirección: {direction}, ID: {(NetworkObject?.NetworkObjectId ?? 0)}");
        
        // Solo servidor actualiza las NetworkVariables
        if (IsServer)
        {
            networkDirection.Value = direction.normalized;
            this.damage = damage;
            networkOwnerId.Value = ownerId;
            
            if (target != null)
            {
                networkTargetId.Value = target.OwnerClientId;
            }
        }
    }
    
    [ClientRpc]
    public void SyncDirectionClientRpc(Vector3 direction)
    {
        Debug.Log($"[Proyectil] SyncDirectionClientRpc - Dirección: {direction}, ID: {NetworkObjectId}");
        UpdateLocalDirection(direction);
    }
    
    public override void OnDestroy()
    {
        base.OnDestroy();
        
        // Limpiar recursos
        CancelInvoke();
        StopAllCoroutines();
        
        // Desuscribir de eventos (si todavía está spawneado)
        if (IsSpawned)
        {
            try
            {
                networkDirection.OnValueChanged -= OnDirectionChanged;
            }
            catch { /* Ignorar errores durante destrucción */ }
        }
    }
    
    // Método estático para crear un proyectil
    public static CombatProjectile SpawnProjectile(GameObject prefab, Vector3 position, Vector3 direction, 
                                         float damage, ulong ownerId, NetworkObject target = null)
    {
        // Solo el servidor puede spawner objetos
        if (!NetworkManager.Singleton.IsServer) return null;
        
        // Validar dirección
        if (direction.magnitude < 0.01f)
        {
            Debug.LogError("[Proyectil] Error al crear proyectil: dirección inválida");
            direction = Vector3.forward;
        }
        
        // Crear proyectil con la rotación correcta
        GameObject projectileObject = Instantiate(prefab, position, Quaternion.LookRotation(direction));
        
        // Obtener NetworkObject
        NetworkObject netObj = projectileObject.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError("El prefab del proyectil no tiene NetworkObject!");
            Destroy(projectileObject);
            return null;
        }
        
        Debug.Log($"[Proyectil] Spawneando proyectil en {position}, dirección: {direction}");
        
        // Asegurarse de que tenga los componentes correctos
        CombatProjectile projectile = projectileObject.GetComponent<CombatProjectile>();
        if (projectile == null)
        {
            projectile = projectileObject.AddComponent<CombatProjectile>();
        }
        
        // Asegurarse de que tenga un collider como trigger
        SphereCollider collider = projectileObject.GetComponent<SphereCollider>();
        if (collider == null)
        {
            collider = projectileObject.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 0.5f;
        }
        else
        {
            collider.isTrigger = true;
        }
        
        // Spawner en la red
        netObj.Spawn();
        
        // Inicializar
        projectile.Initialize(direction, damage, ownerId, target);
        
        // Enviar dirección explícitamente a todos los clientes
        projectile.SyncDirectionClientRpc(direction);
        
        return projectile;
    }
}