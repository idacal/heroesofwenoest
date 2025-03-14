using Unity.Netcode;
using UnityEngine;
using System.Collections;

namespace PlayerAbilities
{
public class SupersonicMissileAbility : BaseAbility
{
    [Header("Configuración Básica")]
    [SerializeField] private float baseDamage = 100f;
    [SerializeField] private float baseExplosionRadius = 4f;
    [SerializeField] private float missileSpeed = 25f;
    [SerializeField] private float missileLifetime = 5f;
    [SerializeField] private float castRange = 30f;
    
    [Header("Bonificación por Velocidad")]
    [SerializeField] private float minSpeedMultiplier = 1.0f;
    [SerializeField] private float maxSpeedMultiplier = 2.5f;
    [SerializeField] private float minSpeed = 1.0f;
    [SerializeField] private float maxSpeedForBonus = 15.0f;
    
    [Header("Sinergia con Dash")]
    [SerializeField] private bool enableDashSynergy = true;
    [SerializeField] private float dashSynergyWindow = 2.0f; // Tiempo después de dash para activar sinergia
    [SerializeField] private int fragmentCount = 5; // Número de fragmentos al explotar
    [SerializeField] private float fragmentDamagePercent = 0.3f; // 30% del daño principal
    [SerializeField] private float fragmentRadius = 2.0f; // Radio de explosión de fragmentos
    
    [Header("Prefabs")]
    [SerializeField] private GameObject missilePrefab;
    [SerializeField] private GameObject explosionEffectPrefab;
    [SerializeField] private GameObject fragmentPrefab;
    
    // Referencias
    private PlayerNetwork playerNetwork;
    private DashAbility dashAbility;
    
    // NUEVO: Referencia al PlayerAbilityManager
    private PlayerAbilityManager abilityManager;
    
    // Variables para rastrear el tiempo desde el último dash
    private float lastDashTime = -10f;
    private bool dashSynergyActive = false;
    
    public override void Initialize(NetworkBehaviour owner)
{
    base.Initialize(owner);
    abilityName = "Misil Supersónico";
    activationKey = KeyCode.R;
    manaCost = 40f;
    cooldown = 1f;
    
    Debug.Log("[SupersonicMissileAbility] Initializing...");
    
    // Obtener referencias
    playerNetwork = owner.GetComponent<PlayerNetwork>();
    dashAbility = owner.GetComponent<DashAbility>();
    
    // NUEVO: Obtener referencia al PlayerAbilityManager
    abilityManager = owner.GetComponent<PlayerAbilityManager>();
    
    // Verificar que tengamos los componentes necesarios
    if (playerNetwork == null)
        Debug.LogWarning("[SupersonicMissileAbility] PlayerNetwork not found!");
    
    // NEW: Try to get missile prefab from GameResources if not already assigned
    if (missilePrefab == null)
    {
        GameResources resources = Resources.Load<GameResources>("GameResources");
        if (resources != null && resources.missileProjectilePrefab != null)
        {
            missilePrefab = resources.missileProjectilePrefab;
            Debug.Log($"[SupersonicMissileAbility] Loaded missile prefab '{missilePrefab.name}' from GameResources");
        }
        else
        {
            Debug.LogError("[SupersonicMissileAbility] Couldn't load missile prefab from GameResources");
        }
    }
    
    // Check if prefab is still null after attempting to load
    if (missilePrefab == null)
        Debug.LogError("[SupersonicMissileAbility] missilePrefab not assigned!");
    
    // Suscribirse a eventos de dash si existe
    if (dashAbility != null && enableDashSynergy)
    {
        // Monitorear el estado del Dash para detectar cuando termina
        StartCoroutine(MonitorDashState());
    }
    
    Debug.Log($"[SupersonicMissileAbility] Initialized with key {activationKey}, playerNetwork: {playerNetwork != null}, missilePrefab: {missilePrefab != null}");
}
    
    private IEnumerator MonitorDashState()
    {
        bool wasDashing = false;
        
        while (true)
        {
            // Verificar si se acaba de terminar un dash
            if (dashAbility != null)
            {
                bool isDashing = dashAbility.IsDashing;
                
                if (wasDashing && !isDashing)
                {
                    // El dash acaba de terminar
                    lastDashTime = Time.time;
                    
                    if (networkOwner.IsOwner)
                    {
                        Debug.Log("[SupersonicMissile] Dash detectado, sinergia disponible por " + dashSynergyWindow + " segundos");
                    }
                }
                
                wasDashing = isDashing;
            }
            
            // Actualizar estado de sinergia con dash
            dashSynergyActive = (Time.time - lastDashTime <= dashSynergyWindow);
            
            yield return new WaitForSeconds(0.1f);
        }
    }
    
