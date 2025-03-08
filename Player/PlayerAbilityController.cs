using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using PlayerAbilities;

public class PlayerAbilityController : NetworkBehaviour
{
    [Header("Configuración del Mouse")]
    [SerializeField] public GameObject clickIndicatorPrefab; // Indicador visual para el clic
    
    // Lista de habilidades activas
    private List<BaseAbility> activeAbilities = new List<BaseAbility>();
    
    // Referencias
    private PlayerStats playerStats;
    private CharacterController characterController;
    private Rigidbody rb;
    
    // Variables para movimiento con clic
    private Vector3 targetPosition;
    private bool isMoving = false;
    
    // Cambiado a público para permitir acceso desde otras clases
    public bool isInImpactPause { get; private set; } = false;
    
    // Abrir acceso público a las habilidades más comunes para consultas rápidas
    private DashAbility dashAbility;
    private EarthquakeAbility earthquakeAbility;
    
    private void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        characterController = GetComponent<CharacterController>();
        rb = GetComponent<Rigidbody>();
        
        if (rb == null && characterController == null)
        {
            Debug.LogWarning("No se encontró CharacterController ni Rigidbody. Las habilidades podrían no funcionar correctamente.");
        }
    }
    
    private void Start()
    {
        // Si estamos en multiplayer, esperar a la inicialización de la red
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer || IsClient)
            {
                // Se inicializa en OnNetworkSpawn con un pequeño retraso
            }
        }
        else
        {
            // Modo fuera de línea
            InitializeDefaultAbilities();
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Esperar un breve momento para que el NetworkManager esté completamente inicializado
        StartCoroutine(DelayedInitialization());
    }

    private IEnumerator DelayedInitialization()
    {
        yield return new WaitForSeconds(0.2f);
        InitializeDefaultAbilities();
    }
    
    // Localiza el método InitializeDefaultAbilities() en PlayerAbilityController.cs
// y modifícalo para incluir la nueva habilidad:

