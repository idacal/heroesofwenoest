using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PlayerAbilities;

public class AbilityUIManager : MonoBehaviour
{
    [System.Serializable]
    public class AbilityUI
    {
        public Image abilityImage;
        public Image cooldownOverlay; // Opcional, puedes dejarlo sin asignar
        public TextMeshProUGUI keyText;
        public TextMeshProUGUI cooldownText;
        public Image requirementIcon; // Ícono para mostrar requisitos (como movimiento)
    }

    [Header("Referencias")]
    [SerializeField] private PlayerAbilityManager playerAbilityManager;
    [SerializeField] private PlayerAbility playerAbility; // Para compatibilidad

    [Header("UI de Habilidades")]
    [SerializeField] private AbilityUI[] abilityUIs = new AbilityUI[4]; // Q, W, E, R

    [Header("Configuración")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color cooldownColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
    [SerializeField] private Color noManaColor = new Color(0.3f, 0.3f, 0.8f, 0.7f);
    [SerializeField] private Color requirementNotMetColor = new Color(0.8f, 0.4f, 0.0f, 0.7f);
    
    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    // Referencia al PlayerStats para verificar mana
    private PlayerStats playerStats;
    
    // Referencias a habilidades específicas para requisitos adicionales
    private EarthquakeAbility earthquakeAbility; // Obsoleto, mantener para compatibilidad
    private StrongJumpAbility strongJumpAbility; // Nueva habilidad que reemplaza Earthquake

    private void Start()
    {
        // Inicializar teclas
        string[] keys = { "Q", "W", "E", "R" };
        for (int i = 0; i < abilityUIs.Length; i++)
        {
            if (abilityUIs[i].keyText != null)
            {
                abilityUIs[i].keyText.text = keys[i];
            }
            
            // Asegurarse de que los textos de cooldown estén desactivados inicialmente
            if (abilityUIs[i].cooldownText != null)
            {
                abilityUIs[i].cooldownText.gameObject.SetActive(false);
            }
            
            // Ocultar íconos de requisitos inicialmente
            if (abilityUIs[i].requirementIcon != null)
            {
                abilityUIs[i].requirementIcon.gameObject.SetActive(false);
            }
        }
        
        // Encontrar referencias a los componentes del jugador
        FindPlayerComponents();
    }

    private void FindPlayerComponents()
    {
        // Primero buscar el PlayerAbilityManager (sistema nuevo)
        if (playerAbilityManager == null)
        {
            PlayerNetwork[] players = FindObjectsOfType<PlayerNetwork>();
            foreach (PlayerNetwork player in players)
            {
                if (player.IsLocalPlayer)
                {
                    playerAbilityManager = player.GetComponent<PlayerAbilityManager>();
                    
                    // Para compatibilidad, también buscar PlayerAbility
                    if (playerAbility == null)
                    {
                        playerAbility = player.GetComponent<PlayerAbility>();
                    }
                    
                    playerStats = player.GetComponent<PlayerStats>();
                    
                    // Buscar referencia a EarthquakeAbility (compatibilidad)
                    earthquakeAbility = player.GetComponent<EarthquakeAbility>();
                    
                    // Buscar referencia a StrongJumpAbility (nuevo sistema)
                    strongJumpAbility = player.GetComponent<StrongJumpAbility>();
                    
                    if ((playerAbilityManager != null || playerAbility != null) && playerStats != null)
                    {
                        Debug.Log("[AbilityUIManager] Se encontraron componentes del jugador local");
                        break;
                    }
                }
            }
            
            if (playerAbilityManager == null && playerAbility == null)
            {
                Debug.LogWarning("[AbilityUIManager] No se pudieron encontrar los componentes del jugador, reintentando en 1 segundo...");
                Invoke("FindPlayerComponents", 1f);
            }
        }
    }

    private void Update()
    {
        // Actualizar UI basado en el sistema disponible
        if (playerAbilityManager != null)
        {
            // Usar el nuevo sistema
            UpdateUIFromAbilityManager();
        }
        else if (playerAbility != null)
        {
            // Usar el sistema antiguo
            UpdateUIFromPlayerAbility();
        }
        else
        {
            // Intentar encontrar componentes de nuevo
            FindPlayerComponents();
        }
    }
    
    // Método para actualizar UI desde PlayerAbilityManager (nuevo sistema)
    private void UpdateUIFromAbilityManager()
    {
        // Actualizar UI de cada slot
        for (int i = 0; i < abilityUIs.Length; i++)
        {
            BaseAbility ability = playerAbilityManager.GetAbilityBySlot(i);
            if (ability != null)
            {
                UpdateAbilityUIFromBaseAbility(i, ability);
            }
        }
    }
    
    // Método para actualizar UI desde PlayerAbility (sistema antiguo)
    private void UpdateUIFromPlayerAbility()
    {
        if (playerStats == null || playerAbility == null) return;
        
        // Actualizar UI de cada habilidad
        int count = Mathf.Min(abilityUIs.Length, playerAbility.GetAbilityCount());
        for (int i = 0; i < count; i++)
        {
            UpdateAbilityUI(i);
        }
    }
    
    // Nuevo método que actualiza UI basado directamente en una instancia de BaseAbility
    private void UpdateAbilityUIFromBaseAbility(int index, BaseAbility ability)
    {
        if (index < 0 || index >= abilityUIs.Length) return;
        
        AbilityUI ui = abilityUIs[index];
        if (ui == null) return;
        
        // Verificar si la habilidad está en cooldown
        bool isInCooldown = !ability.isReady;
        float cooldownRemaining = ability.GetRemainingCooldown();
        bool hasMana = playerStats.CurrentMana >= ability.manaCost;
        
        // Verificar requisitos adicionales específicos para cada habilidad
        bool requirementsMet = CheckRequirements(ability);
        string requirementMessage = GetRequirementMessage(ability);
        
        // Debug logging
        if (showDebugLogs && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[AbilityUIManager] Habilidad {index} ({ability.abilityName}) - " +
                     $"En cooldown: {isInCooldown}, " +
                     $"Tiempo restante: {cooldownRemaining:F1}s, " +
                     $"Tiene maná: {hasMana}, " +
                     $"Requisitos cumplidos: {requirementsMet}");
        }
        
        // Actualizar texto de cooldown
        if (ui.cooldownText != null)
        {
            if (isInCooldown)
            {
                // Activar y actualizar el texto
                ui.cooldownText.gameObject.SetActive(true);
                ui.cooldownText.text = Mathf.CeilToInt(cooldownRemaining).ToString();
            }
            else
            {
                // Habilidad lista, ocultar texto de cooldown
                ui.cooldownText.gameObject.SetActive(false);
            }
        }
        
        // Mostrar ícono de requisito si no se cumplen los requisitos
        if (ui.requirementIcon != null)
        {
            if (!requirementsMet)
            {
                ui.requirementIcon.gameObject.SetActive(true);
                
                // Si hay un texto en el ícono, actualizarlo
                TextMeshProUGUI iconText = ui.requirementIcon.GetComponentInChildren<TextMeshProUGUI>();
                if (iconText != null)
                {
                    iconText.text = requirementMessage;
                }
            }
            else
            {
                ui.requirementIcon.gameObject.SetActive(false);
            }
        }
        
        // Actualizar overlay de cooldown (opcional)
        if (ui.cooldownOverlay != null)
        {
            if (isInCooldown)
            {
                // Mostrar el fill del cooldown (de 1 a 0 durante el cooldown)
                float fillAmount = cooldownRemaining / ability.cooldown;
                ui.cooldownOverlay.fillAmount = fillAmount;
                ui.cooldownOverlay.color = cooldownColor;
            }
            else if (!hasMana)
            {
                // Sin mana suficiente
                ui.cooldownOverlay.fillAmount = 1f;
                ui.cooldownOverlay.color = noManaColor;
            }
            else if (!requirementsMet)
            {
                // Requisitos no cumplidos
                ui.cooldownOverlay.fillAmount = 1f;
                ui.cooldownOverlay.color = requirementNotMetColor;
            }
            else
            {
                // Habilidad lista
                ui.cooldownOverlay.fillAmount = 0f;
            }
        }
        
        // Actualizar imagen principal
        if (ui.abilityImage != null)
        {
            // Asignar icono si no está configurado
            if (ui.abilityImage.sprite == null && ability.icon != null)
            {
                ui.abilityImage.sprite = ability.icon;
            }
            
            if (isInCooldown || !hasMana || !requirementsMet)
            {
                ui.abilityImage.color = new Color(1f, 1f, 1f, 0.5f); // Semi-transparente
            }
            else
            {
                ui.abilityImage.color = normalColor;
            }
        }
    }

