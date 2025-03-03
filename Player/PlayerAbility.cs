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
    private EarthquakeAbility earthquakeAbility;
    private ShieldAbility shieldAbility;

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
            abilities[1] = new Ability { name = "Terremoto", activationKey = KeyCode.W, manaCost = 60f, cooldown = 10f };
        
        if (abilities[2] == null)
            abilities[2] = new Ability { name = "Escudo", activationKey = KeyCode.E, manaCost = 70f, cooldown = 8f };
        
        if (abilities[3] == null)
            abilities[3] = new Ability { name = "Ultimate", activationKey = KeyCode.R, manaCost = 100f, cooldown = 12f };
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
        earthquakeAbility = GetComponent<EarthquakeAbility>();
        shieldAbility = GetComponent<ShieldAbility>();
        
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
            
            if (earthquakeAbility != null)
            {
                abilities[1].isReady = earthquakeAbility.isReady;
                abilities[1].cooldownEndTime = Time.time + earthquakeAbility.GetRemainingCooldown();
            }
            
            if (shieldAbility != null)
            {
                abilities[2].isReady = shieldAbility.isReady;
                abilities[2].cooldownEndTime = Time.time + shieldAbility.GetRemainingCooldown();
            }
            
            // Actualizar cada 0.1 segundos es suficiente para la UI
            yield return new WaitForSeconds(0.1f);
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
        
        if (earthquakeAbility != null && abilities[1] != null)
        {
            earthquakeAbility.abilityName = abilities[1].name;
            earthquakeAbility.activationKey = abilities[1].activationKey;
            earthquakeAbility.manaCost = abilities[1].manaCost;
            earthquakeAbility.cooldown = abilities[1].cooldown;
            earthquakeAbility.icon = abilities[1].icon;
        }
        
        if (shieldAbility != null && abilities[2] != null)
        {
            shieldAbility.abilityName = abilities[2].name;
            shieldAbility.activationKey = abilities[2].activationKey;
            shieldAbility.manaCost = abilities[2].manaCost;
            shieldAbility.cooldown = abilities[2].cooldown;
            shieldAbility.icon = abilities[2].icon;
        }
    }
    
    // Método público para verificar si el jugador está en pausa de impacto
    public bool IsInImpactPause()
    {
        if (earthquakeAbility != null)
        {
            return earthquakeAbility.IsInImpactPause;
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
                    
                case 1: // Terremoto
                    if (earthquakeAbility != null)
                        return earthquakeAbility.GetRemainingCooldown();
                    break;
                    
                case 2: // Escudo
                    if (shieldAbility != null)
                        return shieldAbility.GetRemainingCooldown();
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