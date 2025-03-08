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
                if (playerRenderers[i].material != null)
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
            StartRespawnProcess();
        }
    }
    
    private void StartRespawnProcess()
    {
        if (!IsServer) return;
        
        Debug.Log($"[RespawnController] Jugador {OwnerClientId} cayó de la plataforma. Iniciando respawn...");
        
        // Activar el estado de respawn
        isRespawning.Value = true;
        
        // Incrementar contador de caídas
        fallCount++;
        
        // Notificar la caída/muerte al Game Manager si existe
        if (gameManager != null)
        {
            // Aquí podrías llamar a un método en el Game Manager para registrar la caída
            // Por ejemplo: gameManager.RegisterPlayerFall(OwnerClientId, playerTeam.Value);
        }
        
        // Mostrar efecto de muerte en la posición actual
        SpawnDeathEffectClientRpc(transform.position);
        
        // Programar el respawn después de un delay
        StartCoroutine(RespawnAfterDelay());
    }
    
    private IEnumerator RespawnAfterDelay()
    {
        if (!IsServer) yield break;
        
        // Esperar el tiempo de respawn
        yield return new WaitForSeconds(respawnDelay);
        
        // Determinar punto de spawn basado en el equipo
        Vector3 spawnPosition = GetRespawnPosition();
        
        // Teleportar al jugador a la posición de respawn
        TeleportPlayerClientRpc(spawnPosition);
        
        // Breve pausa antes de reactivar al jugador
        yield return new WaitForSeconds(0.5f);
        
        // Desactivar estado de respawn y activar invulnerabilidad temporal
        isRespawning.Value = false;
        StartInvulnerabilityClientRpc();
    }
    
    private Vector3 GetRespawnPosition()
    {
        // Punto de spawn predeterminado
        Vector3 defaultPosition = new Vector3(0f, 5f, 0f);
        
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
    private void SpawnDeathEffectClientRpc(Vector3 position)
    {
        // Crear efecto visual de muerte/caída
        if (deathEffectPrefab != null)
        {
            GameObject effect = Instantiate(deathEffectPrefab, position, Quaternion.identity);
            Destroy(effect, 3.0f); // Destruir después de 3 segundos
        }
    }
    
    [ClientRpc]
    private void TeleportPlayerClientRpc(Vector3 position)
    {
        // Teleportar al jugador a la posición indicada
        transform.position = position;
        
        // Si tenemos Rigidbody, resetear su velocidad
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        
        // NUEVO: Centrar la cámara en el jugador si somos el propietario
        if (IsOwner && playerCamera != null)
        {
            // Invocar en el próximo frame para darle tiempo al jugador de posicionarse
            StartCoroutine(CenterCameraNextFrame());
        }
        
        // Crear efecto visual de respawn
        if (respawnEffectPrefab != null)
        {
            GameObject effect = Instantiate(respawnEffectPrefab, position, Quaternion.identity);
            Destroy(effect, 3.0f);
        }
        
        Debug.Log($"[RespawnController] Jugador {OwnerClientId} reapareció en {position}");
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
        
        // Aplicar material fantasma si existe
        if (ghostMaterial != null && playerRenderers != null)
        {
            foreach (var renderer in playerRenderers)
            {
                renderer.material = ghostMaterial;
            }
        }
        
        // Esperar el tiempo de invulnerabilidad
        yield return new WaitForSeconds(invulnerabilityTime);
        
        // Restaurar materiales originales
        if (originalMaterials != null && playerRenderers != null)
        {
            for (int i = 0; i < playerRenderers.Length; i++)
            {
                if (i < originalMaterials.Length && originalMaterials[i] != null)
                {
                    playerRenderers[i].material = originalMaterials[i];
                }
            }
        }
        
        // Desactivar estado de invulnerabilidad
        isInvulnerable = false;
        
        Debug.Log($"[RespawnController] Invulnerabilidad terminada para jugador {OwnerClientId}");
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
        // Reactivar controles del jugador y colisiones
        
        // Reactivar colisiones
        if (playerCollider != null)
        {
            playerCollider.enabled = true;
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
    
    // Método para forzar un respawn (útil para testing o eventos de juego)
    public void ForceRespawn()
    {
        if (IsServer)
        {
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
        StartRespawnProcess();
    }
}