    // Método original para soporte de sistema antiguo
    private void UpdateAbilityUI(int index)
    {
        if (index < 0 || index >= abilityUIs.Length) return;
        
        AbilityUI ui = abilityUIs[index];
        PlayerAbility.Ability ability = playerAbility.GetAbility(index);
        
        if (ability == null) return;
        
        // Verificar si la habilidad está en cooldown
        bool isInCooldown = !ability.isReady;
        float cooldownRemaining = playerAbility.GetRemainingCooldown(index);
        bool hasMana = playerStats.CurrentMana >= ability.manaCost;
        
        // Verificar requisitos adicionales (solo para el sistema antiguo)
        bool requirementsMet = true;
        string requirementMessage = "";
        
        // Si es EarthquakeAbility (índice 1 = W)
        if (index == 1 && earthquakeAbility != null)
        {
            requirementsMet = earthquakeAbility.IsMovingFastEnough();
            
            if (!requirementsMet)
            {
                requirementMessage = "¡Muévete!";
            }
        }
        // Si es StrongJumpAbility (new W ability)
        else if (index == 1 && strongJumpAbility != null)
        {
            requirementsMet = strongJumpAbility.IsMovingFastEnough();
            
            if (!requirementsMet)
            {
                requirementMessage = "¡Muévete!";
            }
        }
        
        // Debug logging
        if (showDebugLogs && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[AbilityUIManager] Habilidad {index} ({ability.name}) - " +
                     $"En cooldown: {isInCooldown}, " +
                     $"Tiempo restante: {cooldownRemaining:F1}s, " +
                     $"Tiene maná: {hasMana}, " +
                     $"Requisitos cumplidos: {requirementsMet}");
        }
        
