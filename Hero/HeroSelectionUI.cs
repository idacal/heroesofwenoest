using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

public class HeroSelectionUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject heroSelectionPanel;
    [SerializeField] private Button[] heroButtons;
    [SerializeField] private Button readyButton;
    [SerializeField] public TextMeshProUGUI selectionInfoText;
    [SerializeField] private TextMeshProUGUI timerText;
    
    [Header("Hero Info Panel")]
    [SerializeField] private GameObject heroInfoPanel;
    [SerializeField] private Image heroPortrait;
    [SerializeField] private TextMeshProUGUI heroNameText;
    [SerializeField] private TextMeshProUGUI heroDescriptionText;
    [SerializeField] private Image[] abilityIcons;
    [SerializeField] private TextMeshProUGUI[] abilityNames;
    [SerializeField] private TextMeshProUGUI[] abilityDescriptions;
    
    [Header("Player Selection Indicators")]
    [SerializeField] private Transform playerSelectionsContainer;
    [SerializeField] private GameObject playerSelectionPrefab;
    
    [Header("Visual Feedback")]
    [SerializeField] private Color normalButtonColor = Color.white;
    [SerializeField] private Color selectedButtonColor = new Color(0.8f, 0.8f, 1f);
    [SerializeField] private Color disabledButtonColor = new Color(0.5f, 0.5f, 0.5f);
    [SerializeField] private GameObject selectionHighlight;
    
    // Reference to the hero selection manager
    private HeroSelectionManager heroSelectionManager;
    
    // Track the currently selected hero index
    private int selectedHeroIndex = -1;
    
    // Track player selection indicators by client ID
    private Dictionary<ulong, GameObject> playerSelectionIndicators = new Dictionary<ulong, GameObject>();
    
    private void Awake()
    {
        Debug.Log("[HeroSelectionUI] Awake called");
        
        // Get reference to hero selection manager
        heroSelectionManager = FindObjectOfType<HeroSelectionManager>();
        
        // Validate UI components
        ValidateComponents();
        
        // Initially hide panels
        if (heroInfoPanel != null)
        {
            heroInfoPanel.SetActive(false);
        }
        
        if (heroSelectionPanel != null)
        {
            heroSelectionPanel.SetActive(false);
        }
        else
        {
            Debug.LogError("[HeroSelectionUI] heroSelectionPanel is null!");
        }
        
        // Set up ready button
        if (readyButton != null)
        {
            readyButton.onClick.AddListener(OnReadyButtonClicked);
            readyButton.interactable = false;
        }
        else
        {
            Debug.LogError("[HeroSelectionUI] readyButton is null!");
        }
    }
    
    private void OnEnable()
    {
        // Make sure canvas is properly configured
        EnsureProperCanvasSetup();
    }
    
    private void ValidateComponents()
    {
        // Check canvas configuration
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            // Ensure canvas has GraphicRaycaster
            GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster == null)
            {
                Debug.LogError("[HeroSelectionUI] Canvas missing GraphicRaycaster! Adding one...");
                raycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
            }
            
            // Ensure raycaster is enabled
            raycaster.enabled = true;
            
            // Check canvas render mode
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.renderMode != RenderMode.ScreenSpaceCamera)
            {
                Debug.LogWarning("[HeroSelectionUI] Canvas is not in ScreenSpace mode. This might affect UI interaction.");
            }
        }
        else
        {
            Debug.LogError("[HeroSelectionUI] UI is not inside a Canvas!");
        }
        
        // Check hero buttons
        if (heroButtons == null || heroButtons.Length == 0)
        {
            Debug.LogError("[HeroSelectionUI] Hero buttons array is null or empty!");
        }
        
        // Check hero selection manager
        if (heroSelectionManager == null)
        {
            Debug.LogError("[HeroSelectionUI] HeroSelectionManager not found in scene!");
            heroSelectionManager = FindObjectOfType<HeroSelectionManager>();
        }
    }
    
    private void Start()
    {
        Debug.Log("[HeroSelectionUI] Start called");
        
        // Hide connection panels
        HideAllConnectionPanels();
        
        // Set up hero buttons
        SetupHeroButtons();
        
        // Start timer update
        StartCoroutine(UpdateSelectionTimer());
    }
    
    private void HideAllConnectionPanels()
    {
        // Find and hide all connection panels by tag
        GameObject[] connectionPanels = GameObject.FindGameObjectsWithTag("ConnectionPanel");
        foreach (var panel in connectionPanels)
        {
            Debug.Log($"[HeroSelectionUI] Found connection panel by tag: {panel.name}, hiding it");
            panel.SetActive(false);
        }
        
        // Try by common names
        string[] possibleNames = { "ConnectionPanel", "NetworkUI", "ConnectionUI", "NetworkPanel" };
        foreach (string name in possibleNames)
        {
            GameObject panel = GameObject.Find(name);
            if (panel != null)
            {
                Debug.Log($"[HeroSelectionUI] Found connection panel by name: {panel.name}, hiding it");
                panel.SetActive(false);
            }
        }
    }
    
    private IEnumerator UpdateSelectionTimer()
    {
        while (true)
        {
            // Update timer text if available
            if (timerText != null && heroSelectionManager != null)
            {
                float timeRemaining = heroSelectionManager.GetTimeRemaining();
                
                if (timeRemaining > 0)
                {
                    timerText.text = $"Time left: {Mathf.CeilToInt(timeRemaining)}s";
                    
                    // Change color based on time remaining
                    if (timeRemaining <= 10)
                    {
                        timerText.color = Color.red;
                    }
                    else if (timeRemaining <= 20)
                    {
                        timerText.color = Color.yellow;
                    }
                    else
                    {
                        timerText.color = Color.white;
                    }
                }
                else
                {
                    timerText.text = "Time's up!";
                    timerText.color = Color.red;
                }
            }
            
            yield return new WaitForSeconds(0.5f);
        }
    }
    
    private void OnDestroy()
    {
        // Clean up button listeners
        if (readyButton != null)
        {
            readyButton.onClick.RemoveAllListeners();
        }
        
        if (heroButtons != null)
        {
            foreach (var button in heroButtons)
            {
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                }
            }
        }
    }
    
    private void EnsureProperCanvasSetup()
    {
        // Get parent canvas and ensure proper configuration
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            // Set high sorting order to ensure it's visible
            canvas.sortingOrder = 100;
            
            // Ensure it has a graphic raycaster
            GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster == null)
            {
                raycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
                Debug.Log("[HeroSelectionUI] Added GraphicRaycaster to Canvas");
            }
            
            // Make sure raycaster is enabled
            raycaster.enabled = true;
        }
    }
    
    private void SetupHeroButtons()
    {
        Debug.Log("[HeroSelectionUI] Setting up hero buttons");
        
        if (heroButtons == null || heroSelectionManager == null)
        {
            Debug.LogError("[HeroSelectionUI] Hero buttons array or selection manager is null!");
            return;
        }
        
        // Get available heroes from the manager
        HeroDefinition[] availableHeroes = null;
        
        // Try to get hero definitions from the manager
        try
        {
            availableHeroes = heroSelectionManager.GetAvailableHeroes();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[HeroSelectionUI] Failed to get hero definitions from manager: {e.Message}");
        }
        
        // Set up each button
        for (int i = 0; i < heroButtons.Length; i++)
        {
            if (heroButtons[i] == null) continue;
            
            int heroIndex = i; // Capture for lambda
            
            // Clear existing listeners
            heroButtons[i].onClick.RemoveAllListeners();
            
            // Add click handler
            heroButtons[i].onClick.AddListener(() => {
                Debug.Log($"[HeroSelectionUI] Hero button {heroIndex} clicked");
                OnHeroButtonClicked(heroIndex);
            });
            
            // Configure button appearance if heroes available
            if (availableHeroes != null && i < availableHeroes.Length && availableHeroes[i] != null)
            {
                ConfigureHeroButton(heroButtons[i], availableHeroes[i]);
                heroButtons[i].gameObject.SetActive(true);
            }
            else
            {
                // No hero for this button - hide it
                heroButtons[i].gameObject.SetActive(false);
            }
        }
    }
    
    private void ConfigureHeroButton(Button button, HeroDefinition hero)
    {
        if (button == null || hero == null) return;
        
        // Get text component
        TextMeshProUGUI buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
        if (buttonText != null)
        {
            buttonText.text = hero.heroName;
        }
        
        // Get image component
        Image buttonImage = button.GetComponentInChildren<Image>();
        if (buttonImage != null && hero.portrait != null)
        {
            buttonImage.sprite = hero.portrait;
            buttonImage.preserveAspect = true;
            buttonImage.raycastTarget = true;
        }
        
        // Ensure button is interactive
        button.interactable = true;
    }
    
    // Public method to show the hero selection UI
    public void Show()
    {
        Debug.Log("[HeroSelectionUI] Show() called");
        
        // Hide connection panels
        HideAllConnectionPanels();
        
        // Ensure proper canvas setup
        EnsureProperCanvasSetup();
        
        // Show the hero selection panel
        if (heroSelectionPanel != null)
        {
            heroSelectionPanel.SetActive(true);
            
            // Reset selection state
            selectedHeroIndex = -1;
            
            // Reset ready button state
            if (readyButton != null)
            {
                readyButton.interactable = false;
            }
            
            // Reset info text
            if (selectionInfoText != null)
            {
                selectionInfoText.text = "Select your hero";
            }
            
            // Set up hero buttons
            SetupHeroButtons();
        }
        else
        {
            Debug.LogError("[HeroSelectionUI] heroSelectionPanel is null!");
        }
    }
    
    // Public method to hide the hero selection UI
    public void Hide()
    {
        Debug.Log("[HeroSelectionUI] Hide() called");
        
        // Hide all UI elements
        if (heroSelectionPanel != null)
        {
            heroSelectionPanel.SetActive(false);
        }
        
        if (heroInfoPanel != null)
        {
            heroInfoPanel.SetActive(false);
        }
        
        // Deactivate the game object itself to ensure it stays hidden
        gameObject.SetActive(false);
        
        // Also deactivate canvas if possible
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvas.enabled = false;
        }
    }
    
    // Method to force all UI elements active (for debugging or recovery)
    public void ForceAllUIElementsActive()
    {
        Debug.Log("[HeroSelectionUI] ForceAllUIElementsActive called");
        
        // Activate main GameObject
        gameObject.SetActive(true);
        
        // Activate main panel
        if (heroSelectionPanel != null)
        {
            heroSelectionPanel.SetActive(true);
        }
        
        // Activate all child objects
        foreach (Transform child in transform)
        {
            child.gameObject.SetActive(true);
        }
        
        // Ensure all buttons are interactive
        if (heroButtons != null)
        {
            foreach (var button in heroButtons)
            {
                if (button != null)
                {
                    button.interactable = true;
                    
                    // Ensure image is raycaster target
                    Image buttonImage = button.GetComponent<Image>();
                    if (buttonImage != null)
                    {
                        buttonImage.raycastTarget = true;
                    }
                }
            }
        }
        
        // Reset selection state
        UpdateHeroSelectionUI(-1);
        if (selectionInfoText != null)
        {
            selectionInfoText.text = "Select your hero";
        }
    }
    
    // Called when a hero button is clicked
    public void OnHeroButtonClicked(int heroIndex)
    {
        Debug.Log($"[HeroSelectionUI] OnHeroButtonClicked: {heroIndex}");
        
        // Update local selection
        selectedHeroIndex = heroIndex;
        
        // Update UI to show this hero is selected
        UpdateHeroSelectionUI(heroIndex);
        
        // Enable the ready button
        if (readyButton != null)
        {
            readyButton.interactable = true;
        }
        
        // Show detailed info about this hero
        DisplayHeroInfo(heroIndex);
        
        // Tell the selection manager about our choice
        if (heroSelectionManager != null)
        {
            heroSelectionManager.SelectHero(heroIndex);
            
            // Update info text
            if (selectionInfoText != null)
            {
                string heroName = "Selected hero";
                
                // Try to get hero name
                HeroDefinition hero = heroSelectionManager.GetHeroDefinition(heroIndex);
                if (hero != null)
                {
                    heroName = hero.heroName;
                }
                
                selectionInfoText.text = $"Selected: {heroName}\nClick 'Ready' to confirm";
            }
        }
        else
        {
            Debug.LogError("[HeroSelectionUI] heroSelectionManager is null!");
        }
    }
    
    // Called when the ready button is clicked
    private void OnReadyButtonClicked()
    {
        Debug.Log("[HeroSelectionUI] Ready button clicked with selected hero: " + selectedHeroIndex);
        
        // Confirm hero selection
        if (selectedHeroIndex >= 0 && heroSelectionManager != null)
        {
            // Notify the manager that we're ready
            heroSelectionManager.ConfirmHeroSelection();
            
            // Disable the ready button and hero selection buttons
            readyButton.interactable = false;
            DisableHeroButtons();
            
            // Update info text
            if (selectionInfoText != null)
            {
                // Get hero name
                string heroName = "selected hero";
                HeroDefinition hero = heroSelectionManager.GetHeroDefinition(selectedHeroIndex);
                if (hero != null)
                {
                    heroName = hero.heroName;
                }
                
                selectionInfoText.text = $"Ready with {heroName}!\nWaiting for other players...";
            }
        }
    }
    
    // Update hero selection UI to highlight selected hero
    private void UpdateHeroSelectionUI(int heroIndex)
    {
        // Highlight the selected hero button and unhighlight others
        for (int i = 0; i < heroButtons.Length; i++)
        {
            if (heroButtons[i] != null)
            {
                // Change button color
                Image buttonImage = heroButtons[i].GetComponent<Image>();
                if (buttonImage != null)
                {
                    buttonImage.color = (i == heroIndex) ? selectedButtonColor : normalButtonColor;
                }
                
                // Activate selection highlight if available
                Transform highlight = heroButtons[i].transform.Find("SelectionHighlight");
                if (highlight != null)
                {
                    highlight.gameObject.SetActive(i == heroIndex);
                }
            }
        }
        
        // Move selection highlight object if available
        if (selectionHighlight != null && heroIndex >= 0 && heroIndex < heroButtons.Length)
        {
            selectionHighlight.SetActive(true);
            selectionHighlight.transform.position = heroButtons[heroIndex].transform.position;
        }
        else if (selectionHighlight != null)
        {
            selectionHighlight.SetActive(false);
        }
    }
    
    // Disable hero buttons once a selection is confirmed
    private void DisableHeroButtons()
    {
        for (int i = 0; i < heroButtons.Length; i++)
        {
            if (heroButtons[i] != null)
            {
                heroButtons[i].interactable = false;
                
                // Change color to indicate disabled state except for selected button
                Image buttonImage = heroButtons[i].GetComponent<Image>();
                if (buttonImage != null && i != selectedHeroIndex)
                {
                    buttonImage.color = disabledButtonColor;
                }
            }
        }
    }
    
    // Display detailed info about a hero
    private void DisplayHeroInfo(int heroIndex)
    {
        Debug.Log($"[HeroSelectionUI] DisplayHeroInfo for hero index: {heroIndex}");
        
        if (heroInfoPanel == null || heroSelectionManager == null) return;
        
        // Get hero definition
        HeroDefinition heroDefinition = heroSelectionManager.GetHeroDefinition(heroIndex);
        
        if (heroDefinition != null)
        {
            // Show the info panel
            heroInfoPanel.SetActive(true);
            
            // Set hero details
            if (heroNameText != null) heroNameText.text = heroDefinition.heroName;
            if (heroDescriptionText != null) heroDescriptionText.text = heroDefinition.description;
            
            if (heroPortrait != null && heroDefinition.portrait != null)
            {
                heroPortrait.sprite = heroDefinition.portrait;
                heroPortrait.preserveAspect = true;
            }
            
            // Update ability info
            UpdateAbilityInfo(heroDefinition);
        }
        else
        {
            Debug.LogError($"[HeroSelectionUI] No hero definition found for index: {heroIndex}");
            
            // Show error in info panel
            if (heroInfoPanel != null)
            {
                heroInfoPanel.SetActive(true);
                
                if (heroNameText != null) heroNameText.text = "Hero Not Found";
                if (heroDescriptionText != null) heroDescriptionText.text = "Could not load hero data for index " + heroIndex;
                
                // Hide ability icons
                HideAbilityIcons();
            }
        }
    }
    
    // Update ability info display
    private void UpdateAbilityInfo(HeroDefinition heroDefinition)
    {
        // Check if we have abilities to display
        if (heroDefinition.abilities == null || heroDefinition.abilities.Count == 0)
        {
            HideAbilityIcons();
            return;
        }
        
        // Update each ability slot
        for (int i = 0; i < abilityIcons.Length; i++)
        {
            if (i < heroDefinition.abilities.Count)
            {
                var ability = heroDefinition.abilities[i];
                
                // Set icon
                if (abilityIcons[i] != null)
                {
                    if (ability.icon != null)
                    {
                        abilityIcons[i].sprite = ability.icon;
                        abilityIcons[i].gameObject.SetActive(true);
                    }
                    else
                    {
                        abilityIcons[i].gameObject.SetActive(false);
                    }
                }
                
                // Set name
                if (abilityNames != null && i < abilityNames.Length && abilityNames[i] != null)
                {
                    abilityNames[i].text = ability.abilityName;
                }
                
                // Set description (if available)
                if (abilityDescriptions != null && i < abilityDescriptions.Length && abilityDescriptions[i] != null)
                {
                    abilityDescriptions[i].text = ability.abilityName; // Use name as fallback for description
                }
            }
            else
            {
                // Hide unused slots
                if (abilityIcons[i] != null)
                {
                    abilityIcons[i].gameObject.SetActive(false);
                }
                
                if (abilityNames != null && i < abilityNames.Length && abilityNames[i] != null)
                {
                    abilityNames[i].text = string.Empty;
                }
                
                if (abilityDescriptions != null && i < abilityDescriptions.Length && abilityDescriptions[i] != null)
                {
                    abilityDescriptions[i].text = string.Empty;
                }
            }
        }
    }
    
    // Hide all ability icons
    private void HideAbilityIcons()
    {
        if (abilityIcons != null)
        {
            foreach (var icon in abilityIcons)
            {
                if (icon != null)
                {
                    icon.gameObject.SetActive(false);
                }
            }
        }
    }
    
    // Called by HeroSelectionManager to update player selections
    public void UpdatePlayerSelection(ulong clientId, int heroIndex, bool isReady)
    {
        // Get or create indicator for this player
        GameObject indicator = EnsurePlayerIndicator(clientId);
        
        if (indicator != null)
        {
            // Update the indicator to show selected hero and ready status
            UpdatePlayerIndicator(indicator, clientId, heroIndex, isReady);
        }
    }
    
    // Get or create player indicator
    private GameObject EnsurePlayerIndicator(ulong clientId)
    {
        // Check if we already have an indicator
        if (playerSelectionIndicators.TryGetValue(clientId, out GameObject indicator))
        {
            return indicator;
        }
        
        // Create new indicator
        if (playerSelectionPrefab != null && playerSelectionsContainer != null)
        {
            GameObject newIndicator = Instantiate(playerSelectionPrefab, playerSelectionsContainer);
            
            // Store reference
            playerSelectionIndicators[clientId] = newIndicator;
            
            return newIndicator;
        }
        
        return null;
    }
    
    // Update player indicator
    private void UpdatePlayerIndicator(GameObject indicator, ulong clientId, int heroIndex, bool isReady)
    {
        // Update player name text
        TextMeshProUGUI playerText = indicator.GetComponentInChildren<TextMeshProUGUI>();
        if (playerText != null)
        {
            bool isLocalPlayer = clientId == NetworkManager.Singleton.LocalClientId;
            
            // Get hero name if available
            string heroName = string.Empty;
            if (heroIndex >= 0 && heroSelectionManager != null)
            {
                HeroDefinition hero = heroSelectionManager.GetHeroDefinition(heroIndex);
                if (hero != null)
                {
                    heroName = $" - {hero.heroName}";
                }
            }
            
            // Set player name with ready status
            string readyStatus = isReady ? " (Ready)" : "";
            string playerPrefix = isLocalPlayer ? "You" : $"Player {clientId}";
            playerText.text = $"{playerPrefix}{heroName}{readyStatus}";
            
            // Set color based on ready status
            if (isReady)
            {
                playerText.color = isLocalPlayer ? new Color(0, 0.8f, 0) : new Color(0, 0.6f, 0);
            }
            else
            {
                playerText.color = isLocalPlayer ? Color.green : Color.white;
            }
        }
        
        // Update hero icon if available
        Image heroIcon = indicator.transform.Find("HeroIcon")?.GetComponent<Image>();
        if (heroIcon != null && heroIndex >= 0 && heroSelectionManager != null)
        {
            HeroDefinition hero = heroSelectionManager.GetHeroDefinition(heroIndex);
            if (hero != null && hero.portrait != null)
            {
                heroIcon.sprite = hero.portrait;
                heroIcon.color = Color.white;
                heroIcon.gameObject.SetActive(true);
            }
            else
            {
                heroIcon.gameObject.SetActive(false);
            }
        }
        
        // Update ready indicator if available
        GameObject readyIndicatorObj = indicator.transform.Find("ReadyIndicator")?.gameObject;
        if (readyIndicatorObj != null)
        {
            readyIndicatorObj.SetActive(isReady);
        }
    }
    
    // Update hero buttons display with available heroes
    public void UpdateHeroButtonsDisplay(HeroDefinition[] heroes)
    {
        if (heroButtons == null)
        {
            Debug.LogError("[HeroSelectionUI] Hero buttons array is not configured");
            return;
        }
        
        int heroCount = heroes?.Length ?? 0;
        Debug.Log($"[HeroSelectionUI] Updating hero buttons with {heroCount} available heroes");
        
        // Configure each button
        for (int i = 0; i < heroButtons.Length; i++)
        {
            if (heroButtons[i] == null) continue;
            
            if (i < heroCount && heroes[i] != null)
            {
                ConfigureHeroButton(heroButtons[i], heroes[i]);
                heroButtons[i].gameObject.SetActive(true);
            }
            else
            {
                heroButtons[i].gameObject.SetActive(false);
            }
        }
    }
}