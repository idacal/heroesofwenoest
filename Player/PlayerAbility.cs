using Unity.Netcode;
using UnityEngine;
using System.Collections;
using PlayerAbilities;

// Esta clase funciona como adaptador para mantener compatibilidad con sistemas existentes
public class PlayerAbility : NetworkBehaviour
{
    [System.Serializable]
    public class Ability
    {
        public string name = "Ability";
        public KeyCode activationKey = KeyCode.Q;
        public float manaCost = 30f;
        public float cooldown = 2f;
        public bool isReady = true;
        public Sprite icon; // Icono para la UI
        
        // Variables para seguimiento de cooldown
        [HideInInspector] public float cooldownEndTime = 0f;
    }

    [Header("Habilidades")]
    [SerializeField] private Ability[] abilities = new Ability[4];
    
    [Header("Referencias")]
    [SerializeField] private GameObject clickIndicatorPrefab;

    // Referencias a componentes
    private PlayerStats playerStats;
    private PlayerAbilityController abilityController;
    
    // Referencias directas a las habilidades para consultar estados actuales
    private DashAbility dashAbility;
    private StrongJumpAbility strongJumpAbility; // Reemplazo de EarthquakeAbility
    private ShieldAbility shieldAbility;
    // Añadimos referencias para nuestras nuevas habilidades
    private SupersonicMissileAbility missileAbility;
    private KineticShieldAbility kineticShieldAbility;
    
    // Tabla para mapear habilidades del powerup a slots del array abilities
    private BaseAbility[] powerUpAbilities = new BaseAbility[2]; // Posiciones 2 y 3 del array abilities

    private void Awake()
    {
        // Inicializamos las habilidades aquí para asegurar que siempre se crean correctamente
        InitializeAbilities();
    }

    private void InitializeAbilities()
    {
        // Solo inicializar si el array está vacío o null
        if (abilities == null || abilities.Length != 4)
        {
            abilities = new Ability[4];
        }

        // Verificar cada elemento individualmente y solo inicializar si es null
        if (abilities[0] == null)
            abilities[0] = new Ability { name = "Dash", activationKey = KeyCode.Q, manaCost = 30f, cooldown = 3f };
        
        if (abilities[1] == null)
            abilities[1] = new Ability { name = "Salto Fuerte", activationKey = KeyCode.W, manaCost = 20f, cooldown = 8f };
        
        if (abilities[2] == null)
            abilities[2] = new Ability { name = "Escudo Cinético", activationKey = KeyCode.E, manaCost = 70f, cooldown = 8f };
        
        if (abilities[3] == null)
            abilities[3] = new Ability { name = "Misil Supersónico", activationKey = KeyCode.R, manaCost = 120f, cooldown = 60f };
    }

    private void OnValidate()
    {
        // Asegurarse de que las habilidades estén inicializadas en el editor
        InitializeAbilities();
    }

    private void Start()
    {
        playerStats = GetComponent<PlayerStats>();
        
        // Crear e inicializar el controlador de habilidades
        abilityController = GetComponent<PlayerAbilityController>();
        if (abilityController == null)
        {
            abilityController = gameObject.AddComponent<PlayerAbilityController>();
        }
        
        // Pasar referencias al controlador
        if (clickIndicatorPrefab != null)
        {
            abilityController.clickIndicatorPrefab = clickIndicatorPrefab;
        }
        
        // Esperar a que el sistema de red esté listo
        StartCoroutine(SyncWithNewSystem());
    }
    
    private IEnumerator SyncWithNewSystem()
    {
        // Esperar un pequeño tiempo para que todas las inicializaciones de red ocurran
        yield return new WaitForSeconds(0.2f);
        
        // Obtener referencias a las habilidades específicas
        dashAbility = GetComponent<DashAbility>();
        strongJumpAbility = GetComponent<StrongJumpAbility>();
        
        // Buscar si ya existen las habilidades nuevas
        kineticShieldAbility = GetComponent<KineticShieldAbility>();
        if (kineticShieldAbility != null)
        {
            // Si ya existe, registrarlo
            RegisterPowerUpAbility(kineticShieldAbility, 2);
        }
        
        missileAbility = GetComponent<SupersonicMissileAbility>();
        if (missileAbility != null)
        {
            // Si ya existe, registrarlo
            RegisterPowerUpAbility(missileAbility, 3);
        }
        
        // Buscar si ya existe el ShieldAbility (puede haber sido añadido antes)
        shieldAbility = GetComponent<ShieldAbility>();
        if (shieldAbility != null)
        {
            // Si ya existe, registrarlo
            RegisterPowerUpAbility(shieldAbility, 2);
        }
        
        // Sincronizar información de habilidades con el nuevo sistema
        SyncAbilityInfo();
        
        // Seguir actualizando los estados
        StartCoroutine(UpdateAbilityStates());
    }
    