    public override bool CanActivate()
    {
        // Verificar que tengamos missilePrefab
        if (missilePrefab == null)
        {
            Debug.LogError("[SupersonicMissileAbility] Cannot activate: missilePrefab is null!");
            return false;
        }
        
        // Verificar que playerStats no sea null
        if (playerStats == null)
        {
            Debug.LogWarning("[SupersonicMissileAbility] playerStats is null in CanActivate!");
            return false;
        }
        
        return isReady && playerStats.CurrentMana >= manaCost;
    }
    
    public override void Activate()
    {
        Debug.Log("[SupersonicMissileAbility] Activate called");
        
        if (networkOwner.IsOwner)
        {
            Debug.Log("[SupersonicMissile] Activando Misil Supersónico");
            
            // Calcular bonificadores basados en velocidad
            float speedMultiplier = CalculateSpeedMultiplier();
            
            // Verificar sinergia con dash
            bool useDashSynergy = dashSynergyActive && enableDashSynergy;
            
            // Lanzar el misil (a través del servidor)
            LaunchMissileServerRpc(speedMultiplier, useDashSynergy);
        }
    }
    
    private float CalculateSpeedMultiplier()
    {
        float currentSpeed = 0f;
        
        // Obtener velocidad actual, primero intentar con PlayerAbilityManager
        if (abilityManager != null)
        {
            // Usar información del manager para determinar si estamos en dash
            if (dashAbility != null && dashAbility.IsDashing)
            {
                currentSpeed = maxSpeedForBonus;
            }
            else if (playerNetwork != null)
            {
                // Calcular velocidad basada en el Rigidbody
                Rigidbody rb = networkOwner.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    currentSpeed = rb.velocity.magnitude;
                }
            }
        }
        // Fallback al playerNetwork directamente
        else if (playerNetwork != null)
        {
            // Si estamos en dash, usar velocidad máxima
            if (dashAbility != null && dashAbility.IsDashing)
            {
                currentSpeed = maxSpeedForBonus;
            }
            else
            {
                // Calcular velocidad basada en el Rigidbody
                Rigidbody rb = networkOwner.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    currentSpeed = rb.velocity.magnitude;
                }
            }
        }
        
        // Calcular multiplicador basado en velocidad
        float speedFactor = Mathf.Clamp01((currentSpeed - minSpeed) / (maxSpeedForBonus - minSpeed));
        float multiplier = Mathf.Lerp(minSpeedMultiplier, maxSpeedMultiplier, speedFactor);
        
        if (networkOwner.IsOwner)
        {
            Debug.Log($"[SupersonicMissile] Velocidad actual: {currentSpeed:F1}, Multiplicador: {multiplier:F2}x");
        }
        
        return multiplier;
    }
    
