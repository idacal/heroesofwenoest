using Unity.Netcode;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlayerCombat : NetworkBehaviour
{
    [Header("Configuración de Ataque")]
    [SerializeField] private float attackDamage = 2f;
    [SerializeField] private float attackRange = 15f;
    [SerializeField] private float attackCooldown = 0.2f;
    [SerializeField] private LayerMask playerLayer;
    
    [Header("Configuración de Proyectil")]
    [SerializeField] private bool isRangedAttacker = true;  // True para personajes a distancia, false para cuerpo a cuerpo
    [SerializeField] private GameObject projectilePrefab;   // Prefab del proyectil para ataques a distancia
    [SerializeField] private Transform projectileSpawnPoint; // Punto de origen del proyectil (opcional)
    [SerializeField] private bool showCooldownDebug = true; // Para depuración

    [Header("Efectos Visuales")]
    [SerializeField] private GameObject attackEffectPrefab;
    [SerializeField] private GameObject hitEffectPrefab;

    // Referencias a componentes
    private PlayerNetwork playerNetwork;
    private PlayerStats playerStats;
    private Camera playerCamera;
    private string playerUniqueId;

    // Estado de combate
    private NetworkVariable<bool> isAttacking = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<float> nextAttackTime = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<ulong> currentTargetId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Target y seguimiento
    private NetworkObject currentTarget;
    private bool isFollowingTarget = false;
    private float followTargetStopDistance = 5f; // Distancia a la que nos detenemos para atacar

    private void Awake()
    {
        // Generar ID único para este jugador para depuración
        playerUniqueId = System.Guid.NewGuid().ToString().Substring(0, 8);

        // Obtener referencias básicas
        playerNetwork = GetComponent<PlayerNetwork>();
        playerStats = GetComponent<PlayerStats>();
    }

    // Método para verificar si el prefab está asignado correctamente
    public void Start()
    {
        if (IsServer && projectilePrefab != null)
        {
            Debug.Log($"✅ Prefab de proyectil asignado correctamente: {projectilePrefab.name}");
        }
        else if (IsServer)
        {
            Debug.LogError("❌ ERROR: Prefab de proyectil NO ASIGNADO");
        }
        
        if (isRangedAttacker)
        {
            Debug.Log("✅ Configurado como atacante a distancia");
        }
        else
        {
            Debug.Log("❗ Configurado como atacante cuerpo a cuerpo (no usará proyectiles)");
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Suscribirse a cambios en las variables de red
        isAttacking.OnValueChanged += OnAttackingChanged;
        currentTargetId.OnValueChanged += OnTargetChanged;

        // Si somos el cliente local, buscar la cámara
        if (IsLocalPlayer)
        {
            StartCoroutine(FindPlayerCamera());
            Debug.Log("🎮 PlayerCombat inicializado para jugador local");
        }
    }

    private IEnumerator FindPlayerCamera()
    {
        // Esperar un frame
        yield return null;

        // Intentar encontrar la cámara principal
        if (Camera.main != null)
        {
            playerCamera = Camera.main;
            Debug.Log("📷 Cámara principal encontrada");
        }
        else
        {
            // Buscar cámara asignada al jugador
            MOBACamera[] cameras = FindObjectsOfType<MOBACamera>();
            foreach (var cam in cameras)
            {
                if (cam.GetTarget() == transform)
                {
                    playerCamera = cam.GetComponent<Camera>();
                    Debug.Log("📷 Cámara MOBACamera encontrada");
                    break;
                }
            }
        }

        if (playerCamera == null)
        {
            Debug.LogWarning("⚠️ No se encontró cámara para el jugador, reintentando...");
            yield return new WaitForSeconds(0.5f);
            StartCoroutine(FindPlayerCamera());
        }
    }

    private void Update()
    {
        // Solo procesar lógica para el jugador local
        if (!IsLocalPlayer) return;

        // PRUEBA: Detectar tecla T para prueba de proyectil
        if (Input.GetKeyDown(KeyCode.T))
        {
            Debug.Log("🧪 Prueba: Tecla T presionada, intentando spawner proyectil");
            TestSpawnProjectileServerRpc();
        }

        // Si estamos siguiendo a un objetivo para atacar
        if (isFollowingTarget && currentTarget != null)
        {
            UpdateTargetFollowing();
        }
    }

    // MÉTODO ACTUALIZADO para la prueba de proyectil con tecla T
    [ServerRpc]
    public void TestSpawnProjectileServerRpc()
    {
        Debug.Log($"[TEST] ServerRpc en servidor - ClientID: {OwnerClientId}, IsHost: {IsHost}, IsServer: {IsServer}");
        
        if (!IsServer) return;
        
        // Si estamos en cooldown, rechazar el ataque
        if (Time.time < nextAttackTime.Value)
        {
            float remainingTime = nextAttackTime.Value - Time.time;
            NotifyInCooldownClientRpc(remainingTime);
            return;
        }
        
        // Configuración básica del disparo
        Vector3 spawnPos = transform.position + transform.forward * 1.2f + Vector3.up * 1.2f;
        Vector3 direction = transform.forward;
        
        // PASO 1: Encontrar jugador más cercano que esté delante de nosotros
        NetworkObject target = FindTargetInFront(30f);
        
        // PASO 2: Actualizar cooldown y estado de ataque
        isAttacking.Value = true;
        nextAttackTime.Value = Time.time + attackCooldown;
        
        // PASO 3: Procesar según si tenemos objetivo o no
        if (target != null)
        {
            // Tenemos un objetivo, dirigir hacia él
            direction = (target.transform.position - spawnPos).normalized;
            currentTargetId.Value = target.OwnerClientId;
            
            Debug.Log($"[TEST] Objetivo encontrado: {target.name}, ID: {target.OwnerClientId}");
            
            // Crear proyectil dirigido al objetivo
            CombatProjectile projectile = CombatProjectile.SpawnProjectile(
                projectilePrefab,
                spawnPos,
                direction,
                attackDamage,
                OwnerClientId,
                target  // Con objetivo específico
            );
            
            // Notificar al cliente
            NotifyTestTargetFoundClientRpc(target.OwnerClientId);
        }
        else
        {
            // Sin objetivo, disparo simple al frente
            Debug.Log("[TEST] Sin objetivo, disparo simple al frente");
            
            // Crear proyectil sin objetivo específico
            CombatProjectile projectile = CombatProjectile.SpawnProjectile(
                projectilePrefab,
                spawnPos,
                direction,
                attackDamage,
                OwnerClientId,
                null  // Sin objetivo
            );
            
            // Notificar al cliente
            NotifyNoTargetFoundClientRpc();
        }
        
        // Mostrar efectos visuales en todos los casos
        SpawnAttackEffectClientRpc(spawnPos, direction);
    }

    // Método auxiliar para encontrar un objetivo en frente del jugador
    private NetworkObject FindTargetInFront(float maxDistance)
    {
        // Variables para el objetivo más cercano
        NetworkObject bestTarget = null;
        float closestAngleDistance = float.MaxValue;
        
        // Colección de jugadores para debug
        List<string> debugPlayers = new List<string>();
        
        // Buscar entre todos los jugadores conectados
        foreach (var clientPair in NetworkManager.Singleton.ConnectedClients)
        {
            // Ignorar nuestro propio jugador
            if (clientPair.Key == OwnerClientId)
                continue;
            
            NetworkObject otherPlayer = clientPair.Value.PlayerObject;
            if (otherPlayer == null) continue;
            
            // Verificar componentes necesarios
            PlayerStats stats = otherPlayer.GetComponent<PlayerStats>();
            if (stats == null) continue;
            
            debugPlayers.Add($"Player {clientPair.Key}");
            
            // Calcular vectores y distancias
            Vector3 targetPos = otherPlayer.transform.position;
            Vector3 toTarget = targetPos - transform.position;
            float distance = toTarget.magnitude;
            
            // Solo considerar jugadores en rango
            if (distance > maxDistance) continue;
            
            // Calcular ángulo con respecto a dónde estamos mirando
            float angle = Vector3.Angle(transform.forward, toTarget.normalized);
            
            // Combinar distancia y ángulo para puntuar (favoreciendo jugadores al frente)
            float combinedScore = angle + (distance * 0.1f);
            
            // Actualizar mejor objetivo si mejora la puntuación
            if (combinedScore < closestAngleDistance)
            {
                closestAngleDistance = combinedScore;
                bestTarget = otherPlayer;
            }
        }
        
        // Log detallado
        Debug.Log($"[TEST] Búsqueda de objetivos: encontrados {debugPlayers.Count} jugadores, " +
                  $"mejor objetivo: {(bestTarget != null ? bestTarget.name : "ninguno")}");
        
        return bestTarget;
    }

    // Nuevo método ClientRpc para notificar sobre cooldown
    [ClientRpc]
    private void NotifyInCooldownClientRpc(float remainingTime)
    {
        if (!IsOwner) return;
        
        Debug.Log($"[TEST] No se puede atacar: en cooldown. Tiempo restante: {remainingTime:F1}s");
    }

    // Nuevo método ClientRpc para notificar sobre objetivo encontrado
    [ClientRpc]
    private void NotifyTestTargetFoundClientRpc(ulong targetId)
    {
        if (!IsOwner) return;
        
        Debug.Log($"[TEST] Ataque automático dirigido al jugador {targetId}");
    }

    // Nuevo método ClientRpc para notificar que no se encontró objetivo
    [ClientRpc]
    private void NotifyNoTargetFoundClientRpc()
    {
        if (!IsOwner) return;
        
        Debug.Log("[TEST] No se encontró ningún objetivo cercano, disparando al frente");
    }

    // Método para verificar si estamos listos para atacar (cooldown)
    public bool IsAttackReady()
    {
        // Si estamos en cooldown
        if (Time.time < nextAttackTime.Value)
        {
            float remainingCooldown = nextAttackTime.Value - Time.time;
            
            // Mostrar tiempo restante (solo para depuración)
            if (showCooldownDebug && IsOwner && remainingCooldown > 0.1f)
            {
                Debug.Log($"[Combat] Ataque en cooldown: {remainingCooldown:F1} segundos restantes");
            }
            
            return false;
        }
        
        return true;
    }

    public bool ProcessClickOnEnemy(NetworkObject enemyObject)
    {
        Debug.Log($"[Combat] ProcessClickOnEnemy llamado - IsOwner: {IsOwner}");
        
        if (!IsLocalPlayer || enemyObject == null) {
            Debug.Log("❌ No somos el jugador local o el enemigo es null");
            return false;
        }

        // NUEVO: Verificación explícita de cooldown
        if (!IsAttackReady())
        {
            // Ya en cooldown, mostrar mensaje más claro
            Debug.Log($"[Combat] No se puede atacar: en cooldown");
            return true; // Retornamos true porque sí procesamos el clic, aunque no atacamos
        }

        // Verificar si podemos atacar al enemigo
        if (CanAttackTarget(enemyObject))
        {
            Debug.Log("✅ Podemos atacar al objetivo");
            
            // Verificar si estamos en rango para atacar inmediatamente
            float distance = Vector3.Distance(transform.position, enemyObject.transform.position);
            Debug.Log($"📏 Distancia al objetivo: {distance}, Rango: {attackRange}");
            
            if (distance <= attackRange)
            {
                Debug.Log("🎯 En rango, llamando a AttackTargetServerRpc");
                // Atacar inmediatamente
                AttackTargetServerRpc(enemyObject.OwnerClientId);
            }
            else
            {
                Debug.Log("🚶 Fuera de rango, siguiendo al objetivo");
                // Seguir al objetivo para luego atacar
                BeginFollowTarget(enemyObject);
            }
            
            return true; // Indicar que procesamos el clic
        }
        
        Debug.Log("❌ No podemos atacar al objetivo");
        return false;
    }

    private bool CanAttackTarget(NetworkObject target)
    {
        // No podemos atacarnos a nosotros mismos
        if (target.OwnerClientId == OwnerClientId) {
            Debug.Log("⚔️ No podemos atacarnos a nosotros mismos");
            return false;
        }

        // Verificar si el objetivo tiene un PlayerStats
        PlayerStats targetStats = target.GetComponent<PlayerStats>();
        if (targetStats == null) {
            Debug.Log("⚔️ El objetivo no tiene PlayerStats");
            return false;
        }

        // Verificar si estamos en cooldown
        if (!IsAttackReady()) {
            Debug.Log($"⚔️ Ataque en cooldown, disponible en {nextAttackTime.Value - Time.time} segundos");
            return false;
        }

        // Podemos atacar
        return true;
    }

    private void BeginFollowTarget(NetworkObject target)
    {
        if (target == null) return;

        Debug.Log($"🏃 Comenzando a seguir objetivo: {target.OwnerClientId}");
        
        currentTarget = target;
        isFollowingTarget = true;
        
        // Notificar al servidor de nuestro objetivo
        SetTargetServerRpc(target.OwnerClientId);
        
        // Mover hacia el objetivo
        Vector3 targetPosition = target.transform.position;
        if (playerNetwork != null)
        {
            // Usar el sistema de movimiento existente para acercarse al objetivo
            playerNetwork.MoveToPositionCommand(targetPosition);
        }
    }

    private void UpdateTargetFollowing()
    {
        if (currentTarget == null)
        {
            // El objetivo ha desaparecido
            isFollowingTarget = false;
            return;
        }

        // Calcular distancia al objetivo
        float distance = Vector3.Distance(transform.position, currentTarget.transform.position);
        
        if (distance <= attackRange)
        {
            // Estamos en rango, detener movimiento y atacar
            isFollowingTarget = false;
            
            // Detener movimiento
            if (playerNetwork != null)
            {
                playerNetwork.StopMovement();
            }
            
            // Atacar
            AttackTargetServerRpc(currentTarget.OwnerClientId);
        }
        else
        {
            // Seguimos fuera de rango, actualizar destino
            if (playerNetwork != null && playerNetwork.CanUpdateMovement())
            {
                // Actualizar la posición destino para seguir al objetivo
                playerNetwork.MoveToPositionCommand(currentTarget.transform.position);
            }
        }
    }

    [ServerRpc]
    private void AttackTargetServerRpc(ulong targetId)
    {
        Debug.Log($"🚀 SERVER: AttackTargetServerRpc llamado, objetivo: {targetId}");
        
        // Verificar cooldown en el servidor
        if (Time.time < nextAttackTime.Value) 
        {
            float remaining = nextAttackTime.Value - Time.time;
            Debug.Log($"⏱️ SERVER: En cooldown, esperando {remaining:F1} segundos más");
            return;
        }

        // Buscar el objetivo
        Debug.Log($"🔍 SERVER: Buscando objetivo con ID {targetId}");
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(targetId, out var client) ||
            client.PlayerObject == null)
        {
            Debug.Log("❌ SERVER: Objetivo no encontrado");
            return;
        }

        NetworkObject targetObj = client.PlayerObject;
        PlayerStats targetStats = targetObj.GetComponent<PlayerStats>();

        if (targetStats == null) {
            Debug.Log("❌ SERVER: Objetivo no tiene PlayerStats");
            return;
        }

        Debug.Log("✅ SERVER: Atacando al objetivo");
        
        // Actualizar variables de red
        isAttacking.Value = true;
        nextAttackTime.Value = Time.time + attackCooldown;
        currentTargetId.Value = targetId;

        // Lanzar proyectil o ataque directo
        if (isRangedAttacker && projectilePrefab != null)
        {
            Debug.Log($"🏹 SERVER: Lanzando proyectil desde {transform.position} a {targetObj.transform.position}");
            
            // Comprobar que el prefab existe
            Debug.Log($"🔍 SERVER: Prefab del proyectil asignado: {(projectilePrefab != null ? "SÍ" : "NO")}");
            
            // Ataques a distancia: Lanzar proyectil
            LaunchProjectile(targetObj);
        }
        else
        {
            Debug.Log("⚔️ SERVER: Ataque cuerpo a cuerpo");
            // Ataques cuerpo a cuerpo: Aplicar daño inmediatamente
            targetStats.TakeDamage(attackDamage);
            SpawnHitEffectClientRpc(targetObj.transform.position);
        }

        // Notificar al cliente sobre el cooldown para UI
        NotifyCooldownClientRpc(attackCooldown);
        
        // Programar fin del ataque
        StartCoroutine(ResetAttackState(0.5f));
    }
    
    // Método para lanzar proyectiles
    private void LaunchProjectile(NetworkObject target)
    {
        // Determinar posición inicial del proyectil
        Vector3 spawnPosition;
        if (projectileSpawnPoint != null)
        {
            // Usar punto específico si está configurado
            spawnPosition = projectileSpawnPoint.position;
        }
        else
        {
            // Calcular posición de origen en el centro del jugador
            spawnPosition = transform.position + Vector3.up * 1.0f; // Solo añadir un poco de altura
        }
        
        // Calcular dirección hacia el objetivo
        Vector3 direction = (target.transform.position - spawnPosition).normalized;
        
        Debug.Log($"🚀 Lanzando proyectil desde {spawnPosition} hacia {target.transform.position}");
        
        // Spawner el proyectil
        var projectile = CombatProjectile.SpawnProjectile(
            projectilePrefab,
            spawnPosition,
            direction,
            attackDamage,
            OwnerClientId,
            target
        );
        
        // Efectos visuales de lanzamiento
        SpawnAttackEffectClientRpc(spawnPosition, direction);
    }

    [ClientRpc]
    private void SpawnHitEffectClientRpc(Vector3 position)
    {
        if (hitEffectPrefab != null)
        {
            // Ajustar posición para que el efecto sea visible
            position.y += 1.0f;
            
            GameObject effect = Instantiate(hitEffectPrefab, position, Quaternion.identity);
            Destroy(effect, 2.0f);
            Debug.Log("💥 Efecto de impacto spawneado");
        }

        // Reproducir sonido de golpe si está implementado
        // PlayHitSound();
    }
    
    [ClientRpc]
    private void SpawnAttackEffectClientRpc(Vector3 position, Vector3 direction)
    {
        // Esta función se llama cuando se lanza un proyectil para mostrar efectos visuales
        // en el punto de origen (como destellos, partículas, etc)
        
        if (attackEffectPrefab != null)
        {
            // Crear efecto de lanzamiento
            GameObject effect = Instantiate(attackEffectPrefab, position, Quaternion.LookRotation(direction));
            Destroy(effect, 1.0f);
            Debug.Log("💫 Efecto de ataque spawneado");
        }
        
        // Reproducir sonido de lanzamiento si está implementado
        // PlayAttackSound();
    }

    // Nuevo método para notificar sobre cooldown a los clientes
    [ClientRpc]
    private void NotifyCooldownClientRpc(float duration)
    {
        if (!IsOwner) return;
        
        Debug.Log($"[Combat] Ataque realizado, próximo disponible en {duration:F1} segundos");
        
        // Aquí podrías actualizar alguna UI de cooldown si la tienes
    }

    private IEnumerator ResetAttackState(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (IsServer)
        {
            isAttacking.Value = false;
        }
    }

    private void OnAttackingChanged(bool oldValue, bool newValue)
    {
        // Puede usarse para sincronizar animaciones o efectos visuales
        if (newValue)
        {
            // Iniciar animación de ataque, efectos, etc.
            if (attackEffectPrefab != null && IsOwner)
            {
                Vector3 effectPosition = transform.position + transform.forward + Vector3.up;
                GameObject effect = Instantiate(attackEffectPrefab, effectPosition, transform.rotation);
                Destroy(effect, 1.0f);
            }
            
            Debug.Log($"⚔️ Jugador {OwnerClientId} está atacando");
        }
    }

    private void OnTargetChanged(ulong oldValue, ulong newValue)
    {
        if (newValue == ulong.MaxValue)
        {
            // Se ha limpiado el objetivo
            currentTarget = null;
            isFollowingTarget = false;
        }
        else
        {
            // Buscar el objeto de red correspondiente al ID del cliente
            // sin usar ConnectedClients que solo funciona en el servidor
            currentTarget = FindNetworkObjectByOwnerClientId(newValue);
        }
    }

    // Método para buscar NetworkObject sin usar ConnectedClients
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

    [ServerRpc]
    private void SetTargetServerRpc(ulong targetId)
    {
        currentTargetId.Value = targetId;
    }

    public bool IsInAttackCooldown()
    {
        return Time.time < nextAttackTime.Value;
    }

    public float GetAttackCooldownRemaining()
    {
        return Mathf.Max(0, nextAttackTime.Value - Time.time);
    }

    // Método para limpiar el objetivo actual
    public void ClearTarget()
    {
        if (IsServer)
        {
            currentTargetId.Value = ulong.MaxValue;
        }
        else if (IsOwner)
        {
            SetTargetServerRpc(ulong.MaxValue);
        }
        
        isFollowingTarget = false;
        currentTarget = null;
    }

    // Método para verificar si podemos atacar al objetivo
    public bool IsValidTarget(NetworkObject target)
    {
        if (target == null) return false;
        
        // No atacar al mismo equipo (opcional, si implementas equipos)
        // if (SameTeam(target)) return false;
        
        // Verificar si tiene los componentes necesarios
        return target.GetComponent<PlayerStats>() != null;
    }
    
    // Opcional: Mostrar UI temporal para cooldown
    private void OnGUI()
    {
        if (IsOwner && showCooldownDebug)
        {
            float cooldown = nextAttackTime.Value - Time.time;
            if (cooldown > 0)
            {
                GUI.Label(new Rect(10, 40, 200, 20), $"Ataque disponible en: {cooldown:F1}s");
            }
            else
            {
                GUI.Label(new Rect(10, 40, 200, 20), "¡Listo para atacar!");
            }
        }
    }
}