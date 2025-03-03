using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AbilityUIManager : MonoBehaviour
{
    [System.Serializable]
    public class AbilityUI
    {
        public Image abilityImage;
        public Image cooldownOverlay; // Opcional, puedes dejarlo sin asignar
        public TextMeshProUGUI keyText;
        public TextMeshProUGUI cooldownText;
    }

    [Header("Referencias")]
    [SerializeField] private PlayerAbility playerAbility;

    [Header("UI de Habilidades")]
    [SerializeField] private AbilityUI[] abilityUIs = new AbilityUI[4]; // Q, W, E, R

    [Header("Configuración")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color cooldownColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
    [SerializeField] private Color noManaColor = new Color(0.3f, 0.3f, 0.8f, 0.7f);
    
    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    // Referencia al PlayerStats para verificar mana
    private PlayerStats playerStats;

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
        }
        
        // Encontrar referencias a los componentes del jugador
        FindPlayerComponents();
    }

    private void FindPlayerComponents()
    {
        if (playerAbility == null)
        {
            PlayerNetwork[] players = FindObjectsOfType<PlayerNetwork>();
            foreach (PlayerNetwork player in players)
            {
                if (player.IsLocalPlayer)
                {
                    playerAbility = player.GetComponent<PlayerAbility>();
                    playerStats = player.GetComponent<PlayerStats>();
                    
                    if (playerAbility != null && playerStats != null)
                    {
                        Debug.Log("[AbilityUIManager] Se encontraron componentes del jugador local");
                    }
                    break;
                }
            }
            
            if (playerAbility == null || playerStats == null)
            {
                Debug.LogWarning("[AbilityUIManager] No se pudieron encontrar los componentes del jugador, reintentando en 1 segundo...");
                Invoke("FindPlayerComponents", 1f);
            }
        }
    }

    private void Update()
    {
        if (playerAbility == null || playerStats == null) 
        {
            return;
        }
        
        // Actualizar UI de cada habilidad
        int count = Mathf.Min(abilityUIs.Length, playerAbility.GetAbilityCount());
        for (int i = 0; i < count; i++)
        {
            UpdateAbilityUI(i);
        }
    }

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
        
        // Debug logging
        if (showDebugLogs && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[AbilityUIManager] Habilidad {index} ({ability.name}) - " +
                     $"En cooldown: {isInCooldown}, " +
                     $"Tiempo restante: {cooldownRemaining:F1}s, " +
                     $"Tiene maná: {hasMana}");
        }
        
        // Actualizar texto de cooldown (similar a IntegratedDashCooldown)
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
                // Habilidad lista, ocultar texto
                ui.cooldownText.gameObject.SetActive(false);
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
            else
            {
                // Habilidad lista
                ui.cooldownOverlay.fillAmount = 0f;
            }
        }
        
        // Actualizar imagen principal
        if (ui.abilityImage != null)
        {
            if (isInCooldown || !hasMana)
            {
                ui.abilityImage.color = new Color(1f, 1f, 1f, 0.5f); // Semi-transparente
            }
            else
            {
                ui.abilityImage.color = normalColor;
            }
        }
    }
}