[ServerRpc]
private void LaunchMissileServerRpc(float speedMultiplier, bool useDashSynergy)
{
    // Verify we have a missile prefab
    if (missilePrefab == null)
    {
        Debug.LogError("[SupersonicMissile] missilePrefab is null!");
        return;
    }
    
    // Check cooldown and mana
    if (!isReady || !playerStats.UseMana(manaCost))
    {
        return;
    }
    
    // IMPROVED: Determine launch position and direction
    Vector3 spawnPosition = networkOwner.transform.position + Vector3.up * 1.2f;
    Vector3 direction;
    
    // Get player's movement direction
    if (playerNetwork != null && playerNetwork.IsMoving())
    {
        // Use the direction between current position and target position
        Vector3 targetPos = playerNetwork.GetTargetPosition();
        direction = (targetPos - networkOwner.transform.position).normalized;
        Debug.Log($"[SupersonicMissile] Using movement direction: {direction}, Target: {targetPos}, Current: {networkOwner.transform.position}");
    }
    else
    {
        // If not moving, use the direction the player is facing
        direction = networkOwner.transform.forward;
        Debug.Log($"[SupersonicMissile] Using forward direction: {direction}");
    }
    
    // Keep missile flight path horizontal
    direction.y = 0;
    direction.Normalize();
    
    // IMPROVE: Make missile travel farther
    float missileRangeMultiplier = 1.5f; // Increase range by 50%
    float actualMissileSpeed = missileSpeed * speedMultiplier;
    float actualMissileLifetime = missileLifetime * missileRangeMultiplier;
    float enhancedDamage = baseDamage * speedMultiplier;
    float enhancedRadius = baseExplosionRadius * (1 + (speedMultiplier - 1) * 0.5f);
    
    try
    {
        // Create the missile slightly in front of the player
        Vector3 adjustedPosition = spawnPosition + direction * 1.0f;
        
        // FIX ALIGNMENT: Create the missile with identity rotation first
        GameObject missile = Instantiate(missilePrefab, adjustedPosition, Quaternion.identity);
        
        // Then set the rotation properly
        // Check if there's a visual transform that needs alignment
        Transform missileVisual = missile.transform.Find("MissileVisual");
        if (missileVisual != null)
        {
            // Align the visual child
            missileVisual.rotation = Quaternion.LookRotation(direction);
        }
        else
        {
            // Align the entire missile
            missile.transform.rotation = Quaternion.LookRotation(direction);
        }
        
        // Configure the missile
        SupersonicMissileProjectile missileComponent = missile.AddComponent<SupersonicMissileProjectile>();
        missileComponent.Initialize(
            direction, 
            actualMissileSpeed,
            actualMissileLifetime,
            enhancedDamage, 
            enhancedRadius, 
            networkOwner.OwnerClientId,
            useDashSynergy,
            fragmentCount,
            fragmentDamagePercent,
            fragmentRadius,
            explosionEffectPrefab,
            fragmentPrefab
        );
        
        // Add debug visuals to show direction
        Debug.DrawRay(adjustedPosition, direction * 10f, Color.red, 5f);
        
        // Spawn in the network
        NetworkObject missileNetObj = missile.GetComponent<NetworkObject>();
        if (missileNetObj == null)
        {
            missileNetObj = missile.AddComponent<NetworkObject>();
        }
        missileNetObj.Spawn();
        
        // Add collider if needed
        if (missile.GetComponent<Collider>() == null)
        {
            SphereCollider collider = missile.AddComponent<SphereCollider>();
            collider.radius = 0.5f;
            collider.isTrigger = true;
        }
        
        // Notify about missile launch
        LaunchMissileInfoClientRpc(enhancedDamage, enhancedRadius, speedMultiplier, 
            useDashSynergy ? ", with fragmentation" : ", no fragmentation");
        
        // Start cooldown
        StartCoroutine(StartCooldown());
        
        Debug.Log($"[SupersonicMissile] Missile launched successfully in direction {direction}");
    }
    catch (System.Exception e)
    {
        Debug.LogError($"[SupersonicMissile] Error launching missile: {e.Message}");
    }
}
    
    [ClientRpc]
    private void LaunchMissileInfoClientRpc(float damage, float radius, float multiplier, string extraInfo)
    {
        if (networkOwner.IsOwner)
        {
            Debug.Log($"[SupersonicMissile] Misil lanzado: {damage:F0} daño, {radius:F1}m radio, {multiplier:F2}x multiplicador{extraInfo}");
        }
    }
    
    public override void UpdateAbility()
    {
        // Actualizar estado de sinergia con dash
        if (networkOwner.IsOwner && enableDashSynergy)
        {
            dashSynergyActive = (Time.time - lastDashTime <= dashSynergyWindow);
            
            // Mostrar feedback visual de la sinergia disponible
            if (dashSynergyActive && Time.frameCount % 60 == 0)
            {
                float remainingTime = dashSynergyWindow - (Time.time - lastDashTime);
                Debug.Log($"[SupersonicMissile] Sinergia con Dash disponible: {remainingTime:F1}s restantes");
            }
        }
    }
}

// Clase para el comportamiento del proyectil del misil
public class SupersonicMissileProjectile : MonoBehaviour
{
    // Propiedades del misil
    private Vector3 direction;
    private float speed;
    private float lifetime;
    private float damage;
    private float explosionRadius;
    private ulong ownerClientId;
    private bool enableFragmentation;
    private int fragmentCount;
    private float fragmentDamagePercent;
    private float fragmentRadius;
    private GameObject explosionEffectPrefab;
    private GameObject fragmentPrefab;
    
    // Variables de estado
    private float spawnTime;
    private bool hasExploded = false;
    
