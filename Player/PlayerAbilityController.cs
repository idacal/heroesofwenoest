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
    
    private void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        characterController = GetComponent<CharacterController>();
        rb = GetComponent<Rigidbody>();
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Buscar Hero y solicitar inicialización
        Hero heroComponent = GetComponent<Hero>();
        if (heroComponent != null)
        {
            Debug.Log($"[PlayerAbilityController] OnNetworkSpawn - Delegando inicialización al componente Hero");
            // Esperar un breve momento para asegurar que todo esté listo
            StartCoroutine(DelayedInitialization(heroComponent));
        }
        else
        {
            Debug.LogWarning("[PlayerAbilityController] No se encontró Hero - Inicializando con habilidades por defecto");
            // Inicializar habilidades por defecto
            InitializeDefaultAbilities();
        }
    }
    
    private IEnumerator DelayedInitialization(Hero heroComponent)
    {
        // Esperar un momento para asegurar que todo está listo
        yield return new WaitForSeconds(0.5f);
        
        Debug.Log("[PlayerAbilityController] Iniciando inicialización de habilidades de héroe...");
        heroComponent.InitializeHeroAbilities();
        
        // Verificar si tenemos habilidades
        yield return new WaitForSeconds(0.5f);
        if (activeAbilities.Count == 0)
        {
            Debug.LogError("[PlayerAbilityController] ¡No se inicializaron habilidades! Reintentando...");
            heroComponent.InitializeHeroAbilities();
        }
    }
    
    private void InitializeDefaultAbilities()
    {
        Debug.Log("[PlayerAbilityController] Inicializando habilidades por defecto");
        
        // Limpiar cualquier habilidad existente
        RemoveAllAbilities();
        
        // Añadir habilidades básicas
        AddAbility<DashAbility>();
        AddAbility<StrongJumpAbility>();
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
        
        // Comprobar activación de habilidades
        for (int i = 0; i < activeAbilities.Count; i++)
        {
            BaseAbility ability = activeAbilities[i];
            
            // Verificar si la habilidad existe
            if (ability == null)
            {
                activeAbilities.RemoveAt(i);
                i--;
                continue;
            }
            
            // Verificar si se presionó la tecla de activación
            if (Input.GetKeyDown(ability.activationKey))
            {
                if (ability.CanActivate())
                {
                    Debug.Log($"Activando habilidad {ability.abilityName} con tecla {ability.activationKey}");
                    UseAbilityServerRpc(i);
                }
                else if (!ability.isReady)
                {
                    Debug.Log($"Habilidad {ability.abilityName} en cooldown: {ability.GetRemainingCooldown()} segundos");
                }
                else if (playerStats.CurrentMana < ability.manaCost)
                {
                    ability.OnFailed();
                    Debug.Log($"Maná insuficiente para {ability.abilityName}. Necesitas {ability.manaCost}, tienes {playerStats.CurrentMana}");
                }
            }
            
            // Actualizar lógica de la habilidad cada frame
            ability.UpdateAbility();
        }
        
        // Manejar movimiento hacia posición objetivo
        if (isMoving && !isInImpactPause)
        {
            MoveTowardsTarget();
        }
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
        // Buscar si ya existe
        foreach (var ability in activeAbilities)
        {
            if (ability is T existingAbility)
            {
                Debug.Log($"[PlayerAbilityController] La habilidad {typeof(T).Name} ya existe");
                return existingAbility as T;
            }
        }
        
        // Crear nueva habilidad
        T newAbility = gameObject.AddComponent<T>();
        
        // Verificar si se creó correctamente
        if (newAbility == null)
        {
            Debug.LogError($"[PlayerAbilityController] Error al crear habilidad {typeof(T).Name}");
            return null;
        }
        
        // Inicializar la habilidad
        newAbility.Initialize(this);
        activeAbilities.Add(newAbility);
        
        Debug.Log($"[PlayerAbilityController] Habilidad {typeof(T).Name} añadida correctamente");
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
                
                return true;
            }
        }
        
        return false;
    }
    
    public void RemoveAllAbilities()
    {
        // Limpiar todas las habilidades
        foreach (var ability in GetComponents<BaseAbility>())
        {
            if (ability != null)
            {
                ability.Cleanup();
                Destroy(ability);
            }
        }
        
        // Limpiar la lista
        activeAbilities.Clear();
        
        Debug.Log("[PlayerAbilityController] Todas las habilidades han sido eliminadas");
    }
    
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
    
    // Método para verificar si hay alguna pausa de habilidad activa
    private bool IsInAbilityPause()
    {
        // Verificar pausa explícitamente
        if (isInImpactPause)
        {
            return true;
        }
        
        // Verificar directamente el estado de inmobilización de StrongJump
        StrongJumpAbility sjAbility = GetComponent<StrongJumpAbility>();
        if (sjAbility != null && sjAbility.IsImmobilized)
        {
            return true;
        }
        
        return false;
    }
}