private void InitializeDefaultAbilities()
{
    // Limpiar cualquier habilidad existente para evitar duplicados
    RemoveAllAbilities();
    
    // Añadir habilidades por defecto
    dashAbility = AddAbility<DashAbility>();
    //earthquakeAbility = AddAbility<EarthquakeAbility>();
    
    // Añadir la nueva habilidad StrongJump
    AddAbility<StrongJumpAbility>();
    
    // Si quieres añadir más habilidades por defecto, hazlo aquí
    // AddAbility<OtraHabilidad>();
    
    // Inicializar explícitamente cada habilidad con este NetworkBehaviour
    foreach (var ability in activeAbilities)
    {
        ability.Initialize(this);
    }
    
    // Notificar que se inicializaron correctamente
    if (IsOwner)
    {
        Debug.Log("[PlayerAbilityController] Habilidades inicializadas correctamente");
    }
}
    
    private void Update()
    {
        // Solo procesar entrada para el propietario del objeto
        if (!IsOwner) return;
        
        // Procesar clic para movimiento solo si no estamos en pausa de impacto
        if (!isInImpactPause)
        {
            HandleMouseInput();
        }
        else
        {
            // Si estamos en pausa de impacto, ignorar cualquier entrada de movimiento
            if (Input.GetMouseButtonDown(1))
            {
                // Consumir el evento pero no hacer nada
                Debug.Log("Movimiento bloqueado durante la pausa de impacto");
            }
        }
        
        // Comprobar activación de habilidades
        foreach (var ability in activeAbilities)
        {
            // Verificar si se presionó la tecla de activación
            if (Input.GetKeyDown(ability.activationKey))
            {
                // Verificar si la habilidad se puede activar
                if (ability.CanActivate())
                {
                    Debug.Log($"Activando habilidad {ability.abilityName} a través de la tecla {ability.activationKey}");
                    UseAbilityServerRpc(activeAbilities.IndexOf(ability));
                }
                else if (!ability.isReady)
                {
                    Debug.Log($"Habilidad {ability.abilityName} en enfriamiento. Tiempo restante: {ability.GetRemainingCooldown()} segundos");
                }
                else if (playerStats.CurrentMana < ability.manaCost)
                {
                    ability.OnFailed();
                    Debug.Log($"Maná insuficiente para {ability.abilityName}. Necesitas {ability.manaCost}, tienes {playerStats.CurrentMana}");
                }
                else
                {
                    // NUEVO: Verificar si es el Earthquake para mostrar feedback sobre requisito de movimiento
                    if (ability is EarthquakeAbility earthquakeAbility)
                    {
                        if (!earthquakeAbility.IsMovingFastEnough())
                        {
                            // Mostrar feedback específico para requisito de movimiento
                            Debug.Log($"¡Necesitas estar en movimiento para usar {ability.abilityName}!");
                            
                            // Opcional: Efecto visual o sonoro para indicar el requisito
                            ShowRequirementFeedback("Muévete para usar Terremoto");
                        }
                    }
                    else
                    {
                        // Otro tipo de fallo no específico
                        Debug.Log($"No se puede activar {ability.abilityName} por razones desconocidas");
                    }
                }
            }
            
            // Actualizar lógica de la habilidad cada frame
            ability.UpdateAbility();
        }
        
        // Si estamos en movimiento y no hay ningún estado especial activo
        if (isMoving && !isInImpactPause && !IsPerformingAnyAbility())
        {
            MoveTowardsTarget();
        }
    }
    
    // NUEVO: Método para mostrar feedback visual o de audio cuando no se cumplen requisitos
    private void ShowRequirementFeedback(string message)
    {
        // Aquí puedes implementar efectos visuales, sonidos, o mensajes en pantalla
        // Por ejemplo, podrías mostrar un texto flotante cerca del jugador
        
        Debug.Log($"[REQUISITO] {message}");
        
        // Si tienes un sistema de UI, puedes usarlo para mostrar un mensaje temporal
        // Por ejemplo:
        // UIManager.ShowTemporaryMessage(message, 2.0f);
        
        // También podrías reproducir un sonido de "error" o "requisito no cumplido"
        // AudioManager.PlaySound("requirement_not_met");
    }
    
    private bool IsPerformingAnyAbility()
    {
        // Verificar si el jugador está ejecutando alguna habilidad que bloquee el movimiento
        if (dashAbility != null && dashAbility.IsDashing)
            return true;
            
        if (earthquakeAbility != null && (earthquakeAbility.IsJumping || earthquakeAbility.IsFalling))
            return true;
            
        return false;
    }
    
    private void HandleMouseInput()
    {
        // Detectar clic derecho para movimiento
        if (Input.GetMouseButtonDown(1))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit))
            {
                // Si estamos en pausa de impacto, no permitir movimiento
                if (isInImpactPause)
                {
                    Debug.Log("No se puede mover durante la pausa de impacto");
                    return;
                }
                
                // Guardar posición de destino
                targetPosition = hit.point;
                isMoving = true;
                
                // Mostrar indicador visual de clic
                if (clickIndicatorPrefab != null)
                {
                    // Instanciar en posición ligeramente elevada para evitar problemas de z-fighting
                    Vector3 spawnPosition = hit.point + new Vector3(0, 0.1f, 0);
                    Instantiate(clickIndicatorPrefab, spawnPosition, Quaternion.identity);
                }
                
                // Enviar evento de movimiento al servidor si estamos en red
                SetMovementTargetServerRpc(targetPosition);
            }
        }
    }
    
    private void MoveTowardsTarget()
    {
        // No moverse si estamos en pausa de impacto
        if (isInImpactPause) return;
        
        // Verificar si hemos llegado al destino
        if (Vector3.Distance(transform.position, targetPosition) < 0.1f)
        {
            isMoving = false;
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
            }
        }
    }
    
    // Métodos para agregar/eliminar habilidades dinámicamente
    
    public T AddAbility<T>() where T : BaseAbility
    {
        // Verificar si ya existe esta habilidad
        foreach (var ability in activeAbilities)
        {
            if (ability is T)
            {
                Debug.LogWarning($"La habilidad {typeof(T).Name} ya está añadida al jugador");
                return ability as T;
            }
        }
        
        // Añadir la nueva habilidad
        T newAbility = gameObject.AddComponent<T>();
        newAbility.Initialize(this);
        activeAbilities.Add(newAbility);
        
        // Informar de la adición de la habilidad
        if (IsOwner)
        {
            Debug.Log($"Nueva habilidad adquirida: {newAbility.abilityName}");
        }
        
        return newAbility;
    }
    
    public bool RemoveAbility<T>() where T : BaseAbility
    {
        for (int i = 0; i < activeAbilities.Count; i++)
        {
            if (activeAbilities[i] is T)
            {
                BaseAbility ability = activeAbilities[i];
                activeAbilities.RemoveAt(i);
                
                // Limpiar recursos
                ability.Cleanup();
                Destroy(ability);
                
                if (IsOwner)
                {
                    Debug.Log($"Habilidad removida: {ability.abilityName}");
                }
                
                return true;
            }
        }
        
        return false;
    }
    
    public void RemoveAllAbilities()
    {
        foreach (var ability in activeAbilities)
        {
            ability.Cleanup();
            Destroy(ability);
        }
        
        activeAbilities.Clear();
        dashAbility = null;
        earthquakeAbility = null;
    }
    
    // Métodos de red
    
    [ServerRpc]
    private void UseAbilityServerRpc(int abilityIndex)
    {
        if (abilityIndex < 0 || abilityIndex >= activeAbilities.Count)
        {
            Debug.LogError($"Índice de habilidad fuera de rango: {abilityIndex}");
            return;
        }

        BaseAbility ability = activeAbilities[abilityIndex];
        
        // Solo el servidor verifica el costo de maná
        if (playerStats.UseMana(ability.manaCost))
        {
            // La habilidad fue exitosa, procesarla
            ProcessAbilitySuccessClientRpc(abilityIndex);
        }
        else
        {
            // Maná insuficiente
            AbilityFailedClientRpc(abilityIndex);
        }
    }
    
    [ClientRpc]
    private void ProcessAbilitySuccessClientRpc(int abilityIndex)
    {
        if (abilityIndex < 0 || abilityIndex >= activeAbilities.Count)
            return;
        
        BaseAbility ability = activeAbilities[abilityIndex];
        
        // Activar la habilidad
        ability.Activate();
        
        // Iniciar cooldown
        StartCoroutine(ability.StartCooldown());
    }
    
    [ClientRpc]
    private void AbilityFailedClientRpc(int abilityIndex)
    {
        if (!IsOwner) return;
        
        if (abilityIndex < 0 || abilityIndex >= activeAbilities.Count)
            return;
            
        activeAbilities[abilityIndex].OnFailed();
    }
    
    [ServerRpc]
    private void SetMovementTargetServerRpc(Vector3 position)
    {
        // No permitir establecer destino durante la pausa
        if (isInImpactPause) return;
        
        // Sincronizar la posición de destino en todos los clientes
        SetMovementTargetClientRpc(position);
    }
    
    [ClientRpc]
    private void SetMovementTargetClientRpc(Vector3 position)
    {
        // No permitir establecer destino durante la pausa
        if (isInImpactPause) return;
        
        // Solo actualizar si no somos el propietario (el propietario ya lo ha actualizado)
        if (!IsOwner)
        {
            targetPosition = position;
            isMoving = true;
        }
    }
    
    // Métodos públicos para acceso desde otras clases
    
    public int GetAbilityCount()
    {
        return activeAbilities.Count;
    }
    
    public BaseAbility GetAbility(int index)
    {
        if (index >= 0 && index < activeAbilities.Count)
            return activeAbilities[index];
        return null;
    }
    
    public void SetInImpactPause(bool pause)
    {
        isInImpactPause = pause;
    }
    
    public bool IsMoving()
    {
        return isMoving;
    }
    
    public Vector3 GetTargetPosition()
    {
        return targetPosition;
    }
    
    // Método añadido para compatibilidad con código existente que necesite
    // acceder al estado de pausa de impacto
    public bool IsInImpactPause()
    {
        return isInImpactPause;
    }
}