    // Componentes
    private Rigidbody rb;
    private TrailRenderer trail;
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            // Si no hay Rigidbody, lo creamos
            rb = gameObject.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
        
        // Obtener trail si existe
        trail = GetComponent<TrailRenderer>();
        
        Debug.Log("[SupersonicMissileProjectile] Projectile created");
    }
    
    public void Initialize(
        Vector3 direction, 
        float speed, 
        float lifetime, 
        float damage, 
        float explosionRadius, 
        ulong ownerClientId,
        bool enableFragmentation,
        int fragmentCount,
        float fragmentDamagePercent,
        float fragmentRadius,
        GameObject explosionEffectPrefab,
        GameObject fragmentPrefab)
    {
        this.direction = direction.normalized;
        this.speed = speed;
        this.lifetime = lifetime;
        this.damage = damage;
        this.explosionRadius = explosionRadius;
        this.ownerClientId = ownerClientId;
        this.enableFragmentation = enableFragmentation;
        this.fragmentCount = fragmentCount;
        this.fragmentDamagePercent = fragmentDamagePercent;
        this.fragmentRadius = fragmentRadius;
        this.explosionEffectPrefab = explosionEffectPrefab;
        this.fragmentPrefab = fragmentPrefab;
        
        spawnTime = Time.time;
        transform.forward = direction;
        
        // Aplicar velocidad inicial
        if (rb != null)
        {
            rb.velocity = direction * speed;
        }
        
        Debug.Log($"[SupersonicMissileProjectile] Initialized with: speed={speed}, damage={damage}, owner={ownerClientId}");
    }
    
    private void Update()
    {
        // Verificar tiempo de vida
        if (Time.time - spawnTime > lifetime && !hasExploded)
        {
            Explode(transform.position);
        }
        
        // Mantenemos la dirección constante (no afectada por colisiones)
        if (rb != null && !hasExploded)
        {
            rb.velocity = direction * speed;
            transform.forward = direction;
        }
    }
    
    private void OnTriggerEnter(Collider other)
    {
        // Ignorar colisiones si ya explotó
        if (hasExploded) return;
        
        // Ignorar colisiones con el propietario
        NetworkObject hitNetObj = other.GetComponent<NetworkObject>();
        if (hitNetObj != null && hitNetObj.OwnerClientId == ownerClientId)
        {
            return;
        }
        
        Explode(transform.position);
    }
    
    private void Explode(Vector3 position)
    {
        if (hasExploded) return;
        
        hasExploded = true;
        Debug.Log($"[SupersonicMissile] Explosión en {position}, radio: {explosionRadius}m, daño: {damage}");
        
        // Desactivar colisiones y movimiento
        if (GetComponent<Collider>() != null)
        {
            GetComponent<Collider>().enabled = false;
        }
        
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.velocity = Vector3.zero;
        }
        
        // Desactivar trail
        if (trail != null)
        {
            trail.emitting = false;
        }
        
        // Mostrar efecto de explosión
        if (explosionEffectPrefab != null)
        {
            GameObject explosionEffect = Instantiate(explosionEffectPrefab, position, Quaternion.identity);
            // Escalar efecto según radio
            explosionEffect.transform.localScale = Vector3.one * (explosionRadius / 4f);
            Destroy(explosionEffect, 3f);
        }
        
        // Aplicar daño en área
        Collider[] hitColliders = Physics.OverlapSphere(position, explosionRadius);
        foreach (var hitCollider in hitColliders)
        {
            // Verificar si es un jugador
            NetworkObject targetNetObj = hitCollider.GetComponent<NetworkObject>();
            if (targetNetObj != null)
            {
                // Buscar PlayerStats
                PlayerStats targetStats = targetNetObj.GetComponent<PlayerStats>();
                if (targetStats != null)
                {
                    // No dañar al propietario
                    if (targetNetObj.OwnerClientId != ownerClientId)
                    {
                        // Calcular daño considerando distancia al centro
                        float distance = Vector3.Distance(position, hitCollider.transform.position);
                        float damageMultiplier = 1f - (distance / explosionRadius);
                        float finalDamage = damage * Mathf.Max(0.2f, damageMultiplier);
                        
                        targetStats.TakeDamage(finalDamage);
                        Debug.Log($"[SupersonicMissile] Daño a {targetNetObj.OwnerClientId}: {finalDamage:F0} (dist: {distance:F1}m)");
                    }
                }
            }
        }
        
        // Crear fragmentos si está habilitado
        if (enableFragmentation && fragmentPrefab != null)
        {
            CreateFragments(position);
        }
        
        // Destruir el misil después de un breve retraso
        Destroy(gameObject, 0.1f);
    }
    
    private void CreateFragments(Vector3 position)
    {
        for (int i = 0; i < fragmentCount; i++)
        {
            // Calcular dirección aleatoria para este fragmento
            float angle = UnityEngine.Random.Range(0f, 360f);
            float distance = UnityEngine.Random.Range(2f, 5f);
            
            Vector3 fragmentDirection = Quaternion.Euler(0, angle, 0) * Vector3.forward;
            Vector3 landingPos = position + fragmentDirection * distance;
            
            // Ajustar para mantener la altura del impacto original
            landingPos.y = position.y;
            
            // Lanzar raycast para encontrar punto de aterrizaje
            if (Physics.Raycast(position, fragmentDirection, out RaycastHit hit, distance))
            {
                landingPos = hit.point;
            }
            
            // Crear fragmento
            GameObject fragment = Instantiate(fragmentPrefab, position, Quaternion.identity);
            
            // Inicializar fragmento
            MissileFragment fragmentComponent = fragment.AddComponent<MissileFragment>();
            fragmentComponent.Initialize(
                landingPos, 
                damage * fragmentDamagePercent, 
                fragmentRadius, 
                ownerClientId,
                explosionEffectPrefab
            );
            
            // No necesitamos spawnearlo en la red si sólo es visual
            Destroy(fragment, 5f);
        }
    }
}