        // Actualizar texto de cooldown
        if (ui.cooldownText != null)
        {
            if (isInCooldown)
            {
                // Activar y actualizar el texto
                ui.cooldownText.gameObject.SetActive(true);
                ui.cooldownText.text = Mathf.CeilToInt(cooldownRemaining).ToString();
                
                if (showDebugLogs && index == 0 && Time.frameCount % 60 == 0)
                {
                    Debug.Log($"[AbilityUIManager] Mostrando cooldown de Dash: {ui.cooldownText.text}s");
                }
            }
            else
            {
                // Habilidad lista, ocultar texto de cooldown
                ui.cooldownText.gameObject.SetActive(false);
            }
        }
        
        // Mostrar ícono de requisito si no se cumplen los requisitos
        if (ui.requirementIcon != null)
        {
            if (!requirementsMet)
            {
                ui.requirementIcon.gameObject.SetActive(true);
                
                // Si hay un texto en el ícono, actualizarlo
                TextMeshProUGUI iconText = ui.requirementIcon.GetComponentInChildren<TextMeshProUGUI>();
                if (iconText != null)
                {
                    iconText.text = requirementMessage;
                }
            }
            else
            {
                ui.requirementIcon.gameObject.SetActive(false);
            }
        }
        
        // Actualizar overlay de cooldown (opcional)
        if (ui.cooldownOverlay != null)
        {
            if (isInCooldown)
            {
                // Mostrar el fill del cooldown (de 1 a 0 durante el cooldown)
                float fillAmount = cooldownRemaining / ability.cooldown;
                ui.cooldownOverlay.fillAmount = fillAmount;
                ui.cooldownOverlay.color = cooldownColor;
            }
            else if (!hasMana)
            {
                // Sin mana suficiente
                ui.cooldownOverlay.fillAmount = 1f;
                ui.cooldownOverlay.color = noManaColor;
            }
            else if (!requirementsMet)
            {
                // Requisitos no cumplidos
                ui.cooldownOverlay.fillAmount = 1f;
                ui.cooldownOverlay.color = requirementNotMetColor;
            }
            else
            {
                // Habilidad lista
                ui.cooldownOverlay.fillAmount = 0f;
            }
        }
        
        // Actualizar imagen principal
        if (ui.abilityImage != null)
        {
            if (isInCooldown || !hasMana || !requirementsMet)
            {
                ui.abilityImage.color = new Color(1f, 1f, 1f, 0.5f); // Semi-transparente
            }
            else
            {
                ui.abilityImage.color = normalColor;
            }
        }
    }
    
    // Métodos auxiliares para verificar requisitos específicos de cada habilidad
    private bool CheckRequirements(BaseAbility ability)
    {
        // Si es StrongJumpAbility, verificar movimiento
        if (ability is StrongJumpAbility)
        {
            StrongJumpAbility jumpAbility = ability as StrongJumpAbility;
            return jumpAbility.IsMovingFastEnough();
        }
        
        // Si es EarthquakeAbility (compatibilidad)
        if (ability is EarthquakeAbility)
        {
            EarthquakeAbility earthquakeAbility = ability as EarthquakeAbility;
            return earthquakeAbility.IsMovingFastEnough();
        }
        
        // Por defecto, todos los requisitos se cumplen
        return true;
    }
    
    private string GetRequirementMessage(BaseAbility ability)
    {
        // Si es StrongJumpAbility o EarthquakeAbility y requiere movimiento
        if ((ability is StrongJumpAbility || ability is EarthquakeAbility) && !CheckRequirements(ability))
        {
            return "¡Muévete!";
        }
        
        // Mensaje predeterminado
        return "";
    }
}