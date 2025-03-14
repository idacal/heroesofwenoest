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
        // Verificar si tenemos el container de habilidades
        if (abilityContainer == null)
        {
            Debug.LogError("[AbilityUIManager] abilityContainer no está asignado!");
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
    }

    private void Start()
    {
        // Iniciar la búsqueda de componentes del jugador
        StartCoroutine(FindPlayerComponents());

        // Crear slots vacíos para habilidades
        CreateAbilitySlots();
    }

    private IEnumerator FindPlayerComponents()
    {
        int maxRetries = 15;
        int retryCount = 0;
        
        while (retryCount < maxRetries)
        {
            // Buscar en todos los jugadores existentes
            PlayerNetwork[] allPlayers = FindObjectsOfType<PlayerNetwork>();
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
                        SetupAbilityUI();
                        yield break;  // Corregido: antes era return; debe ser yield break;
                    }
                }
            }
            
            // Si no se encontraron, esperar un tiempo y reintentar
            retryCount++;
            float waitTime = 1.0f;
            Debug.LogWarning($"[AbilityUIManager] No se pudieron encontrar los componentes del jugador, reintentando en {waitTime} segundo(s)... (intento {retryCount}/{maxRetries})");
            yield return new WaitForSeconds(waitTime);
        }
        
        Debug.LogError("[AbilityUIManager] No se pudieron encontrar los componentes del jugador después de múltiples reintentos");
    }

    private void CreateAbilitySlots()
    {
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
    }

    private void SetupAbilityUI()
    {
        if (abilityManager == null)
        {
            Debug.LogError("[AbilityUIManager] No se puede configurar UI sin PlayerAbilityManager");
            return;
        }

        // Actualizar los slots con las habilidades actuales
        UpdateAbilitySlots();
    }

    private void UpdateAbilitySlots()
    {
        // Limpiar slots
        for (int i = 0; i < abilitySlots.Count; i++)
        {
            AbilitySlot slot = abilitySlots[i];
            BaseAbility ability = abilityManager.GetAbilityBySlot(i);

            // Actualizar el slot con la habilidad (o null si no hay habilidad)
            UpdateSlotWithAbility(slot, ability);
        }
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
            slot.abilityIcon.enabled = true;
            slot.abilityIcon.sprite = ability.icon;
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
            UpdateAbilitySlots();
        }
    }
}