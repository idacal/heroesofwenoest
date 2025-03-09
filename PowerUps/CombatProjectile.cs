using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class CombatProjectile : NetworkBehaviour
{
    [Header("Propiedades del Proyectil")]
    [SerializeField] private float speed = 15f;
    [SerializeField] private float maxDistance = 30f;
    [SerializeField] private bool homing = false;
    [SerializeField] private float homingStrength = 5f;
    [SerializeField] private float damage = 30f;
    
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
        
        Vector3 moveDirection = networkDirection.Value;
        
        // Actualizar dirección si es homing y tiene objetivo
        if (homing && targetObject != null)
        {
            Vector3 directionToTarget = (targetObject.transform.position - transform.position).normalized;
            moveDirection = Vector3.Lerp(moveDirection, directionToTarget, Time.deltaTime * homingStrength);
            moveDirection.Normalize();
            
            // Actualizar dirección en el servidor
            if (IsServer)
            {
                networkDirection.Value = moveDirection;
            }
        }
        
        // Mover el proyectil
        transform.position += moveDirection * speed * Time.deltaTime;
        transform.forward = moveDirection;
        
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
        
        // Verificar si es un jugador y no es el lanzador
        NetworkObject hitNetObj = other.GetComponent<NetworkObject>();
        if (hitNetObj != null && hitNetObj.OwnerClientId != networkOwnerId.Value)
        {
            // Verificar si tiene estadísticas de jugador
            PlayerStats targetStats = hitNetObj.GetComponent<PlayerStats>();
            if (targetStats != null)
            {
                // Marcar como golpeado
                hasHit = true;
                
                // Aplicar daño
                targetStats.TakeDamage(damage);
                
                // Mostrar efecto de impacto
                Vector3 impactPosition = transform.position;
                SpawnImpactEffectClientRpc(impactPosition);
                
                // Destruir el proyectil
                StartCoroutine(DestroyAfterEffect(0.1f));
            }
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
        // Esperar un breve momento para que se vea el efecto
        yield return new WaitForSeconds(delay);
        
        // Destruir en la red
        NetworkObject.Despawn(true);
    }
    
    // Métodos para inicializar el proyectil
    public void Initialize(Vector3 direction, float damage, ulong ownerId, NetworkObject target = null)
    {
        if (IsServer)
        {
            networkDirection.Value = direction.normalized;
            this.damage = damage;
            networkOwnerId.Value = ownerId;
            
            if (target != null)
            {
                networkTargetId.Value = target.OwnerClientId;
                homing = true;
            }
        }
    }
    
    // Método estático para crear un proyectil desde el servidor
    public static CombatProjectile SpawnProjectile(GameObject prefab, Vector3 position, Vector3 direction, 
                                                  float damage, ulong ownerId, NetworkObject target = null)
    {
        // Solo el servidor puede spawner objetos en red
        if (!NetworkManager.Singleton.IsServer) return null;
        
        // Crear el proyectil
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