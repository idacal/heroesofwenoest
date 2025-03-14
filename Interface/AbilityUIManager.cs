using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using PlayerAbilities;

public class AbilityUIManager : MonoBehaviour
{
    [Header("Referencia de UI")]
    [SerializeField] private RectTransform abilityContainer;
    [SerializeField] private GameObject abilitySlotPrefab;
    [SerializeField] private int maxAbilitySlots = 6;

    [Header("Configuración de Ranuras")]
    [SerializeField] private Color readyColor = Color.white;
    [SerializeField] private Color cooldownColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
    [SerializeField] private bool showAbilityTooltips = true;
    [SerializeField] private bool showCooldownText = true;
    [SerializeField] private bool showDebugInfo = true;

    // Referencias a componentes del jugador
    private PlayerNetwork playerNetwork;
    private PlayerAbilityManager abilityManager;

    // Referencias a slots de habilidad en la UI
    private List<AbilitySlot> abilitySlots = new List<AbilitySlot>();

    // Clase para gestionar un slot de habilidad
    [System.Serializable]
    private class AbilitySlot
    {
        public GameObject slotObject;
        public Image abilityIcon;
        public Image cooldownOverlay;
        public TextMeshProUGUI cooldownText;
        public TextMeshProUGUI keyBindText;
        public BaseAbility linkedAbility;
        public int slotIndex;
    }

    private void Awake()
    {
        Debug.Log("[AbilityUIManager] Initializing...");
        
        // Verificar si tenemos el container de habilidades
        if (abilityContainer == null)
        {
            Debug.LogError("[AbilityUIManager] abilityContainer no está asignado!");
            
            // Intentar encontrar un contenedor adecuado en el canvas
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                RectTransform[] possibleContainers = canvas.GetComponentsInChildren<RectTransform>();
                foreach (var container in possibleContainers)
                {
                    if (container.name.ToLower().Contains("ability") || 
                        container.name.ToLower().Contains("skill") || 
                        container.name.ToLower().Contains("bar"))
                    {
                        Debug.Log($"[AbilityUIManager] Candidato para abilityContainer encontrado: {container.name}");
                        
                        // Podría hacer una asignación automática aquí, pero es mejor que el usuario lo asigne manualmente
                    }
                }
            }
            
            enabled = false;
            return;
        }

        // Verificar que tengamos el prefab
        if (abilitySlotPrefab == null)
        {
            Debug.LogError("[AbilityUIManager] abilitySlotPrefab no está asignado!");
            enabled = false;
            return;
        }

        // Limpiar cualquier slot existente en el editor
        foreach (Transform child in abilityContainer)
        {
            Destroy(child.gameObject);
        }
        abilitySlots.Clear();
        
