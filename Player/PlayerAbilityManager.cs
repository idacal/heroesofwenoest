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
    
    // Lista de habilidades activas
    private List<BaseAbility> abilities = new List<BaseAbility>();
    
    // Referencias
    private PlayerStats playerStats;
    private PlayerNetwork playerNetwork;
    
    // Variables para seguimiento de estado
    public bool isInImpactPause { get; private set; } = false;
    
    private void Awake()
    {
        // Obtener referencias
        playerStats = GetComponent<PlayerStats>();
        playerNetwork = GetComponent<PlayerNetwork>();
        
        if (playerStats == null)
        {
            Debug.LogError("[PlayerAbilityManager] No se encontró componente PlayerStats");
        }
        
        if (playerNetwork == null)
        {
            Debug.LogError("[PlayerAbilityManager] No se encontró componente PlayerNetwork");
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Activar procesamiento de entrada solo para el propietario
        if (IsOwner)
        {
            enabled = true;
        }
        else
        {
            enabled = false;
        }
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
                Debug.Log($"Habilidad {ability.abilityName} en cooldown: {ability.GetRemainingCooldown():F1}s");
            }
            else if (playerStats.CurrentMana < ability.manaCost)
            {
                Debug.Log($"No hay suficiente maná para {ability.abilityName}. Necesitas {ability.manaCost}, tienes {playerStats.CurrentMana:F1}");
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
        
        // Activar la habilidad
        ability.Activate();
        
        // Iniciar cooldown
        StartCoroutine(ability.StartCooldown());
    }
    
    // Añadir una nueva habilidad
    public T AddAbility<T>() where T : BaseAbility
    {
        // Verificar si la habilidad ya existe
        foreach (var ability in abilities)
        {
            if (ability is T)
            {
                Debug.LogWarning($"La habilidad {typeof(T).Name} ya está añadida al jugador");
                return ability as T;
            }
        }
        
        // Añadir la nueva habilidad e inicializarla correctamente
        T newAbility = gameObject.AddComponent<T>();
        
        // Inicializar con el NetworkBehaviour correcto
        newAbility.Initialize(this);
        
        // Agregar a la lista activa
        abilities.Add(newAbility);
        
        if (IsOwner)
        {
            Debug.Log($"Nueva habilidad adquirida: {newAbility.abilityName}");
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
        
        Debug.Log("[PlayerAbilityManager] Todas las habilidades han sido eliminadas");
    }
    
    // Inicializar habilidades desde definiciones
    public void InitializeAbilities(List<AbilityDefinition> abilityDefinitions)
    {
        // Limpiar habilidades existentes
        RemoveAllAbilities();
        
        // Crear e inicializar habilidades
        foreach (var def in abilityDefinitions)
        {
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
                
                Debug.Log($"Añadida habilidad: {ability.abilityName}");
            }
            else
            {
                Debug.LogError($"Error al crear habilidad de tipo: {def.abilityType}");
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
}