    private IEnumerator UpdateAbilityStates()
    {
        while (true)
        {
            // Actualizar estados de isReady de las habilidades del sistema antiguo
            // para que la UI pueda leerlos correctamente
            if (dashAbility != null)
            {
                abilities[0].isReady = dashAbility.isReady;
                abilities[0].cooldownEndTime = Time.time + dashAbility.GetRemainingCooldown();
            }
            
            if (strongJumpAbility != null)
            {
                abilities[1].isReady = strongJumpAbility.isReady;
                abilities[1].cooldownEndTime = Time.time + strongJumpAbility.GetRemainingCooldown();
            }
            
            // Actualizar las habilidades nuevas específicamente
            if (kineticShieldAbility != null)
            {
                abilities[2].isReady = kineticShieldAbility.isReady;
                abilities[2].cooldownEndTime = Time.time + kineticShieldAbility.GetRemainingCooldown();
            }
            
            if (missileAbility != null)
            {
                abilities[3].isReady = missileAbility.isReady;
                abilities[3].cooldownEndTime = Time.time + missileAbility.GetRemainingCooldown();
            }
            
            // Actualizar las habilidades de powerup registradas
            UpdatePowerUpAbilitiesState();
            
            // Actualizar cada 0.1 segundos es suficiente para la UI
            yield return new WaitForSeconds(0.1f);
        }
    }
    
    // NUEVO MÉTODO: Actualiza el estado de las habilidades adquiridas por powerup
    private void UpdatePowerUpAbilitiesState()
    {
        for (int i = 0; i < powerUpAbilities.Length; i++)
        {
            if (powerUpAbilities[i] != null)
            {
                int abilitySlot = i + 2; // +2 porque 0 y 1 son dash y strongjump
                
                // Asegurar que el slot existe en el array de abilities
                if (abilitySlot < abilities.Length && abilities[abilitySlot] != null)
                {
                    abilities[abilitySlot].isReady = powerUpAbilities[i].isReady;
                    abilities[abilitySlot].cooldownEndTime = Time.time + powerUpAbilities[i].GetRemainingCooldown();
                    
                    // Debugging
                    if (IsOwner && powerUpAbilities[i].GetRemainingCooldown() > 0)
                    {
                        Debug.Log($"[PlayerAbility] Ability {powerUpAbilities[i].abilityName} cooldown: {powerUpAbilities[i].GetRemainingCooldown()} seconds");
                    }
                }
            }
        }
    }
    
    private void SyncAbilityInfo()
    {
        // Actualizar información en ambas direcciones
        if (dashAbility != null && abilities[0] != null)
        {
            dashAbility.abilityName = abilities[0].name;
            dashAbility.activationKey = abilities[0].activationKey;
            dashAbility.manaCost = abilities[0].manaCost;
            dashAbility.cooldown = abilities[0].cooldown;
            dashAbility.icon = abilities[0].icon;
        }
        
        if (strongJumpAbility != null && abilities[1] != null)
        {
            strongJumpAbility.abilityName = abilities[1].name;
            strongJumpAbility.activationKey = abilities[1].activationKey;
            strongJumpAbility.manaCost = abilities[1].manaCost;
            strongJumpAbility.cooldown = abilities[1].cooldown;
            strongJumpAbility.icon = abilities[1].icon;
        }
        
        // Sincronizar las nuevas habilidades
        if (kineticShieldAbility != null && abilities[2] != null)
        {
            kineticShieldAbility.abilityName = abilities[2].name;
            kineticShieldAbility.activationKey = abilities[2].activationKey;
            kineticShieldAbility.manaCost = abilities[2].manaCost;
            kineticShieldAbility.cooldown = abilities[2].cooldown;
            kineticShieldAbility.icon = abilities[2].icon;
        }
        
        if (missileAbility != null && abilities[3] != null)
        {
            missileAbility.abilityName = abilities[3].name;
            missileAbility.activationKey = abilities[3].activationKey;
            missileAbility.manaCost = abilities[3].manaCost;
            missileAbility.cooldown = abilities[3].cooldown;
            missileAbility.icon = abilities[3].icon;
        }
        
        // Sincronizar las habilidades de powerup si existen
        SyncPowerUpAbilities();
    }
    
    // NUEVO MÉTODO: Sincroniza las habilidades adquiridas por powerup
    private void SyncPowerUpAbilities()
    {
        for (int i = 0; i < powerUpAbilities.Length; i++)
        {
            if (powerUpAbilities[i] != null)
            {
                int abilitySlot = i + 2; // +2 porque posiciones 0 y 1 son dash y strongjump
                
                if (abilitySlot < abilities.Length && abilities[abilitySlot] != null)
                {
                    // Transferir configuración entre sistemas
                    powerUpAbilities[i].abilityName = abilities[abilitySlot].name;
                    powerUpAbilities[i].activationKey = abilities[abilitySlot].activationKey;
                    powerUpAbilities[i].manaCost = abilities[abilitySlot].manaCost;
                    powerUpAbilities[i].cooldown = abilities[abilitySlot].cooldown;
                    powerUpAbilities[i].icon = abilities[abilitySlot].icon;
                    
                    Debug.Log($"[PlayerAbility] Sincronizada info de {powerUpAbilities[i].abilityName} con cooldown {powerUpAbilities[i].cooldown}s");
                }
            }
        }
    }
    