        Debug.Log("[AbilityUIManager] Inicializado correctamente");
    }

    private void Start()
    {
        Debug.Log("[AbilityUIManager] Starting...");
        
        // Crear slots vacíos para habilidades
        CreateAbilitySlots();
        
        // Iniciar la búsqueda de componentes del jugador
        StartCoroutine(FindPlayerComponents());
    }

    private IEnumerator FindPlayerComponents()
    {
        int maxRetries = 30;  // Aumentado para permitir más intentos
        int retryCount = 0;
        float initialDelay = 1.0f;  // Esperar un tiempo inicial para que todo se inicialice
        
        Debug.Log("[AbilityUIManager] Waiting initial delay before searching for player...");
        yield return new WaitForSeconds(initialDelay);
        
        while (retryCount < maxRetries)
        {
            // Buscar en todos los jugadores existentes
            PlayerNetwork[] allPlayers = FindObjectsOfType<PlayerNetwork>();
            Debug.Log($"[AbilityUIManager] Found {allPlayers.Length} PlayerNetwork components in scene");
            
            foreach (var player in allPlayers)
            {
                if (player.IsLocalPlayer)
                {
                    Debug.Log($"[AbilityUIManager] Encontrado jugador local: {player.name}");
                    
                    // Intenta obtener los componentes necesarios
                    playerNetwork = player;
                    abilityManager = player.GetComponent<PlayerAbilityManager>();
                    
                    if (abilityManager != null)
                    {
                        Debug.Log("[AbilityUIManager] Componentes encontrados con éxito");
                        
                        // Esperar un momento para asegurar que las habilidades estén inicializadas
                        yield return new WaitForSeconds(0.5f);
                        
                        SetupAbilityUI();
                        yield break;
                    }
                    else
                    {
                        Debug.LogWarning("[AbilityUIManager] Jugador local encontrado pero no tiene PlayerAbilityManager");
                    }
                }
            }
            
            // Si no se encontraron, esperar un tiempo y reintentar
            retryCount++;
            float waitTime = 1.0f;
            Debug.Log($"[AbilityUIManager] No se pudieron encontrar los componentes del jugador, reintentando en {waitTime} segundo(s)... (intento {retryCount}/{maxRetries})");
            yield return new WaitForSeconds(waitTime);
        }
        
        Debug.LogError("[AbilityUIManager] No se pudieron encontrar los componentes del jugador después de múltiples reintentos");
        
        // Como último recurso, buscar cualquier PlayerAbilityManager en la escena
        PlayerAbilityManager[] allManagers = FindObjectsOfType<PlayerAbilityManager>();
        if (allManagers.Length > 0)
        {
            Debug.LogWarning($"[AbilityUIManager] Usando el primer PlayerAbilityManager encontrado ({allManagers[0].name}) como recurso de emergencia");
            abilityManager = allManagers[0];
            SetupAbilityUI();
        }
    }

    private void CreateAbilitySlots()
    {
        Debug.Log($"[AbilityUIManager] Creating {maxAbilitySlots} ability slots");
        
        // Crear los slots de habilidad vacíos
        for (int i = 0; i < maxAbilitySlots; i++)
        {
            GameObject slotObject = Instantiate(abilitySlotPrefab, abilityContainer);
            slotObject.name = $"AbilitySlot_{i}";

            // Crear una nueva instancia de AbilitySlot
            AbilitySlot newSlot = new AbilitySlot
            {
                slotObject = slotObject,
                slotIndex = i
            };

            // Obtener referencias a componentes del slot
            newSlot.abilityIcon = slotObject.transform.Find("AbilityIcon")?.GetComponent<Image>();
            newSlot.cooldownOverlay = slotObject.transform.Find("CooldownOverlay")?.GetComponent<Image>();
            newSlot.cooldownText = slotObject.transform.Find("CooldownText")?.GetComponent<TextMeshProUGUI>();
            newSlot.keyBindText = slotObject.transform.Find("KeyBindText")?.GetComponent<TextMeshProUGUI>();

            // Verificar que se encontraron todas las referencias
            bool missingComponents = false;
            if (newSlot.abilityIcon == null) { 
                Debug.LogError($"[AbilityUIManager] AbilityIcon no encontrado en {slotObject.name}");
                missingComponents = true;
            }
            if (newSlot.cooldownOverlay == null) { 
                Debug.LogError($"[AbilityUIManager] CooldownOverlay no encontrado en {slotObject.name}");
                missingComponents = true;
            }
            if (newSlot.cooldownText == null) { 
                Debug.LogError($"[AbilityUIManager] CooldownText no encontrado en {slotObject.name}");
                missingComponents = true;
            }
            if (newSlot.keyBindText == null) { 
                Debug.LogError($"[AbilityUIManager] KeyBindText no encontrado en {slotObject.name}");
                missingComponents = true;
            }
            
            if (missingComponents) {
                Debug.LogError($"[AbilityUIManager] Slot {i} tiene componentes faltantes. Verifica la estructura del prefab.");
            }

            // Configurar estado inicial
            if (newSlot.abilityIcon != null)
            {
                // Inicialmente sin icono
                newSlot.abilityIcon.enabled = false;
            }

            if (newSlot.cooldownOverlay != null)
            {
                // Inicialmente sin cooldown
                newSlot.cooldownOverlay.fillAmount = 0f;
                newSlot.cooldownOverlay.enabled = false;
            }

            if (newSlot.cooldownText != null)
            {
                newSlot.cooldownText.text = "";
                newSlot.cooldownText.enabled = false;
            }

            if (newSlot.keyBindText != null)
            {
                // Tecla por defecto
                newSlot.keyBindText.text = $"{i+1}";
            }

            // Agregar a la lista
            abilitySlots.Add(newSlot);
        }
        
        Debug.Log($"[AbilityUIManager] Created {abilitySlots.Count} ability slots");
    }

    private void SetupAbilityUI()
    {
        if (abilityManager == null)
        {
            Debug.LogError("[AbilityUIManager] No se puede configurar UI sin PlayerAbilityManager");
            return;
        }

        Debug.Log("[AbilityUIManager] Setting up UI with abilities from PlayerAbilityManager");
        
        // Actualizar los slots con las habilidades actuales
        UpdateAbilitySlots();
        
        // Subscribirse a eventos para actualización continua (si necesario)
        // Por ahora usaremos Update para mantener la UI actualizada
    }

    private void UpdateAbilitySlots()
    {
        if (abilityManager == null) {
            Debug.LogWarning("[AbilityUIManager] Cannot update ability slots - abilityManager is null");
            return;
        }
        
        Debug.Log("[AbilityUIManager] Updating ability slots");
        
        int abilitiesFound = 0;
        
        // Limpiar slots
        for (int i = 0; i < abilitySlots.Count; i++)
        {
            AbilitySlot slot = abilitySlots[i];
            BaseAbility ability = abilityManager.GetAbilityBySlot(i);

            // Actualizar el slot con la habilidad (o null si no hay habilidad)
            UpdateSlotWithAbility(slot, ability);
            
            if (ability != null) {
                abilitiesFound++;
            }
        }
        
        Debug.Log($"[AbilityUIManager] Updated ability slots. Found {abilitiesFound} abilities.");
    }

    private void UpdateSlotWithAbility(AbilitySlot slot, BaseAbility ability)
    {
        slot.linkedAbility = ability;

        if (ability == null)
        {
            // No hay habilidad, desactivar elementos visuales
            if (slot.abilityIcon != null)
                slot.abilityIcon.enabled = false;

            if (slot.cooldownOverlay != null)
                slot.cooldownOverlay.enabled = false;

            if (slot.cooldownText != null)
                slot.cooldownText.enabled = false;

            if (slot.keyBindText != null)
                slot.keyBindText.enabled = false;

            return;
        }

        // Hay una habilidad, configurar visuales
        if (slot.abilityIcon != null)
        {
            if (ability.icon != null) {
                slot.abilityIcon.enabled = true;
                slot.abilityIcon.sprite = ability.icon;
                Debug.Log($"[AbilityUIManager] Slot {slot.slotIndex}: Set icon for {ability.abilityName}");
            }
            else {
                Debug.LogWarning($"[AbilityUIManager] Ability {ability.abilityName} has no icon!");
                slot.abilityIcon.enabled = false;
            }
        }

        if (slot.keyBindText != null)
        {
            slot.keyBindText.enabled = true;
            slot.keyBindText.text = ability.activationKey.ToString();
        }

        // Resetear cooldown
        if (slot.cooldownOverlay != null)
        {
            slot.cooldownOverlay.fillAmount = 0f;
            slot.cooldownOverlay.enabled = false;
        }

        if (slot.cooldownText != null)
        {
            slot.cooldownText.text = "";
            slot.cooldownText.enabled = false;
        }
        
        if (showDebugInfo) {
            Debug.Log($"[AbilityUIManager] Slot {slot.slotIndex} updated with {ability.abilityName}, key: {ability.activationKey}");
        }
    }

    private void Update()
    {
        // Actualizar estado de cooldown para cada slot
        for (int i = 0; i < abilitySlots.Count; i++)
        {
            AbilitySlot slot = abilitySlots[i];
            
            if (slot.linkedAbility != null)
            {
                UpdateSlotCooldown(slot);
            }
        }
    }

    private void UpdateSlotCooldown(AbilitySlot slot)
    {
        if (slot.linkedAbility == null) return;

        // Verificar estado de cooldown
        float remainingCooldown = slot.linkedAbility.GetRemainingCooldown();
        bool isInCooldown = remainingCooldown > 0f;

        // Actualizar overlay de cooldown
        if (slot.cooldownOverlay != null)
        {
            slot.cooldownOverlay.enabled = isInCooldown;
            
            if (isInCooldown)
            {
                float cooldownDuration = slot.linkedAbility.cooldown;
                float cooldownRatio = remainingCooldown / cooldownDuration;
                
                // El fillAmount va de 1 a 0 para representar el progreso del cooldown
                slot.cooldownOverlay.fillAmount = cooldownRatio;
            }
            else
            {
                slot.cooldownOverlay.fillAmount = 0f;
            }
        }

        // Actualizar texto de cooldown
        if (slot.cooldownText != null && showCooldownText)
        {
            slot.cooldownText.enabled = isInCooldown;
            
            if (isInCooldown)
            {
                slot.cooldownText.text = remainingCooldown.ToString("F1");
            }
            else
            {
                slot.cooldownText.text = "";
            }
        }

        // Actualizar color del icono
        if (slot.abilityIcon != null)
        {
            slot.abilityIcon.color = isInCooldown ? cooldownColor : readyColor;
        }
    }

    // Método público para forzar actualización de UI (puede ser llamado desde Player)
    public void RefreshAbilityUI()
    {
        if (abilityManager != null)
        {
            Debug.Log("[AbilityUIManager] Forcing UI refresh");
            UpdateAbilitySlots();
        }
        else
        {
            Debug.LogWarning("[AbilityUIManager] Cannot refresh UI - no abilityManager");
            
            // Intentar buscar el manager nuevamente
            if (playerNetwork != null) {
                abilityManager = playerNetwork.GetComponent<PlayerAbilityManager>();
                if (abilityManager != null) {
                    Debug.Log("[AbilityUIManager] Found abilityManager, refreshing UI");
                    UpdateAbilitySlots();
                }
            } else {
                StartCoroutine(FindPlayerComponents());
            }
        }
    }
}