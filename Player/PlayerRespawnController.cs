using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class PlayerRespawnController : NetworkBehaviour
{
    [Header("Configuración de Respawn")]
    [SerializeField] private float fallThreshold = -10f; // Altura a la que se considera caída
    [SerializeField] private float respawnDelay = 1.5f; // Tiempo antes de reaparecer
    [SerializeField] private float invulnerabilityTime = 2.0f; // Tiempo de invulnerabilidad después de respawn
    
    [Header("Referencias de Spawn")]
    [SerializeField] private Transform[] team1SpawnPoints; // Puntos para equipo 1
    [SerializeField] private Transform[] team2SpawnPoints; // Puntos para equipo 2
    
    [Header("Efectos")]
    [SerializeField] private GameObject deathEffectPrefab; // Efecto al morir
    [SerializeField] private GameObject respawnEffectPrefab; // Efecto al reaparecer
    [SerializeField] private Material ghostMaterial; // Material semitransparente para invulnerabilidad

    // Variables de red
    private NetworkVariable<bool> isRespawning = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    private NetworkVariable<int> playerTeam = new NetworkVariable<int>(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Referencias de componentes
    private PlayerNetwork playerNetwork;
    private PlayerAbilityController abilityController;
    private PlayerStats playerStats;
    private Collider playerCollider;
    private Renderer[] playerRenderers;
    private Material[] originalMaterials;
    
    // Variables para seguimiento
    private Vector3 lastValidPosition;
    private bool isInvulnerable = false;
    private int fallCount = 0;
    
    // Nueva variable para indicar si el respawn es por muerte (vs. caída)
    private bool isDeathRespawn = false;
    
    // Referencia al Game Manager para notificar muertes
    private MOBAGameManager gameManager;
    
    // Referencia a la cámara
    private MOBACamera playerCamera;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Obtener referencias
        playerNetwork = GetComponent<PlayerNetwork>();
        abilityController = GetComponent<PlayerAbilityController>();
        playerStats = GetComponent<PlayerStats>();
        playerCollider = GetComponent<Collider>();
        playerRenderers = GetComponentsInChildren<Renderer>();
        
        // Buscar la cámara del jugador
        if (IsOwner)
        {
            StartCoroutine(FindPlayerCamera());
        }
        
        // Guardar materiales originales
        if (playerRenderers.Length > 0)
        {
            originalMaterials = new Material[playerRenderers.Length];
            for (int i = 0; i < playerRenderers.Length; i++)
            {
                if (playerRenderers[i] != null && playerRenderers[i].material != null)
                {
                    originalMaterials[i] = playerRenderers[i].material;
                }
            }
        }
        
        // Suscribirse a cambios en la variable de respawn
        isRespawning.OnValueChanged += OnRespawningChanged;
        
        // Inicializar última posición válida
        lastValidPosition = transform.position;
        
        // Buscar referencia al Game Manager
        if (IsServer)
        {
            gameManager = FindObjectOfType<MOBAGameManager>();
        }
        
        Debug.Log($"[RespawnController] Inicializado para jugador {OwnerClientId}, Equipo {playerTeam.Value}");
    }
    
    private IEnumerator FindPlayerCamera()
    {
        // Esperar un poco para que la cámara se inicialice
        yield return new WaitForSeconds(0.5f);
        
        // Buscar todas las cámaras de tipo MOBA
        MOBACamera[] cameras = FindObjectsOfType<MOBACamera>();
        foreach (MOBACamera cam in cameras)
        {
            // Verificar si esta cámara tiene como objetivo a este jugador
            if (cam.GetTarget() == transform)
            {
                playerCamera = cam;
                Debug.Log("[RespawnController] Cámara del jugador encontrada");
                break;
            }
        }
        
        // Si no encontramos la cámara, intentar de nuevo más tarde
        if (playerCamera == null)
        {
            Debug.LogWarning("[RespawnController] No se encontró la cámara del jugador, reintentando en 1 segundo...");
            yield return new WaitForSeconds(1.0f);
            StartCoroutine(FindPlayerCamera());
        }
    }
    
    private void Update()
    {
        // Solo verificar caídas si no estamos ya en proceso de respawn
        if (!isRespawning.Value)
        {
            // Si estamos por encima de un umbral seguro, actualizar la última posición válida
            if (transform.position.y > fallThreshold + 2.0f)
            {
                lastValidPosition = transform.position;
            }
            
            // Verificar si el jugador cayó por debajo del umbral
            if (transform.position.y < fallThreshold)
            {
                if (IsServer)
                {
                    // Si somos el servidor, iniciar el proceso de respawn
                    isDeathRespawn = false; // No es una muerte, es una caída
                    StartRespawnProcess();
                }
                else if (IsOwner)
                {
                    // Si somos el cliente, notificar al servidor
                    NotifyFallServerRpc();
                }
            }
        }
    }
    
    [ServerRpc]
    private void NotifyFallServerRpc()
    {
        // Verificar que no estemos ya en respawn
        if (!isRespawning.Value)
        {
            isDeathRespawn = false; // Marcar como caída, no muerte
            StartRespawnProcess();
        }
    }
    
    // Método público para forzar un respawn (usado por PlayerStats cuando el jugador muere)
    public void ForceRespawn()
    {
        if (IsServer)
        {
            isDeathRespawn = true; // Marcar como respawn por muerte
            
            // IMPORTANTE: NO crear un jugador nuevo, simplemente utilizar el proceso de respawn
            StartRespawnProcess();
        }
        else
        {
            ForceRespawnServerRpc();
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void ForceRespawnServerRpc()
    {
        isDeathRespawn = true; // Marcar como respawn por muerte
        StartRespawnProcess();
    }
    
    private void StartRespawnProcess()
    {
        if (!IsServer) return;
        
        string reason = isDeathRespawn ? "murió" : "cayó de la plataforma";
        Debug.Log($"[RespawnController] Jugador {OwnerClientId} {reason}. Iniciando respawn...");
        
        // Activar el estado de respawn
        isRespawning.Value = true;
        
        // Incrementar contador de caídas (solo si es una caída real)
        if (!isDeathRespawn)
        {
            fallCount++;
        }
        
        // Notificar la caída/muerte al Game Manager si existe
        if (gameManager != null)
        {
            // Aquí podrías llamar a un método en el Game Manager para registrar la caída/muerte
            // Por ejemplo: gameManager.RegisterPlayerDeath(OwnerClientId, playerTeam.Value, isDeathRespawn);
        }
        
        // Mostrar efecto de muerte en la posición actual
        SpawnDeathEffectClientRpc(transform.position, isDeathRespawn);
        
        // Programar el respawn después de un delay
        StartCoroutine(RespawnAfterDelay());
    }
    
    private IEnumerator RespawnAfterDelay()
    {
        if (!IsServer) yield break;
        
        // Esperar el tiempo de respawn
        yield return new WaitForSeconds(respawnDelay);
        
        // Determinar punto de spawn basado en el equipo y si es muerte o caída
        Vector3 spawnPosition = GetRespawnPosition();
        
        // Teleportar al jugador a la posición de respawn
        TeleportPlayerClientRpc(spawnPosition);
        
        // NUEVO: Restablecer la salud y maná del jugador a los valores máximos
        if (playerStats != null)
        {
            // Restaurar salud al máximo
            playerStats.Heal(playerStats.MaxHealth);
            
            // Restaurar maná al máximo
            playerStats.RestoreMana(playerStats.MaxMana);
            
            Debug.Log($"[RespawnController] Restablecida salud y maná de jugador {OwnerClientId} a valores máximos");
        }
        
        // Breve pausa antes de reactivar al jugador
        yield return new WaitForSeconds(0.5f);
        
        // Desactivar estado de respawn y activar invulnerabilidad temporal
        isRespawning.Value = false;
        StartInvulnerabilityClientRpc();
    }
    
    private Vector3 GetRespawnPosition()
    {
        // Posición del centro del mapa para respawns por muerte
        Vector3 centerMapPosition = new Vector3(0f, 5f, 0f);
        
        // Si es un respawn por muerte, usar el centro del mapa
        if (isDeathRespawn)
        {
            Debug.Log($"[RespawnController] Respawn por muerte, usando centro del mapa");
            return centerMapPosition;
        }
        
        // Punto de spawn predeterminado (si no hay puntos de equipo)
        Vector3 defaultPosition = centerMapPosition;
        
        // Determinar array de spawn points según el equipo
        Transform[] spawnPoints = null;
        
        if (playerTeam.Value == 1 && team1SpawnPoints != null && team1SpawnPoints.Length > 0)
        {
            spawnPoints = team1SpawnPoints;
        }
        else if (playerTeam.Value == 2 && team2SpawnPoints != null && team2SpawnPoints.Length > 0)
        {
            spawnPoints = team2SpawnPoints;
        }
        
        // Si no hay puntos de spawn disponibles, usar posición predeterminada
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning($"[RespawnController] No hay puntos de spawn disponibles para equipo {playerTeam.Value}");
            return defaultPosition;
        }
        
        // Seleccionar un punto de spawn aleatorio para el equipo
        int spawnIndex = (int)OwnerClientId % spawnPoints.Length;
        Transform spawnPoint = spawnPoints[spawnIndex];
        
        // Verificar que el punto exista
        if (spawnPoint == null)
        {
            Debug.LogWarning($"[RespawnController] Punto de spawn seleccionado es nulo (Equipo {playerTeam.Value}, Índice {spawnIndex})");
            return defaultPosition;
        }
        
        return spawnPoint.position;
    }
    
    [ClientRpc]
    private void TeleportPlayerClientRpc(Vector3 position)
    {
        // NUEVO: Asegurar que todo el objeto esté activo
        gameObject.SetActive(true);
        
        // Teletransportar al jugador a la posición indicada
        transform.position = position;
        
        // NUEVO: Asegurar que todos los renderizadores estén activos
        Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true); // incluir inactivos
        foreach (var renderer in allRenderers)
        {
            renderer.enabled = true;
        }
        
        // Si tenemos Rigidbody, resetear su velocidad y asegurarnos que la física esté correcta
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            // Detener cualquier movimiento
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            
            // Asegurar que la gravedad esté activada
            rb.useGravity = true;
            rb.isKinematic = false;
            
            // Aplicar un pequeño impulso hacia arriba para evitar atravesar el suelo
            rb.AddForce(Vector3.up * 2f, ForceMode.Impulse);
        }
        
        // Asegurar que el collider esté habilitado
        if (playerCollider != null)
        {
            playerCollider.enabled = true;
        }
        
        // NUEVO: Actualizar la lista de renderizadores
        playerRenderers = GetComponentsInChildren<Renderer>();
        
        // NUEVO: Debug log
        Debug.Log($"[RespawnController] Jugador teleportado a {position}. Renderizadores encontrados: {playerRenderers.Length}");
        
        // Verificar suelo y ajustar posición si es necesario
        StartCoroutine(VerifyGroundCollision());
        
        // Centrar la cámara en el jugador si somos el propietario
        if (IsOwner && playerCamera != null)
        {
            StartCoroutine(CenterCameraNextFrame());
        }
        
        // Crear efecto visual de respawn
        if (respawnEffectPrefab != null)
        {
            GameObject effect = Instantiate(respawnEffectPrefab, position, Quaternion.identity);
            Destroy(effect, 3.0f);
        }
        
        Debug.Log($"[RespawnController] Jugador {OwnerClientId} reapareció en {position}");
        
        // NUEVO: Notificar al PlayerNetwork para sincronizar la posición con el servidor
        PlayerNetwork playerNet = GetComponent<PlayerNetwork>();
        if (playerNet != null && IsOwner)
        {
            playerNet.UpdatePositionServerRpc(position, transform.rotation);
        }
    }
    
    // NUEVO: Método auxiliar para forzar la visibilidad
    public void ForceVisibility()
    {
        // Asegurar que todo el objeto esté activo
        gameObject.SetActive(true);
        
        // Activar todos los renderizadores
        Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in allRenderers)
        {
            renderer.enabled = true;
        }
        
        // Actualizar la lista de renderizadores
        playerRenderers = GetComponentsInChildren<Renderer>();
        
        Debug.Log($"[RespawnController] Forzando visibilidad del jugador. Renderizadores activados: {allRenderers.Length}");
    }
    
    // NUEVO: Método para verificar la colisión con el suelo
    private IEnumerator VerifyGroundCollision()
    {
        // Esperar a que pase un frame para que la física se actualice
        yield return new WaitForFixedUpdate();
        
        // Verificar si hay suelo debajo del jugador mediante un raycast
        RaycastHit hit;
        float maxGroundCheckDistance = 50f;
        float groundOffset = 1.0f; // Altura sobre el suelo a mantener
        
        if (Physics.Raycast(transform.position, Vector3.down, out hit, maxGroundCheckDistance))
        {
            // Si estamos muy lejos del suelo, ajustar posición
            if (hit.distance > 5f)
            {
                // Posición ajustada: punto de impacto + offset en Y
                Vector3 adjustedPosition = hit.point + Vector3.up * groundOffset;
                transform.position = adjustedPosition;
                
                // Ajustar Rigidbody si existe
                Rigidbody rb = GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.velocity = Vector3.zero;
                    rb.position = adjustedPosition;
                }
                
                Debug.Log($"[RespawnController] Ajustando posición para evitar caída. Distancia al suelo: {hit.distance}");
            }
        }
        else
        {
            // No se encontró suelo, intentar reposicionar en el origen con un offset en Y
            Debug.LogWarning("[RespawnController] No se encontró suelo debajo del jugador. Reposicionando en origen.");
            
            // Posicionar en el origen con altura segura
            Vector3 safePosition = new Vector3(0f, 5f, 0f);
            transform.position = safePosition;
            
            // Ajustar Rigidbody si existe
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.position = safePosition;
            }
        }
        
        // Asegurar que el jugador no atraviese el suelo haciendo verificaciones adicionales
        for (int i = 0; i < 5; i++)
        {
            yield return new WaitForFixedUpdate();
            
            // Si empezamos a caer muy rápido, intentar corregir
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null && rb.velocity.y < -10f)
            {
                rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
                rb.AddForce(Vector3.up * 5f, ForceMode.Impulse);
                Debug.Log("[RespawnController] Corrigiendo caída rápida post-respawn");
            }
        }
    }
    
    // NUEVO: Método para centrar la cámara en el próximo frame
    private IEnumerator CenterCameraNextFrame()
    {
        yield return null; // Esperar un frame
        
        // Intentar encontrar la cámara de nuevo si no la tenemos
        if (playerCamera == null)
        {
            MOBACamera[] cameras = FindObjectsOfType<MOBACamera>();
            foreach (MOBACamera cam in cameras)
            {
                if (cam.GetTarget() == transform)
                {
                    playerCamera = cam;
                    break;
                }
            }
        }
        
        // Centrar la cámara en el jugador
        if (playerCamera != null)
        {
            // Para llamar al método CenterOnPlayer() en MOBACamera
            playerCamera.CenterOnPlayer();
            Debug.Log("[RespawnController] Cámara centrada en el jugador");
        }
        else
        {
            Debug.LogWarning("[RespawnController] No se pudo centrar la cámara (no encontrada)");
        }
    }
    
    [ClientRpc]
    private void StartInvulnerabilityClientRpc()
    {
        // Iniciar tiempo de invulnerabilidad
        StartCoroutine(InvulnerabilityRoutine());
    }
    
    private IEnumerator InvulnerabilityRoutine()
    {
        // Activar estado de invulnerabilidad
        isInvulnerable = true;
        
        // NUEVO: Asegurarnos que los renderizadores estén activos
        if (playerRenderers != null)
        {
            foreach (var renderer in playerRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }
        }
        
        // Aplicar material fantasma si existe
        if (ghostMaterial != null && playerRenderers != null)
        {
            foreach (var renderer in playerRenderers)
            {
                if (renderer != null)
                {
                    // Guardar material original de nuevo, por si acaso
                    int index = System.Array.IndexOf(playerRenderers, renderer);
                    if (index >= 0 && index < originalMaterials.Length)
                    {
                        originalMaterials[index] = renderer.material;
                    }
                    
                    // Aplicar material fantasma
                    renderer.material = ghostMaterial;
                }
            }
        }
        
        // Debug log para verificar estado
        Debug.Log($"[RespawnController] Iniciando invulnerabilidad. Renderizadores activos: {playerRenderers?.Length ?? 0}");
        
        // Esperar el tiempo de invulnerabilidad
        yield return new WaitForSeconds(invulnerabilityTime);
        
        // Restaurar materiales originales
        if (originalMaterials != null && playerRenderers != null)
        {
            for (int i = 0; i < playerRenderers.Length; i++)
            {
                if (playerRenderers[i] != null && i < originalMaterials.Length && originalMaterials[i] != null)
                {
                    playerRenderers[i].material = originalMaterials[i];
                    
                    // NUEVO: Asegurar que el renderizador permanezca activado
                    playerRenderers[i].enabled = true;
                }
            }
        }
        
        // Desactivar estado de invulnerabilidad
        isInvulnerable = false;
        
        Debug.Log($"[RespawnController] Invulnerabilidad terminada para jugador {OwnerClientId}");
    }
    
    [ClientRpc]
    private void SpawnDeathEffectClientRpc(Vector3 position, bool isDeathEffect = false)
    {
        // Crear un efecto más dramático para muerte vs caída
        GameObject effect = null;
        
        if (isDeathEffect)
        {
            // Crear explosión de muerte (más dramática)
            effect = new GameObject("PlayerDeathExplosion");
            effect.transform.position = position + Vector3.up;
            
            // Añadir sistema de partículas para muerte
            ParticleSystem particles = effect.AddComponent<ParticleSystem>();
            
            // Configurar ajustes principales de partículas
            var main = particles.main;
            main.startSpeed = 8.0f;
            main.startSize = 0.8f;
            main.startLifetime = 2.0f;
            main.startColor = Color.red;
            
            // Configurar forma de emisión
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1.0f;
            
            // Configurar burst de partículas
            var emission = particles.emission;
            emission.rateOverTime = 0;
            var burst = new ParticleSystem.Burst(0.0f, 100);
            emission.SetBurst(0, burst);
            
            // Añadir color a lo largo de la vida para un efecto más dramático
            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            
            // Crear gradiente de rojo a naranja a transparente
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] 
                { 
                    new GradientColorKey(Color.red, 0.0f),
                    new GradientColorKey(new Color(1f, 0.6f, 0f), 0.5f),
                    new GradientColorKey(new Color(0.7f, 0.3f, 0f), 1.0f)
                },
                new GradientAlphaKey[] 
                { 
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(0.8f, 0.5f),
                    new GradientAlphaKey(0.0f, 1.0f)
                }
            );
            colorOverLifetime.color = gradient;
            
            // Añadir luz para efecto dramático
            Light light = effect.AddComponent<Light>();
            light.color = Color.red;
            light.intensity = 5.0f;
            light.range = 10.0f;
            
            // Hacer que la luz parpadee y se desvanezca
            StartCoroutine(FadeLight(light, 2.0f));
            
            // Si somos el propietario, sacudir la cámara
            if (IsOwner)
            {
                // Intentar encontrar la cámara para sacudirla
                ShakeCameraOnDeath();
            }
        }
        else
        {
            // Para efectos que no son muerte (caídas), usar un efecto más simple
            // Podrías añadir un efecto simple aquí o reutilizar uno existente
            if (deathEffectPrefab != null)
            {
                effect = Instantiate(deathEffectPrefab, position, Quaternion.identity);
            }
        }
        
        // Destruir el efecto después de un tiempo
        if (effect != null)
        {
            Destroy(effect, 3.0f);
        }
    }
    
    // Encontrar la cámara del jugador y sacudirla
    private void ShakeCameraOnDeath()
    {
        // Método 1: Intentar con Main Camera
        Camera camera = Camera.main;
        if (camera != null)
        {
            MOBACamera mobaCamera = camera.GetComponent<MOBACamera>();
            if (mobaCamera != null)
            {
                mobaCamera.ShakeCamera(1.0f, 1.0f);
                return;
            }
        }
        
        // Método 2: Buscar todas las MOBACameras y verificar su objetivo
        MOBACamera[] allCameras = FindObjectsOfType<MOBACamera>();
        foreach (MOBACamera mobaCam in allCameras)
        {
            if (mobaCam.GetTarget() == transform)
            {
                mobaCam.ShakeCamera(1.0f, 1.0f);
                return;
            }
        }
    }
    
    // Efecto de fade de luz
    private IEnumerator FadeLight(Light light, float duration)
    {
        float elapsed = 0f;
        float startIntensity = light.intensity;
        float flickerSpeed = 20f;
        
        while (elapsed < duration)
        {
            // Añadir efecto de parpadeo
            float flickerFactor = Mathf.PerlinNoise(elapsed * flickerSpeed, 0) * 0.5f + 0.5f;
            
            // Calcular factor de desvanecimiento
            float fadeOutFactor = 1.0f - (elapsed / duration);
            
            // Aplicar ambos efectos
            light.intensity = startIntensity * flickerFactor * fadeOutFactor;
            
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        // Asegurar que la luz está completamente apagada al final
        light.intensity = 0;
    }
    
    // Reacción a cambios en la variable de red isRespawning
    private void OnRespawningChanged(bool oldValue, bool newValue)
    {
        if (newValue)
        {
            // Entrando en estado de respawn
            DisablePlayerControl();
        }
        else
        {
            // Saliendo del estado de respawn
            EnablePlayerControl();
        }
    }
    
    private void DisablePlayerControl()
    {
        // Desactivar controles del jugador y colisiones durante el respawn
        
        // Solo enviar notificación de UI al propietario
        if (IsOwner)
        {
            Debug.Log("[RespawnController] ¡Has caído! Reapareciendo...");
            // Aquí podrías mostrar un mensaje en pantalla o UI
        }
        
        // Desactivar colisiones
        if (playerCollider != null)
        {
            playerCollider.enabled = false;
        }
        
        // Desactivar el controlador de habilidades
        if (abilityController != null)
        {
            abilityController.enabled = false;
        }
        
        // Desactivar el movimiento del jugador
        if (playerNetwork != null)
        {
            playerNetwork.SetPlayerControlEnabled(false);
        }
    }
    
    private void EnablePlayerControl()
    {
        // NUEVO: Forzar visibilidad
        ForceVisibility();
        
        // Reactivar colisiones explícitamente
        if (playerCollider != null)
        {
            playerCollider.enabled = true;
        }
        
        // Asegurar que la física esté correctamente configurada
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.velocity = Vector3.zero;
            
            // Asegurarnos que las constraints estén correctas (solo congelar rotación)
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }
        
        // Reactivar el controlador de habilidades
        if (abilityController != null)
        {
            abilityController.enabled = true;
        }
        
        // Reactivar el movimiento del jugador
        if (playerNetwork != null)
        {
            playerNetwork.SetPlayerControlEnabled(true);
            
            // NUEVO: Forzar sincronización de posición con el servidor
            if (IsOwner)
            {
                playerNetwork.UpdatePositionServerRpc(transform.position, transform.rotation);
            }
        }
        
        // Solo enviar notificación de UI al propietario
        if (IsOwner)
        {
            Debug.Log("[RespawnController] ¡Control restaurado! Puedes moverte nuevamente.");
            // Aquí podrías mostrar un mensaje en pantalla o UI
        }
    }
    
    // Método público para verificar si el jugador está en estado de invulnerabilidad
    public bool IsInvulnerable()
    {
        return isInvulnerable;
    }
    
    // Método público para verificar si el jugador está en proceso de respawn
    public bool IsRespawning()
    {
        return isRespawning.Value;
    }
    
    // Método para establecer el equipo del jugador (llamado por el Game Manager)
    public void SetTeam(int team)
    {
        if (IsServer)
        {
            playerTeam.Value = team;
        }
        else
        {
            SetTeamServerRpc(team);
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void SetTeamServerRpc(int team)
    {
        playerTeam.Value = team;
    }
    
    // Método para obtener el número de caídas del jugador
    public int GetFallCount()
    {
        return fallCount;
    }
}