    // NUEVO MÉTODO: Registra una habilidad adquirida por powerup en el sistema
    public void RegisterPowerUpAbility(BaseAbility ability, int slot)
    {
        if (slot < 2 || slot >= 4)
        {
            Debug.LogError($"[PlayerAbility] Slot inválido para habilidad de powerup: {slot}. Debe ser 2 o 3.");
            return;
        }
        
        int powerUpIndex = slot - 2;
        powerUpAbilities[powerUpIndex] = ability;
        
        // Si es KineticShield específicamente, guardar referencia directa
        if (ability is KineticShieldAbility)
        {
            kineticShieldAbility = ability as KineticShieldAbility;
            Debug.Log("[PlayerAbility] KineticShieldAbility guardado en referencia directa");
        }
        
        // Si es SupersonicMissile específicamente, guardar referencia directa
        if (ability is SupersonicMissileAbility)
        {
            missileAbility = ability as SupersonicMissileAbility;
            Debug.Log("[PlayerAbility] SupersonicMissileAbility guardado en referencia directa");
        }
        
        // Si es Shield específicamente, guardar referencia directa también
        if (ability is ShieldAbility)
        {
            shieldAbility = ability as ShieldAbility;
            Debug.Log("[PlayerAbility] ShieldAbility guardado en referencia directa");
        }
        
        // Hacer una sincronización inmediata
        SyncPowerUpAbilities();
        
        Debug.Log($"[PlayerAbility] Habilidad {ability.abilityName} registrada en slot {slot}");
    }
    
    // NUEVO MÉTODO: Desregistra una habilidad de powerup
    public void UnregisterPowerUpAbility(int slot)
    {
        if (slot < 2 || slot >= 4)
        {
            Debug.LogError($"[PlayerAbility] Slot inválido para eliminar: {slot}. Debe ser 2 o 3.");
            return;
        }
        
        int powerUpIndex = slot - 2;
        
        // Limpiar referencias directas según el tipo
        if (powerUpAbilities[powerUpIndex] is KineticShieldAbility)
        {
            kineticShieldAbility = null;
        }
        
        if (powerUpAbilities[powerUpIndex] is SupersonicMissileAbility)
        {
            missileAbility = null;
        }
        
        if (powerUpAbilities[powerUpIndex] is ShieldAbility)
        {
            shieldAbility = null;
        }
        
        powerUpAbilities[powerUpIndex] = null;
        
        Debug.Log($"[PlayerAbility] Habilidad en slot {slot} desregistrada");
    }
    
    // Método público para verificar si el jugador está en pausa de impacto
    public bool IsInImpactPause()
    {
        if (strongJumpAbility != null)
        {
            return strongJumpAbility.IsImmobilized;
        }
        return false;
    }
    
    // Métodos públicos para acceso desde la UI
    public int GetAbilityCount()
    {
        return abilities.Length;
    }
    
    public Ability GetAbility(int index)
    {
        if (index >= 0 && index < abilities.Length)
            return abilities[index];
        return null;
    }
    
    public float GetRemainingCooldown(int index)
    {
        if (index >= 0 && index < abilities.Length)
        {
            // Obtener cooldown desde el nuevo sistema de habilidades
            switch (index)
            {
                case 0: // Dash
                    if (dashAbility != null)
                        return dashAbility.GetRemainingCooldown();
                    break;
                    
                case 1: // StrongJump
                    if (strongJumpAbility != null)
                        return strongJumpAbility.GetRemainingCooldown();
                    break;
                    
                case 2: // Escudo Cinético u otra habilidad de powerup
                    if (kineticShieldAbility != null)
                        return kineticShieldAbility.GetRemainingCooldown();
                    else if (shieldAbility != null)
                        return shieldAbility.GetRemainingCooldown();
                    else if (powerUpAbilities[0] != null)
                        return powerUpAbilities[0].GetRemainingCooldown();
                    break;
                    
                case 3: // Misil Supersónico u otra habilidad de powerup
                    if (missileAbility != null)
                        return missileAbility.GetRemainingCooldown();
                    else if (powerUpAbilities[1] != null)
                        return powerUpAbilities[1].GetRemainingCooldown();
                    break;
            }
            
            // Fallback al sistema antiguo
            Ability ability = abilities[index];
            if (!ability.isReady)
            {
                return Mathf.Max(0, ability.cooldownEndTime - Time.time);
            }
        }
        return 0f;
    }
}