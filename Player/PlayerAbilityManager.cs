using Unity.Netcode;
using UnityEngine;
using System;
using System.Collections.Generic;
using PlayerAbilities;
using System.Collections;

public class PlayerAbilityManager : NetworkBehaviour
{
    [Header("Configuración")]
    [SerializeField] public GameObject clickIndicatorPrefab;
    
    [Header("Depuración")]
    [SerializeField] private bool showDebugMessages = true;
    [SerializeField] private float syncInterval = 5f; // Segundos entre sincronizaciones completas
    
    // Lista de habilidades activas
    private List<BaseAbility> abilities = new List<BaseAbility>();
    
    // Mapa de slots a habilidades (para UI)
    private Dictionary<int, BaseAbility> abilitySlots = new Dictionary<int, BaseAbility>();
    
    // Referencias
    private PlayerStats playerStats;
    private PlayerNetwork playerNetwork;
    
    // Variables para seguimiento de estado
    public bool isInImpactPause { get; private set; } = false;
    
    // Para compatibilidad con sistema antiguo
    private PlayerAbility legacyAbilityComponent;
    
    private void Awake()
    {
        // Obtener referencias
        playerStats = GetComponent<PlayerStats>();
        playerNetwork = GetComponent<PlayerNetwork>();
        legacyAbilityComponent = GetComponent<PlayerAbility>();
        
        if (playerStats == null)
        {
            Debug.LogError("[PlayerAbilityManager] No se encontró componente PlayerStats");
        }
        
        if (playerNetwork == null)
        {
            Debug.LogError("[PlayerAbilityManager] No se encontró componente PlayerNetwork");
        }
        
        // Verificar componentes antiguos para compatibilidad
        if (legacyAbilityComponent != null && showDebugMessages)
        {
            Debug.Log("[PlayerAbilityManager] Se encontró componente antiguo PlayerAbility. Se mantendrá para compatibilidad.");
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Activar procesamiento de entrada solo para el propietario
        if (IsOwner)
        {
            enabled = true;
            
            // Iniciar sincronización periódica si somos el cliente local
            if (!IsServer)
            {
                StartCoroutine(RequestSyncCoroutine());
            }
        }
        else if (!IsServer)
        {
            // Si no somos ni propietario ni servidor, deshabilitamos
            enabled = false;
        }
        
        // Si somos el servidor, iniciar sincronización periódica
        if (IsServer)
        {
            StartCoroutine(PeriodicSyncCoroutine());
        }
        
        // Buscar Hero y solicitar inicialización
        Hero heroComponent = GetComponent<Hero>();
        if (heroComponent != null)
        {
            if (showDebugMessages)
            {
                Debug.Log($"[PlayerAbilityManager] OnNetworkSpawn - Delegando inicialización al componente Hero");
            }
            // Esperar un breve momento para asegurar que todo esté listo
            StartCoroutine(DelayedInitialization(heroComponent));
        }
    }
    
    private IEnumerator DelayedInitialization(Hero heroComponent)
    {
        // Esperar un momento para asegurar que todo está listo
        yield return new WaitForSeconds(0.5f);
        
        if (showDebugMessages)
        {
            Debug.Log("[PlayerAbilityManager] Iniciando inicialización de habilidades de héroe...");
        }
        heroComponent.InitializeHeroAbilities();
        
        // Verificar si tenemos habilidades
        yield return new WaitForSeconds(0.5f);
        if (abilities.Count == 0)
        {
            Debug.LogWarning("[PlayerAbilityManager] ¡No se inicializaron habilidades! Iniciando habilidades por defecto...");
            InitializeDefaultAbilities();
        }
    }
    
    private void InitializeDefaultAbilities()
    {
        if (showDebugMessages)
        {
            Debug.Log("[PlayerAbilityManager] Inicializando habilidades por defecto");
        }
        
        // Limpiar cualquier habilidad existente
        RemoveAllAbilities();
        
        // Añadir habilidades básicas
        AddAbility<DashAbility>(0);
        AddAbility<StrongJumpAbility>(1);
    }
    
    private void Update()
    {
        // Solo procesar entrada para el propietario del objeto
        if (!IsOwner) return;
        
        // Procesar activación de habilidades
        foreach (var ability in abilities)
        {
            // Verificar si se presionó la tecla de activación
            if (Input.GetKeyDown(ability.activationKey))
            {
                TryActivateAbility(ability);
            }
            
            // Actualizar lógica de la habilidad cada frame
            ability.UpdateAbility();
        }
    }
    
    private void TryActivateAbility(BaseAbility ability)
    {
        if (ability.CanActivate())
        {
            // Enviar solicitud de activación al servidor
            ActivateAbilityServerRpc(abilities.IndexOf(ability));
        }
        else
        {
            // Proporcionar feedback sobre por qué no se puede usar la habilidad
            if (!ability.isReady)
            {
                if (showDebugMessages)
                {
                    Debug.Log($"Habilidad {ability.abilityName} en cooldown: {ability.GetRemainingCooldown():F1}s");
                }
            }
            else if (playerStats != null && playerStats.CurrentMana < ability.manaCost)
            {
                if (showDebugMessages)
                {
                    Debug.Log($"No hay suficiente maná para {ability.abilityName}. Necesitas {ability.manaCost}, tienes {playerStats.CurrentMana:F1}");
                }
            }
        }
    }
    
    [ServerRpc]
    private void ActivateAbilityServerRpc(int abilityIndex)
    {
        if (abilityIndex < 0 || abilityIndex >= abilities.Count)
            return;
            
        BaseAbility ability = abilities[abilityIndex];
        
        // Verificar si hay suficiente maná
        if (playerStats.UseMana(ability.manaCost))
        {
            // Activar habilidad en todos los clientes
            ActivateAbilityClientRpc(abilityIndex);
        }
    }
    
    [ClientRpc]
    private void ActivateAbilityClientRpc(int abilityIndex)
    {
        if (abilityIndex < 0 || abilityIndex >= abilities.Count)
            return;
            
        BaseAbility ability = abilities[abilityIndex];
        
        // Activar la habilidad a través del método de red
        ability.ActivateNetworked();
        
        // Sincronizar con PlayerAbility si existe (compatibilidad)
        SyncLegacySystem();
    }
    
    // Añadir una nueva habilidad con slot opcional
    public T AddAbility<T>(int slot = -1) where T : BaseAbility
    {
        // Verificar si la habilidad ya existe
        foreach (var ability in abilities)
        {
            if (ability is T)
            {
                if (showDebugMessages)
                {
                    Debug.LogWarning($"La habilidad {typeof(T).Name} ya está añadida al jugador");
                }
                
                // Si se especificó un slot, asignar a ese slot
                if (slot >= 0)
                {
                    if (abilitySlots.ContainsKey(slot))
                    {
                        abilitySlots[slot] = ability;
                    }
                    else
                    {
                        abilitySlots.Add(slot, ability);
                    }
                }
                
                return ability as T;
            }
        }
        
        // Añadir la nueva habilidad e inicializarla correctamente
        T newAbility = gameObject.AddComponent<T>();
        
        // Inicializar con el NetworkBehaviour correcto
        newAbility.Initialize(this);
        
        // Agregar a la lista activa
        abilities.Add(newAbility);
        
        // Asignar a slot específico si se solicita
        if (slot >= 0)
        {
            if (abilitySlots.ContainsKey(slot))
            {
                abilitySlots[slot] = newAbility;
            }
            else
            {
                abilitySlots.Add(slot, newAbility);
            }
        }
        
        // Sincronizar con el sistema antiguo si existe
        if (legacyAbilityComponent != null)
        {
            legacyAbilityComponent.RegisterPowerUpAbility(newAbility, slot);
        }
        
        if (IsOwner && showDebugMessages)
        {
            Debug.Log($"Nueva habilidad adquirida: {newAbility.abilityName}, asignada a slot {slot}");
        }
        
        return newAbility;
    }
    
    // Eliminar una habilidad por tipo
    public bool RemoveAbility<T>() where T : BaseAbility
    {
        for (int i = 0; i < abilities.Count; i++)
        {
            if (abilities[i] is T)
            {
                BaseAbility ability = abilities[i];
                abilities.RemoveAt(i);
                
                // Eliminar de slots
                foreach (var slotPair in abilitySlots)
                {
                    if (slotPair.Value == ability)
                    {
                        abilitySlots.Remove(slotPair.Key);
                    }
                }
                
                // Limpiar recursos
                ability.Cleanup();
                Destroy(ability);
                
                // Sincronizar con sistema antiguo
                if (legacyAbilityComponent != null)
                {
                    // No tenemos un método directo para determinar el slot, pero podemos intentar
                    // por los slots comunes
                    for (int slot = 0; slot < 4; slot++)
                    {
                        if (legacyAbilityComponent.GetAbility(slot)?.name == ability.abilityName)
                        {
                            legacyAbilityComponent.UnregisterPowerUpAbility(slot);
                            break;
                        }
                    }
                }
                
                if (IsOwner && showDebugMessages)
                {
                    Debug.Log($"Habilidad removida: {ability.abilityName}");
                }
                
                return true;
            }
        }
        
        return false;
    }
    
    // Eliminar todas las habilidades
    public void RemoveAllAbilities()
    {
        // Usar HashSet para evitar procesar habilidades duplicadas
        HashSet<BaseAbility> processedAbilities = new HashSet<BaseAbility>();
        
        // Primero, procesar todas las habilidades en nuestra lista
        foreach (var ability in abilities)
        {
            if (ability != null && !processedAbilities.Contains(ability))
            {
                processedAbilities.Add(ability);
                ability.Cleanup();
                Destroy(ability);
            }
        }
        
        // Luego buscar habilidades que pudieran haber sido añadidas directamente como componentes
        foreach (var ability in GetComponents<BaseAbility>())
        {
            if (ability != null && !processedAbilities.Contains(ability))
            {
                processedAbilities.Add(ability);
                ability.Cleanup();
                Destroy(ability);
            }
        }
        
        // Limpiar la lista de habilidades activas
        abilities.Clear();
        
        // Limpiar diccionario de slots
        abilitySlots.Clear();
        
        if (showDebugMessages)
        {
            Debug.Log("[PlayerAbilityManager] Todas las habilidades han sido eliminadas");
        }
    }
    
    // Inicializar habilidades desde definiciones
    public void InitializeAbilities(List<AbilityDefinition> abilityDefinitions)
    {
        // Limpiar habilidades existentes
        RemoveAllAbilities();
        
        // Crear e inicializar habilidades
        for (int i = 0; i < abilityDefinitions.Count; i++)
        {
            var def = abilityDefinitions[i];
            
            // Crear habilidad a partir del tipo
            Type abilityType = Type.GetType(def.abilityType);
            if (abilityType != null && typeof(BaseAbility).IsAssignableFrom(abilityType))
            {
                BaseAbility ability = gameObject.AddComponent(abilityType) as BaseAbility;
                
                // Configurar habilidad
                ability.abilityName = def.abilityName;
                ability.activationKey = def.activationKey;
                ability.manaCost = def.manaCost;
                ability.cooldown = def.cooldown;
                ability.icon = def.icon;
                
                // Inicializar con este NetworkBehaviour
                ability.Initialize(this);
                
                // Añadir a las habilidades activas
                abilities.Add(ability);
                
                // Asignar al slot correspondiente
                abilitySlots[i] = ability;
                
                if (showDebugMessages)
                {
                    Debug.Log($"Añadida habilidad: {ability.abilityName} en slot {i}");
                }
            }
            else
            {
                Debug.LogError($"Error al crear habilidad de tipo: {def?.abilityType ?? "NULL"}");
            }
        }
        
        // Sincronizar con sistema antiguo
        SyncLegacySystem();
    }
    
    // Sincronizar con el sistema antiguo de PlayerAbility (para compatibilidad)
    private void SyncLegacySystem()
    {
        if (legacyAbilityComponent == null) return;
        
        // Intentar mapear habilidades a slots del sistema antiguo
        foreach (var slotPair in abilitySlots)
        {
            int slot = slotPair.Key;
            BaseAbility ability = slotPair.Value;
            
            // Solo sincronizamos slots 0-3 que es lo que soporta el sistema antiguo
            if (slot >= 0 && slot < 4 && ability != null)
            {
                legacyAbilityComponent.RegisterPowerUpAbility(ability, slot);
            }
        }
    }
    
    // Método para establecer el estado de pausa de impacto
    public void SetInImpactPause(bool pause)
    {
        isInImpactPause = pause;
    }
    
    // Métodos de ayuda para UI y otros sistemas
    public int GetAbilityCount() => abilities.Count;
    
    public BaseAbility GetAbility(int index)
    {
        if (index >= 0 && index < abilities.Count)
            return abilities[index];
        return null;
    }
    
    // Obtener habilidad por slot (para UI)
    public BaseAbility GetAbilityBySlot(int slot)
    {
        if (abilitySlots.TryGetValue(slot, out BaseAbility ability))
        {
            return ability;
        }
        
        // Fallback: intentar mapear índice en la lista
        if (slot >= 0 && slot < abilities.Count)
        {
            return abilities[slot];
        }
        
        return null;
    }
    
    // Para consultas de habilidades específicas
    public T GetAbilityOfType<T>() where T : BaseAbility
    {
        foreach (var ability in abilities)
        {
            if (ability is T typedAbility)
                return typedAbility;
        }
        return null;
    }
    
    // Verificar si el jugador tiene una habilidad específica
    public bool HasAbility<T>() where T : BaseAbility
    {
        foreach (var ability in abilities)
        {
            if (ability is T)
                return true;
        }
        return false;
    }
    
    // Métodos para compatibilidad con PlayerNetwork
    public bool IsMoving()
    {
        if (playerNetwork != null)
        {
            return playerNetwork.IsMoving();
        }
        return false;
    }
    
    public Vector3 GetTargetPosition()
    {
        if (playerNetwork != null)
        {
            return playerNetwork.GetTargetPosition();
        }
        return transform.position;
    }
    
    // Sincronización periódica (desde el servidor)
    private IEnumerator PeriodicSyncCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(syncInterval);
            
            if (IsServer)
            {
                SyncAbilityStates();
            }
        }
    }
    
    // Solicitar sincronización periódica (desde el cliente)
    private IEnumerator RequestSyncCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(syncInterval * 1.5f); // Ligeramente más largo que el intervalo del servidor
            
            if (IsOwner && !IsServer)
            {
                RequestSyncServerRpc();
            }
        }
    }
    
    // Método para sincronizar el estado de todas las habilidades
    public void SyncAbilityStates()
    {
        if (!IsServer) return;
        
        // Preparar listas para los datos de sincronización
        List<int> indices = new List<int>();
        List<bool> readyStates = new List<bool>();
        List<float> cooldowns = new List<float>();
        
        for (int i = 0; i < abilities.Count; i++)
        {
            BaseAbility ability = abilities[i];
            if (ability != null)
            {
                indices.Add(i);
                readyStates.Add(ability.isReady);
                cooldowns.Add(ability.GetRemainingCooldown());
            }
        }
        
        // Enviar datos a todos los clientes
        if (indices.Count > 0)
        {
            SyncAbilityStatesClientRpc(indices.ToArray(), readyStates.ToArray(), cooldowns.ToArray());
        }
    }
    
    [ClientRpc]
    private void SyncAbilityStatesClientRpc(int[] indices, bool[] readyStates, float[] cooldowns)
    {
        if (IsServer) return; // El servidor ya tiene la información
        
        for (int i = 0; i < indices.Length; i++)
        {
            int index = indices[i];
            bool isReady = readyStates[i];
            float remainingCooldown = cooldowns[i];
            
            if (index >= 0 && index < abilities.Count)
            {
                BaseAbility ability = abilities[index];
                if (ability != null)
                {
                    ability.SyncCooldown(remainingCooldown);
                }
            }
        }
        
        // Sincronizar con sistema antiguo
        SyncLegacySystem();
    }
    
    [ServerRpc]
    private void RequestSyncServerRpc()
    {
        // El cliente solicita sincronización, simplemente llamamos a SyncAbilityStates
        SyncAbilityStates();
    }
    
    // Resetear todas las habilidades (ej: después de morir)
    public void ResetAllAbilities()
    {
        foreach (var ability in abilities)
        {
            if (ability != null)
            {
                ability.Reset();
            }
        }
        
        if (IsServer)
        {
            // Notificar a los clientes
            ResetAllAbilitiesClientRpc();
        }
    }
    
    [ClientRpc]
    private void ResetAllAbilitiesClientRpc()
    {
        if (IsServer) return; // El servidor ya lo hizo
        
        foreach (var ability in abilities)
        {
            if (ability != null)
            {
                ability.Reset();
            }
        }
        
        // Sincronizar con sistema antiguo
        SyncLegacySystem();
    }
}