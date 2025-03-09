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
    
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();
    
    // Guardar posición inicial
    startPosition = transform.position;
    
    // Inicializar orientación del proyectil
    if (networkDirection.Value != Vector3.zero)
    {
        Debug.Log($"Proyectil: Orientando hacia {networkDirection.Value} al spawnearse");
        transform.forward = networkDirection.Value;
    }
    else
    {
        Debug.LogError("Proyectil: networkDirection es cero al spawnearse!");
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
    
    // Usar dirección local en lugar de la variable de red
    Vector3 moveDirection = directionInitialized ? localMoveDirection : transform.forward;
    
    // Depuración periódica
    if (Time.frameCount % 60 == 0)
    {
        Debug.Log($"Proyectil en {transform.position}, usando dirección {moveDirection}");
    }
    
    // Eliminar casi todo el código de homing, dejando solo esto:
    
    // Mover el proyectil
    transform.position += moveDirection * speed * Time.deltaTime;
    transform.forward = moveDirection; // Mantener la orientación
    
    // Actualizar distancia recorrida
    distanceTraveled += speed * Time.deltaTime;
    
    // Destruir si ha recorrido la distancia máxima
    if (distanceTraveled >= maxDistance)
    {
        if (IsServer)
        {
            // Destruir en la red
            NetworkObject.Despawn(true);
        }
    }
}
    
private void OnTriggerEnter(Collider other)
{
    // Si ya golpeó algo o no somos el servidor, no hacer nada
    if (hasHit || !IsServer) return;
    
    Debug.Log($"Proyectil colisionó con: {other.gameObject.name}");
    
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
            
            Debug.Log($"¡Impacto exitoso! Aplicando {damage} de daño a jugador {hitNetObj.OwnerClientId}");
            
            // Aplicar daño
            targetStats.TakeDamage(damage);
            
            // Mostrar efecto de impacto
            Vector3 impactPosition = transform.position;
            SpawnImpactEffectClientRpc(impactPosition);
            
            // Destruir el proyectil después de un breve retraso para que se vea el efecto
            StartCoroutine(DestroyAfterEffect(0.1f));
        }
        else
        {
            Debug.Log("El objeto golpeado no tiene PlayerStats");
        }
    }
    else
    {
        Debug.Log("El objeto golpeado no es un jugador válido o es el lanzador");
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
    
    Debug.Log("Destruyendo proyectil después de impacto");
    
    // Destruir en la red
    if (IsServer)
    {
        NetworkObject.Despawn(true);
    }
}
    
    // Métodos para inicializar el proyectil// Agrega estos campos al inicio de la clase CombatProjectile
private Vector3 localMoveDirection;
private bool directionInitialized = false;

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
            // Comentamos esta línea para evitar el homing
            // homing = true;
        }
    }
    
    // Orientar el proyectil inmediatamente, sin esperar a OnNetworkSpawn
    transform.forward = direction.normalized;
    
    Debug.Log($"Proyectil inicializado con dirección: {direction.normalized}, " +
              $"forward ahora es: {transform.forward}");
}
    
    // Método estático para crear un proyectil desde el servidor
    public static CombatProjectile SpawnProjectile(GameObject prefab, Vector3 position, Vector3 direction, 
                                             float damage, ulong ownerId, NetworkObject target = null)
{
    // Solo el servidor puede spawner objetos en red
    if (!NetworkManager.Singleton.IsServer) return null;
    
    // IMPORTANTE: Crear el proyectil con la rotación específica hacia la dirección
    GameObject projectileObject = Instantiate(prefab, position, Quaternion.LookRotation(direction));
    
    // Imprimir información de depuración
    Debug.Log($"Proyectil creado en {position}, rotación: {Quaternion.LookRotation(direction)}, " +
              $"dirección: {direction}, forward: {projectileObject.transform.forward}");
    
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