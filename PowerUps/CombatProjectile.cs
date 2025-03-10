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
    private bool hasHit = false;
    
    // Variables para dirección local
    private Vector3 localMoveDirection;
    private bool directionInitialized = false;
    private float creationTime;
    
    void Start()
    {
        // Guardar tiempo de creación
        creationTime = Time.time;
        
        // Programar destrucción automática después del tiempo máximo de vida
        Destroy(gameObject, maxLifetime);
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Guardar posición inicial
        startPosition = transform.position;
        
        // Inicializar orientación del proyectil
        if (networkDirection.Value != Vector3.zero)
        {
            transform.forward = networkDirection.Value;
        }
        
        // Buscar referencia al objetivo si es un proyectil homing
        if (homing && networkTargetId.Value != ulong.MaxValue)
        {
            FindTargetObject();
        }
        
        // Iniciar sistemas de partículas si existen
        if (particleSystem != null)
        {
            particleSystem.Play();
        }
        
        // En el servidor, programar destrucción automática
        if (IsServer)
        {
            Invoke("DestroyProjectile", maxLifetime);
        }
    }
    
    private void FindTargetObject()
    {
        // Usar el método para buscar el NetworkObject sin depender de ConnectedClients
        targetObject = FindNetworkObjectByOwnerClientId(networkTargetId.Value);
    }
    
    private NetworkObject FindNetworkObjectByOwnerClientId(ulong clientId)
    {
        // Solo el servidor puede usar ConnectedClients
        if (IsServer && NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            {
                return client.PlayerObject;
            }
        }
        
        // Para clientes (o si no lo encontramos en ConnectedClients)
        // Buscamos entre todos los NetworkObjects de la escena
        NetworkObject[] networkObjects = FindObjectsOfType<NetworkObject>();
        foreach (var netObj in networkObjects)
        {
            if (netObj.OwnerClientId == clientId)
            {
                return netObj;
            }
        }
        
        return null;
    }
    
    private void Update()
    {
        // Si ya golpeó a algo, no hacer nada
        if (hasHit) return;
        
        // Verificar tiempo de vida
        if (Time.time - creationTime > maxLifetime && IsServer)
        {
            DestroyProjectile();
            return;
        }
        
        // Usar dirección local en lugar de la variable de red
        Vector3 moveDirection = directionInitialized ? localMoveDirection : transform.forward;
        
        // Mover el proyectil
        transform.position += moveDirection * speed * Time.deltaTime;
        transform.forward = moveDirection; // Mantener la orientación
        
        // Actualizar distancia recorrida
        distanceTraveled += speed * Time.deltaTime;
        
        // Destruir si ha recorrido la distancia máxima
        if (distanceTraveled >= maxDistance && IsServer)
        {
            DestroyProjectile();
        }
    }
    
    private void OnTriggerEnter(Collider other)
    {
        // Si ya golpeó algo o no somos el servidor, no hacer nada
        if (hasHit || !IsServer) return;
        
        // Verificar si es un jugador y no es el lanzador
        NetworkObject hitNetObj = other.GetComponent<NetworkObject>();
        if (hitNetObj != null && hitNetObj.OwnerClientId != networkOwnerId.Value)
        {
            // Verificar si tiene estadísticas de jugador
            PlayerStats targetStats = hitNetObj.GetComponent<PlayerStats>();
            if (targetStats != null)
            {
                // Marcar como golpeado para evitar golpear varias veces
                hasHit = true;
                
                // Aplicar daño
                targetStats.TakeDamage(damage);
                
                // Mostrar efecto de impacto
                Vector3 impactPosition = transform.position;
                SpawnImpactEffectClientRpc(impactPosition);
                
                // Destruir el proyectil después de un breve retraso para que se vea el efecto
                StartCoroutine(DestroyAfterEffect(0.1f));
            }
        }
    }
    
    // MÉTODO PRINCIPAL PARA DESTRUIR EL PROYECTIL
    private void DestroyProjectile()
    {
        // Si ya está marcado como golpeado, no hacer nada más
        if (hasHit) return;
        
        if (IsServer)
        {
            // 1. Avisar a los clientes para que muestren efectos de desaparición
            DestroyVisualEffectsClientRpc();
            
            // 2. Despawnear de la red
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn();
            }
            
            // 3. IMPORTANTE: Destruir el GameObject explícitamente
            // Esto destruirá tanto el objeto padre como todos sus hijos
            Destroy(gameObject);
        }
    }
    
    [ClientRpc]
    private void DestroyVisualEffectsClientRpc()
    {
        // Detener emisión de partículas
        if (particleSystem != null)
        {
            var emission = particleSystem.emission;
            emission.enabled = false;
        }
        
        // Detener el trail si existe
        if (trailRenderer != null)
        {
            trailRenderer.emitting = false;
        }
    }
    
    [ClientRpc]
    private void SpawnImpactEffectClientRpc(Vector3 position)
    {
        // Detener emisión de partículas
        if (particleSystem != null)
        {
            var emission = particleSystem.emission;
            emission.enabled = false;
        }
        
        // Detener el trail si existe
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
    
    private IEnumerator DestroyAfterEffect(float delay)
    {
        // Desactivar el collider inmediatamente para evitar múltiples colisiones
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        
        // Detener emisión de partículas
        if (particleSystem != null)
        {
            var emission = particleSystem.emission;
            emission.enabled = false;
        }
        
        // Detener el trail si existe
        if (trailRenderer != null)
        {
            trailRenderer.emitting = false;
        }
        
        // Esperar un breve momento para que se vea el efecto
        yield return new WaitForSeconds(delay);
        
        // Destruir en la red
        if (IsServer)
        {
            DestroyProjectile();
        }
    }
    
    public void Initialize(Vector3 direction, float damage, ulong ownerId, NetworkObject target = null)
    {
        // Guardar la dirección localmente para usarla independientemente de la red
        localMoveDirection = direction.normalized;
        directionInitialized = true;
        
        if (IsServer)
        {
            // También establecer variables de red para compatibilidad
            networkDirection.Value = direction.normalized;
            this.damage = damage;
            networkOwnerId.Value = ownerId;
            
            if (target != null)
            {
                networkTargetId.Value = target.OwnerClientId;
            }
        }
        
        // Orientar el proyectil inmediatamente, sin esperar a OnNetworkSpawn
        transform.forward = direction.normalized;
    }
    
    // MÉTODO DE LIMPIEZA AL DESTRUIR
    private void OnDestroy()
    {
        // Cancelar cualquier invocación pendiente
        CancelInvoke();
        
        // Detener todas las corrutinas
        StopAllCoroutines();
        
        // Para debugging
        Debug.Log($"Proyectil destruido: {gameObject.name}");
    }
    
    // Método estático para crear un proyectil desde el servidor
    public static CombatProjectile SpawnProjectile(GameObject prefab, Vector3 position, Vector3 direction, 
                                             float damage, ulong ownerId, NetworkObject target = null)
    {
        // Solo el servidor puede spawner objetos en red
        if (!NetworkManager.Singleton.IsServer) return null;
        
        // IMPORTANTE: Crear el proyectil con la rotación específica hacia la dirección
        GameObject projectileObject = Instantiate(prefab, position, Quaternion.LookRotation(direction));
        
        // Obtener el componente de red
        NetworkObject netObj = projectileObject.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError("El prefab del proyectil no tiene NetworkObject!");
            Destroy(projectileObject);
            return null;
        }
        
        // Spawner en la red
        netObj.Spawn();
        
        // Inicializar el proyectil
        CombatProjectile projectile = projectileObject.GetComponent<CombatProjectile>();
        if (projectile != null)
        {
            projectile.Initialize(direction, damage, ownerId, target);
        }
        
        return projectile;
    }
}