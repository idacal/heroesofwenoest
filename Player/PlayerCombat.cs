using Unity.Netcode;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlayerCombat : NetworkBehaviour
{
    [Header("Configuración de Ataque")]
    [SerializeField] private float attackDamage = 5f;
    [SerializeField] private float attackRange = 15f;
    [SerializeField] private float attackCooldown = 0.2f; // Cooldown en segundos (0.2 = 200ms)
    [SerializeField] private LayerMask playerLayer;
    
    [Header("Configuración de Proyectil")]
    [SerializeField] private bool isRangedAttacker = true;  // True para personajes a distancia, false para cuerpo a cuerpo
    [SerializeField] private GameObject projectilePrefab;   // Prefab del proyectil para ataques a distancia
    [SerializeField] private Transform projectileSpawnPoint; // Punto de origen del proyectil (opcional)
    [SerializeField] private bool showCooldownDebug = true; // Para depuración

    [Header("Coste de Maná")]
    [SerializeField] private float attackManaCost = 2f;    // Coste de maná por ataque
    [SerializeField] private bool showManaWarnings = true; // Mostrar avisos de maná insuficiente

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

    // IMPORTANTE: Esta variable debe ser estructurada correctamente para Netcode for GameObjects
    private NetworkVariable<float> serverAttackTime = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<ulong> currentTargetId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Variable local (no de red) para rastrear el tiempo de cooldown local
    private float localNextAttackTime = 0f;

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
        
        // Inicializar tiempo de ataque local
        localNextAttackTime = 0f;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Suscribirse a cambios en las variables de red
        isAttacking.OnValueChanged += OnAttackingChanged;
        currentTargetId.OnValueChanged += OnTargetChanged;
        serverAttackTime.OnValueChanged += OnServerAttackTimeChanged;

        // Si somos el cliente local, buscar la cámara
        if (IsLocalPlayer)
        {
            StartCoroutine(FindPlayerCamera());
            Debug.Log("🎮 PlayerCombat inicializado para jugador local");
        }
    }

    // Manejador para cambios en el tiempo de ataque del servidor
    private void OnServerAttackTimeChanged(float previousValue, float newValue)
    {
        if (IsOwner && !IsServer)
        {
            // Si somos un cliente (no host), usamos este valor para sincronizar nuestro cooldown local
            localNextAttackTime = Time.time + attackCooldown;
            Debug.Log($"[COOLDOWN] Cliente recibió actualización de serverAttackTime: {newValue}, " +
                      $"estableciendo localNextAttackTime: {localNextAttackTime} " +
                      $"(tiempo actual + cooldown: {Time.time} + {attackCooldown})");
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

        // Detectar tecla T para prueba de proyectil
        if (Input.GetKeyDown(KeyCode.T))
        {
            if (IsAttackReady())
            {
                Debug.Log("🧪 Tecla T presionada, intentando lanzar proyectil");
                TestSpawnProjectileServerRpc();
            }
            else
            {
                Debug.Log($"🧪 No se puede atacar aún. Time.time: {Time.time}, próximo ataque: {localNextAttackTime}");
            }
        }

        // Si estamos siguiendo a un objetivo para atacar
        if (isFollowingTarget && currentTarget != null)
        {
            UpdateTargetFollowing();
        }
    }

    // Método para el ataque con tecla T
    [ServerRpc]
    public void TestSpawnProjectileServerRpc()
    {
        Debug.Log($"[Ataque T] ServerRpc recibido - ClientID: {OwnerClientId}");
        
        // Verificar cooldown (solo comprobamos el tiempo en el servidor)
        float currentTime = Time.time;
        if (currentTime < serverAttackTime.Value + attackCooldown)
        {
            float remainingTime = (serverAttackTime.Value + attackCooldown) - currentTime;
            Debug.Log($"[Ataque T] En cooldown por {remainingTime:F1}s");
            return;
        }
        
        // Verificar maná
        if (playerStats.CurrentMana < attackManaCost)
        {
            Debug.Log($"[Ataque T] Maná insuficiente: {playerStats.CurrentMana:F1}/{attackManaCost}");
            NotifyInsufficientManaClientRpc();
            return;
        }
        
        // Configuración del disparo
        Vector3 spawnPos = transform.position + transform.forward * 1.2f + Vector3.up * 1.2f;
        Vector3 direction = transform.forward;
        
        // Buscar objetivo cercano
        NetworkObject target = FindTargetInFront(30f);
        
        // Actualizar estado
        isAttacking.Value = true;
        
        // IMPORTANTE: Guardar el tiempo actual del servidor para cooldown
        serverAttackTime.Value = currentTime;
        
        // Consumir maná
        playerStats.UseMana(attackManaCost);
        
        // Procesar ataque según si hay objetivo o no
        if (target != null)
        {
            // Con objetivo
            direction = (target.transform.position - spawnPos).normalized;
            currentTargetId.Value = target.OwnerClientId;
            
            // Lanzar proyectil dirigido
            CombatProjectile.SpawnProjectile(
                projectilePrefab,
                spawnPos,
                direction,
                attackDamage,
                OwnerClientId,
                target
            );
        }
        else
        {
            // Sin objetivo, disparo al frente
            CombatProjectile.SpawnProjectile(
                projectilePrefab,
                spawnPos,
                direction,
                attackDamage,
                OwnerClientId,
                null
            );
        }
        
        // Mostrar efectos visuales
        SpawnAttackEffectClientRpc(spawnPos, direction);
    }

    // Método auxiliar para encontrar un objetivo en frente del jugador
    private NetworkObject FindTargetInFront(float maxDistance)
    {
        // Variables para el objetivo más cercano
        NetworkObject bestTarget = null;
        float closestAngleDistance = float.MaxValue;
        
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
        
        return bestTarget;
    }

    // Notificación de maná insuficiente
    [ClientRpc]
    private void NotifyInsufficientManaClientRpc()
    {
        if (!IsOwner) return;
        
        if (showManaWarnings)
        {
            Debug.Log($"<color=blue>[Maná]</color> No tienes suficiente maná para atacar. Necesitas {attackManaCost} maná.");
        }
    }

    // Método para verificar si estamos listos para atacar (cooldown y maná)
    public bool IsAttackReady()
    {
        // Verificar maná primero
        if (playerStats != null && playerStats.CurrentMana < attackManaCost)
        {
            if (showManaWarnings && IsOwner)
            {
                Debug.Log($"<color=blue>[Maná]</color> No tienes suficiente maná para atacar ({playerStats.CurrentMana:F1}/{attackManaCost} requerido)");
            }
            return false;
        }
        
        // IMPORTANTE: Usar la variable local de tiempo para clientes, para evitar problemas de latencia
        float timeToCheck = IsServer ? (serverAttackTime.Value + attackCooldown) : localNextAttackTime;
        
        // Verificar cooldown
        if (Time.time < timeToCheck)
        {
            float remainingCooldown = timeToCheck - Time.time;
            
            // Mostrar tiempo restante (solo para depuración)
            if (showCooldownDebug && IsOwner && remainingCooldown > 0.01f)
            {
                Debug.Log($"[COOLDOWN] Ataque en cooldown: {remainingCooldown:F3}s restantes. " +
                          $"Current: {Time.time:F3}, Next: {timeToCheck:F3}");
            }
            
            return false;
        }
        
        return true;
    }

    public bool ProcessClickOnEnemy(NetworkObject enemyObject)
    {
        if (!IsLocalPlayer || enemyObject == null) {
            return false;
        }

        // Verificación de cooldown local y depuración
        if (showCooldownDebug)
        {
            Debug.Log($"[CLICK] Verificando ataque - Time.time: {Time.time:F3}, localNextAttackTime: {localNextAttackTime:F3}, " +
                     $"serverAttackTime: {serverAttackTime.Value:F3}, cooldown: {attackCooldown}");
        }

        // Verificar cooldown y maná
        if (!IsAttackReady())
        {
            return true; // Procesamos el clic aunque no podamos atacar
        }

        // Verificar si podemos atacar al enemigo
        if (CanAttackTarget(enemyObject))
        {
            // Verificar si estamos en rango para atacar inmediatamente
            float distance = Vector3.Distance(transform.position, enemyObject.transform.position);
            
            if (distance <= attackRange)
            {
                Debug.Log($"[CLICK] Atacando directamente al objetivo {enemyObject.OwnerClientId}, " +
                          $"distancia: {distance:F1}, rango: {attackRange:F1}");
                
                // Para evitar retrasos por latencia, actualizamos el tiempo local inmediatamente
                localNextAttackTime = Time.time + attackCooldown;
                
                // Atacar inmediatamente
                AttackTargetServerRpc(enemyObject.OwnerClientId);
            }
            else
            {
                Debug.Log($"[CLICK] Fuera de rango, comenzando seguimiento al objetivo {enemyObject.OwnerClientId}, " +
                          $"distancia: {distance:F1}, rango: {attackRange:F1}");
                
                // Seguir al objetivo para luego atacar
                BeginFollowTarget(enemyObject);
            }
            
            return true; // Indicar que procesamos el clic
        }
        
        return false;
    }

    private bool CanAttackTarget(NetworkObject target)
    {
        // No podemos atacarnos a nosotros mismos
        if (target.OwnerClientId == OwnerClientId) {
            return false;
        }

        // Verificar si el objetivo tiene un PlayerStats
        PlayerStats targetStats = target.GetComponent<PlayerStats>();
        if (targetStats == null) {
            return false;
        }

        // Verificar cooldown y maná
        if (!IsAttackReady()) {
            return false;
        }

        // Podemos atacar
        return true;
    }

    private void BeginFollowTarget(NetworkObject target)
    {
        if (target == null) return;
        
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
            
            // Verificar si podemos atacar (cooldown y maná)
            if (IsAttackReady())
            {
                // Para evitar retrasos por latencia, actualizamos el tiempo local inmediatamente
                localNextAttackTime = Time.time + attackCooldown;
                
                // Atacar
                AttackTargetServerRpc(currentTarget.OwnerClientId);
            }
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
        Debug.Log($"[Ataque Click] ServerRpc recibido, objetivo: {targetId}");
        
        // Verificar cooldown (solo comprobamos el tiempo en el servidor)
        float currentTime = Time.time;
        if (currentTime < serverAttackTime.Value + attackCooldown)
        {
            float remainingTime = (serverAttackTime.Value + attackCooldown) - currentTime;
            Debug.Log($"[Ataque Click] En cooldown por {remainingTime:F1}s");
            return;
        }
        
        // Verificar maná
        if (playerStats.CurrentMana < attackManaCost)
        {
            Debug.Log($"[Ataque Click] Maná insuficiente: {playerStats.CurrentMana:F1}/{attackManaCost}");
            NotifyInsufficientManaClientRpc();
            return;
        }

        // Buscar el objetivo
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(targetId, out var client) ||
            client.PlayerObject == null)
        {
            Debug.Log("[Ataque Click] Objetivo no encontrado");
            return;
        }

        NetworkObject targetObj = client.PlayerObject;
        PlayerStats targetStats = targetObj.GetComponent<PlayerStats>();

        if (targetStats == null) {
            Debug.Log("[Ataque Click] Objetivo no tiene PlayerStats");
            return;
        }
        
        // Actualizar estado
        isAttacking.Value = true;
        
        // IMPORTANTE: Guardar el tiempo actual del servidor para cooldown
        serverAttackTime.Value = currentTime;
        
        // Consumir maná
        playerStats.UseMana(attackManaCost);

        // Lanzar proyectil o ataque directo
        if (isRangedAttacker && projectilePrefab != null)
        {
            // Ataques a distancia: Lanzar proyectil
            LaunchProjectile(targetObj);
        }
        else
        {
            // Ataques cuerpo a cuerpo: Aplicar daño inmediatamente
            targetStats.TakeDamage(attackDamage);
            SpawnHitEffectClientRpc(targetObj.transform.position);
        }
        
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
            spawnPosition = transform.position + Vector3.up * 1.0f;
        }
        
        // Calcular dirección hacia el objetivo
        Vector3 direction = (target.transform.position - spawnPosition).normalized;
        
        // Spawner el proyectil
        CombatProjectile.SpawnProjectile(
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
        }
    }
    
    [ClientRpc]
    private void SpawnAttackEffectClientRpc(Vector3 position, Vector3 direction)
    {
        if (attackEffectPrefab != null)
        {
            // Crear efecto de lanzamiento
            GameObject effect = Instantiate(attackEffectPrefab, position, Quaternion.LookRotation(direction));
            Destroy(effect, 1.0f);
        }
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

    // Método para verificar si estamos en cooldown de ataque
    public bool IsInAttackCooldown()
    {
        // IMPORTANTE: Usar la variable local de tiempo para clientes, para evitar problemas de latencia
        float timeToCheck = IsServer ? (serverAttackTime.Value + attackCooldown) : localNextAttackTime;
        return Time.time < timeToCheck;
    }

    // Método para obtener el tiempo restante de cooldown
    public float GetAttackCooldownRemaining()
    {
        // IMPORTANTE: Usar la variable local de tiempo para clientes, para evitar problemas de latencia
        float timeToCheck = IsServer ? (serverAttackTime.Value + attackCooldown) : localNextAttackTime;
        return Mathf.Max(0, timeToCheck - Time.time);
    }
    
    // Método para obtener el costo de maná del ataque
    public float GetAttackManaCost()
    {
        return attackManaCost;
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
        
        // Verificar si tiene los componentes necesarios
        return target.GetComponent<PlayerStats>() != null;
    }
    
    // Opcional: Mostrar UI temporal para cooldown y maná
    private void OnGUI()
    {
        if (IsOwner && showCooldownDebug)
        {
            // IMPORTANTE: Usar la variable local de tiempo para clientes, para evitar problemas de latencia
            float timeToCheck = IsServer ? (serverAttackTime.Value + attackCooldown) : localNextAttackTime;
            float cooldown = timeToCheck - Time.time;
            
            if (cooldown > 0)
            {
                GUI.Label(new Rect(10, 40, 300, 20), $"Ataque en cooldown: {cooldown:F3}s (next: {timeToCheck:F3}, now: {Time.time:F3})");
            }
            else
            {
                GUI.Label(new Rect(10, 40, 200, 20), "¡Listo para atacar!");
            }
            
            // Mostrar costo de maná del ataque
            if (playerStats != null)
            {
                GUI.Label(new Rect(10, 60, 200, 20), $"Maná: {playerStats.CurrentMana:F1}/{playerStats.MaxMana} (Costo: {attackManaCost})");
            }
        }
    }
}