// Clase para los fragmentos del misil
public class MissileFragment : MonoBehaviour
{
    private Vector3 targetPosition;
    private float damage;
    private float radius;
    private ulong ownerClientId;
    private GameObject explosionEffectPrefab;
    
    private float duration = 1.5f;
    private float timeElapsed = 0f;
    private bool hasExploded = false;
    
    private TrailRenderer trail;
    
    private void Awake()
    {
        trail = GetComponent<TrailRenderer>();
    }
    
    public void Initialize(Vector3 targetPosition, float damage, float radius, ulong ownerClientId, GameObject explosionEffectPrefab)
    {
        this.targetPosition = targetPosition;
        this.damage = damage;
        this.radius = radius;
        this.ownerClientId = ownerClientId;
        this.explosionEffectPrefab = explosionEffectPrefab;
    }
    
    private void Update()
    {
        if (hasExploded) return;
        
        timeElapsed += Time.deltaTime;
        float progress = timeElapsed / duration;
        
        if (progress >= 1f)
        {
            Explode();
            return;
        }
        
        // Trayectoria parabólica
        Vector3 startPos = transform.position;
        Vector3 midPos = Vector3.Lerp(startPos, targetPosition, 0.5f) + Vector3.up * 3f;
        
        transform.position = Bezier(startPos, midPos, targetPosition, progress);
        
        // Orientar hacia la dirección del movimiento
        Vector3 moveDirection = (targetPosition - transform.position).normalized;
        if (moveDirection != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(moveDirection);
        }
    }
    
    private Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        Vector3 ab = Vector3.Lerp(a, b, t);
        Vector3 bc = Vector3.Lerp(b, c, t);
        return Vector3.Lerp(ab, bc, t);
    }
    
    private void Explode()
    {
        if (hasExploded) return;
        
        hasExploded = true;
        
        // Efecto visual
        if (explosionEffectPrefab != null)
        {
            GameObject effect = Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
            effect.transform.localScale = Vector3.one * (radius / 4f);
            Destroy(effect, 2f);
        }
        
        // Aplicar daño en área
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, radius);
        foreach (var hitCollider in hitColliders)
        {
            // Verificar si es un jugador
            NetworkObject targetNetObj = hitCollider.GetComponent<NetworkObject>();
            if (targetNetObj != null && targetNetObj.OwnerClientId != ownerClientId)
            {
                PlayerStats targetStats = targetNetObj.GetComponent<PlayerStats>();
                if (targetStats != null)
                {
                    targetStats.TakeDamage(damage);
                    Debug.Log($"[MissileFragment] Daño a {targetNetObj.OwnerClientId}: {damage:F0}");
                }
            }
        }
        
        // Desactivar trail
        if (trail != null)
        {
            trail.emitting = false;
        }
        
        // Destruir tras un breve retraso
        Destroy(gameObject, 0.1f);
    }
}
}