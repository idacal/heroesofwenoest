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
    [SerializeField] private TextMeshProUGUI selectionInfoText;
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
    
    // Track the currently selected hero index (local)
    private int selectedHeroIndex = -1;
    
    // Track player selection indicators by client ID
    private Dictionary<ulong, GameObject> playerSelectionIndicators = new Dictionary<ulong, GameObject>();
    
    // Variable para rastrear si el panel se ha mostrado correctamente
    private bool hasInitialized = false;
    
    // Contador para reintentos
    private int initRetries = 0;
    private const int MAX_RETRIES = 3;
    
    private void Awake()
    {
        Debug.Log("[HeroSelectionUI] Awake called");
        
        // Asegurarnos de que todos los componentes están correctamente configurados
        ValidateComponents();
        
        // Ocultar el panel de información del héroe hasta que se seleccione uno
        if (heroInfoPanel != null)
        {
            heroInfoPanel.SetActive(false);
        }
        
        // Inicialmente ocultar el panel de selección de héroe
        if (heroSelectionPanel != null)
        {
            heroSelectionPanel.SetActive(false);
        }
        else
        {
            Debug.LogError("[HeroSelectionUI] heroSelectionPanel is null!");
        }
        
        // Configurar el botón Ready para que esté deshabilitado inicialmente
        if (readyButton != null)
        {
            readyButton.onClick.AddListener(OnReadyButtonClicked);
            readyButton.interactable = false;
            Debug.Log("[HeroSelectionUI] Ready button listener set up");
        }
        else
        {
            Debug.LogError("[HeroSelectionUI] readyButton is null!");
        }
    }
    
    private void OnEnable()
    {
        // Make sure canvas is properly configured when enabled
        EnsureProperCanvasSetup();
    }
    
    private void EnsureProperCanvasSetup()
    {
        // Get parent canvas and ensure it has proper sorting order
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas != null)
        {
            // Set a high sorting order to ensure this canvas is on top
            parentCanvas.sortingOrder = 100;
            Debug.Log($"[HeroSelectionUI] Canvas sorting order set to {parentCanvas.sortingOrder}");
            
            // Ensure it has a graphic raycaster
            GraphicRaycaster raycaster = parentCanvas.GetComponent<GraphicRaycaster>();
            if (raycaster == null)
            {
                raycaster = parentCanvas.gameObject.AddComponent<GraphicRaycaster>();
                Debug.Log("[HeroSelectionUI] Added GraphicRaycaster to Canvas");
            }
            
            // Make sure the raycaster is enabled
            raycaster.enabled = true;
        }
    }
    
    private void ValidateComponents()
    {
        // Verificar que el Canvas tiene un GraphicRaycaster
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            UnityEngine.UI.GraphicRaycaster raycaster = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (raycaster == null)
            {
                Debug.LogError("[HeroSelectionUI] Canvas doesn't have a GraphicRaycaster! Adding one...");
                raycaster = canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }
            
            // Ensure the raycaster is enabled
            raycaster.enabled = true;
            
            // Set canvas to front
            canvas.sortingOrder = 100;
            Debug.Log($"[HeroSelectionUI] Canvas sorting order set to {canvas.sortingOrder}");
            
            // Verificar que el Canvas está en modo correcto
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.renderMode != RenderMode.ScreenSpaceCamera)
            {
                Debug.LogWarning("[HeroSelectionUI] Canvas is not in ScreenSpace mode. This might affect UI interaction.");
            }
        }
        else
        {
            Debug.LogError("[HeroSelectionUI] UI is not inside a Canvas!");
        }
        
        // Verificar el array de botones de héroes
        if (heroButtons == null || heroButtons.Length == 0)
        {
            Debug.LogError("[HeroSelectionUI] Hero buttons array is null or empty!");
        }
        else
        {
            // Verificar cada botón
            for (int i = 0; i < heroButtons.Length; i++)
            {
                if (heroButtons[i] == null)
                {
                    Debug.LogError($"[HeroSelectionUI] Hero button at index {i} is null!");
                }
                else
                {
                    // Verificar que cada botón tiene una imagen para raycast
                    Image buttonImage = heroButtons[i].GetComponent<Image>();
                    if (buttonImage == null)
                    {
                        Debug.LogError($"[HeroSelectionUI] Hero button at index {i} doesn't have an Image component!");
                        buttonImage = heroButtons[i].gameObject.AddComponent<Image>();
                        buttonImage.color = new Color(1f, 1f, 1f, 0.01f); // Casi invisible pero permite raycast
                    }
                    
                    if (!buttonImage.raycastTarget)
                    {
                        Debug.LogWarning($"[HeroSelectionUI] Hero button at index {i} has raycastTarget disabled!");
                        buttonImage.raycastTarget = true;
                    }
                }
            }
        }
        
        // Verificar que el HeroSelectionManager existe
        heroSelectionManager = FindObjectOfType<HeroSelectionManager>();
        if (heroSelectionManager == null)
        {
            Debug.LogError("[HeroSelectionUI] HeroSelectionManager not found in the scene!");
        }
    }
    
    private void Start()
    {
        Debug.Log("[HeroSelectionUI] Start called");
        
        // Hide all connection panels first
        HideAllConnectionPanels();
        
        // Ensure our canvas is properly configured
        EnsureProperCanvasSetup();
        
        // Intentar encontrar el HeroSelectionManager de nuevo si no lo hemos encontrado
        if (heroSelectionManager == null)
        {
            heroSelectionManager = FindObjectOfType<HeroSelectionManager>();
            Debug.Log("[HeroSelectionUI] Attempting to find HeroSelectionManager again: " + 
                    (heroSelectionManager != null ? "Success" : "Failed"));
        }
        
        // Configurar los botones de héroes
        SetupHeroButtons();
        
        // Subscribe to network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
        
        // Iniciar la actualización del temporizador
        StartCoroutine(UpdateSelectionTimer());
    }
    
    private void HideAllConnectionPanels()
    {
        // Try finding any connection panels by tag
        GameObject[] connectionPanels = GameObject.FindGameObjectsWithTag("ConnectionPanel");
        foreach (var panel in connectionPanels)
        {
            Debug.Log($"[HeroSelectionUI] Found connection panel by tag: {panel.name}, hiding it");
            panel.SetActive(false);
        }
        
        // Try finding common connection panel names
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
            // Actualizar el texto del temporizador si existe
            if (timerText != null && heroSelectionManager != null)
            {
                float timeRemaining = heroSelectionManager.GetTimeRemaining();
                
                if (timeRemaining > 0)
                {
                    timerText.text = $"Time left: {Mathf.CeilToInt(timeRemaining)}s";
                    
                    // Cambiar el color cuando queda poco tiempo
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
        // Unsubscribe from network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        
        // Limpiar los listeners para evitar memory leaks
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
    
    private void SetupHeroButtons()
    {
        Debug.Log("[HeroSelectionUI] Setting up hero buttons. Count: " + (heroButtons != null ? heroButtons.Length : 0));
        
        // Set up click handlers for each hero button
        if (heroButtons == null)
        {
            Debug.LogError("[HeroSelectionUI] heroButtons array is null!");
            return;
        }
        
        for (int i = 0; i < heroButtons.Length; i++)
        {
            if (heroButtons[i] != null)
            {
                int heroIndex = i; // Need to capture the index for the lambda
                
                // Remuevo los listeners existentes
                heroButtons[i].onClick.RemoveAllListeners();
                
                // Añado un nuevo listener con un debug explícito
                heroButtons[i].onClick.AddListener(() => {
                    Debug.Log($"[HeroSelectionUI] Hero button {heroIndex} clicked!");
                    OnHeroButtonClicked(heroIndex);
                });
                
                // Verificar si el héroe existe en el manager
                if (heroSelectionManager != null)
                {
                    HeroData heroData = heroSelectionManager.GetHeroData(i);
                    if (heroData != null)
                    {
                        // Configurar la visualización del botón con datos reales
                        ConfigureHeroButton(heroButtons[i], heroData);
                    }
                    else
                    {
                        Debug.LogWarning($"[HeroSelectionUI] No hero data for index {i}");
                    }
                }
                
                Debug.Log($"[HeroSelectionUI] Hero button {i} listener set up");
            }
            else
            {
                Debug.LogError($"[HeroSelectionUI] Hero button at index {i} is null!");
            }
        }
    }
    
    private void ConfigureHeroButton(Button button, HeroData heroData)
    {
        // Configurar el botón con datos del héroe
        if (button != null && heroData != null)
        {
            // Buscar componentes en el botón
            TextMeshProUGUI buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            Image buttonImage = button.GetComponentInChildren<Image>();
            
            // Configurar texto si existe
            if (buttonText != null)
            {
                buttonText.text = heroData.heroName;
            }
            
            // Configurar imagen si existe y hay retrato
            if (buttonImage != null && heroData.portrait != null)
            {
                buttonImage.sprite = heroData.portrait;
                buttonImage.preserveAspect = true;
            }
            
            // Make sure raycast target is enabled
            if (buttonImage != null)
            {
                buttonImage.raycastTarget = true;
            }
            
            // Hacer el botón interactivo
            button.interactable = true;
        }
    }
    
    public void OnHeroButtonClicked(int heroIndex)
    {
        Debug.Log($"[HeroSelectionUI] OnHeroButtonClicked: {heroIndex}");
        
        // Update the local selection
        selectedHeroIndex = heroIndex;
        
        // Update UI to show this hero is selected
        UpdateHeroSelectionUI(heroIndex);
        
        // Enable the ready button since we've selected a hero
        if (readyButton != null)
        {
            readyButton.interactable = true;
        }
        
        // Show detailed info about this hero
        DisplayHeroInfo(heroIndex);
        
        // Tell the selection manager about our choice (but don't confirm yet)
        if (heroSelectionManager != null)
        {
            heroSelectionManager.SelectHero(heroIndex);
            
            // Actualizar el texto informativo
            if (selectionInfoText != null)
            {
                HeroData heroData = heroSelectionManager.GetHeroData(heroIndex);
                if (heroData != null)
                {
                    selectionInfoText.text = $"Selected: {heroData.heroName}\nClick 'Ready' to confirm";
                }
                else
                {
                    selectionInfoText.text = "Selected a hero. Click 'Ready' to confirm";
                }
            }
        }
        else
        {
            Debug.LogError("[HeroSelectionUI] heroSelectionManager is null! Cannot select hero.");
            // Try to find it again
            heroSelectionManager = FindObjectOfType<HeroSelectionManager>();
            if (heroSelectionManager != null)
            {
                heroSelectionManager.SelectHero(heroIndex);
            }
        }
    }
    
    private void OnReadyButtonClicked()
    {
        Debug.Log("Ready button clicked with selected hero: " + selectedHeroIndex);
        
        // Confirm our hero selection
        if (selectedHeroIndex >= 0)
        {
            if (heroSelectionManager != null)
            {
                // Notify the manager that we're ready with our hero selection
                heroSelectionManager.ConfirmHeroSelection();
                
                // Disable the ready button and hero selection buttons
                readyButton.interactable = false;
                DisableHeroButtons();
                
                // Update info text
                if (selectionInfoText != null)
                {
                    HeroData heroData = heroSelectionManager.GetHeroData(selectedHeroIndex);
                    if (heroData != null)
                    {
                        selectionInfoText.text = $"Ready with {heroData.heroName}!\nWaiting for other players...";
                    }
                    else
                    {
                        selectionInfoText.text = "Ready! Waiting for other players...";
                    }
                }
            }
            else
            {
                Debug.LogError("[HeroSelectionUI] heroSelectionManager is null! Cannot confirm selection.");
                // Try to find it again
                heroSelectionManager = FindObjectOfType<HeroSelectionManager>();
                if (heroSelectionManager != null)
                {
                    heroSelectionManager.ConfirmHeroSelection();
                }
            }
        }
    }
    
    private void UpdateHeroSelectionUI(int heroIndex)
    {
        // Highlight the selected hero button and unhighlight others
        for (int i = 0; i < heroButtons.Length; i++)
        {
            if (heroButtons[i] != null)
            {
                // Cambiar el color del botón
                Image buttonImage = heroButtons[i].GetComponent<Image>();
                if (buttonImage != null)
                {
                    buttonImage.color = (i == heroIndex) ? selectedButtonColor : normalButtonColor;
                }
                
                // Si hay un highlight child, activarlo solo para el seleccionado
                Transform highlight = heroButtons[i].transform.Find("SelectionHighlight");
                if (highlight != null)
                {
                    highlight.gameObject.SetActive(i == heroIndex);
                }
            }
        }
        
        // Mover el highlight visual si existe
        if (selectionHighlight != null && heroIndex >= 0 && heroIndex < heroButtons.Length)
        {
            selectionHighlight.SetActive(true);
            selectionHighlight.transform.position = heroButtons[heroIndex].transform.position;
        }
    }
    
    private void DisableHeroButtons()
    {
        // Disable all hero buttons once a selection is confirmed
        for (int i = 0; i < heroButtons.Length; i++)
        {
            if (heroButtons[i] != null)
            {
                heroButtons[i].interactable = false;
                
                // Cambiar el color para indicar que está deshabilitado
                Image buttonImage = heroButtons[i].GetComponent<Image>();
                if (buttonImage != null && i != selectedHeroIndex)
                {
                    buttonImage.color = disabledButtonColor;
                }
            }
        }
    }
    
    private void DisplayHeroInfo(int heroIndex)
    {
        Debug.Log($"[HeroSelectionUI] DisplayHeroInfo for hero index: {heroIndex}");
        
        if (heroInfoPanel != null && heroSelectionManager != null)
        {
            // Get hero data
            HeroData heroData = heroSelectionManager.GetHeroData(heroIndex);
            
            if (heroData != null)
            {
                // Show the info panel
                heroInfoPanel.SetActive(true);
                
                // Set hero details
                if (heroNameText != null) heroNameText.text = heroData.heroName;
                if (heroDescriptionText != null) heroDescriptionText.text = heroData.description;
                
                if (heroPortrait != null)
                {
                    heroPortrait.sprite = heroData.portrait;
                    heroPortrait.preserveAspect = true;
                    Debug.Log($"[HeroSelectionUI] Set portrait: {(heroData.portrait != null ? heroData.portrait.name : "null")}");
                }
                
                // Update ability info
                for (int i = 0; i < abilityIcons.Length && i < heroData.abilities.Length; i++)
                {
                    if (abilityIcons[i] != null && heroData.abilities[i] != null)
                    {
                        abilityIcons[i].sprite = heroData.abilities[i].icon;
                        abilityIcons[i].preserveAspect = true;
                    }
                    
                    if (abilityNames != null && i < abilityNames.Length && abilityNames[i] != null && heroData.abilities[i] != null)
                    {
                        abilityNames[i].text = heroData.abilities[i].abilityName;
                    }
                    
                    if (abilityDescriptions != null && i < abilityDescriptions.Length && abilityDescriptions[i] != null && heroData.abilities[i] != null)
                    {
                        abilityDescriptions[i].text = heroData.abilities[i].description;
                    }
                }
            }
            else
            {
                Debug.LogError($"[HeroSelectionUI] HeroData is null for index: {heroIndex}");
                
                // Mostrar un mensaje de error en el panel
                if (heroNameText != null) heroNameText.text = "Hero Not Found";
                if (heroDescriptionText != null) heroDescriptionText.text = "Could not load hero data for index " + heroIndex;
                
                // Ocultar iconos de habilidades
                for (int i = 0; i < abilityIcons.Length; i++)
                {
                    if (abilityIcons[i] != null)
                        abilityIcons[i].gameObject.SetActive(false);
                }
            }
        }
        else
        {
            if (heroInfoPanel == null) Debug.LogError("[HeroSelectionUI] heroInfoPanel is null!");
            if (heroSelectionManager == null) Debug.LogError("[HeroSelectionUI] heroSelectionManager is null!");
        }
    }
    
    // Called by HeroSelectionManager to show this UI when needed
    public void Show()
    {
        Debug.Log("[HeroSelectionUI] Show() called");
        
        // Hide all connection panels first
        HideAllConnectionPanels();
        
        // Ensure proper canvas setup for interactivity
        EnsureProperCanvasSetup();
        
        // Verificar que tenemos todos los componentes necesarios
        ValidateComponents();
        
        if (heroSelectionPanel != null)
        {
            heroSelectionPanel.SetActive(true);
            
            // Comprobar si los botones están configurados
            if (!hasInitialized)
            {
                SetupHeroButtons();
                hasInitialized = true;
                
                // Debugging de verificación de clics en botones
                StartCoroutine(DebugButtonsInteractivity());
            }
        }
        else
        {
            Debug.LogError("[HeroSelectionUI] heroSelectionPanel is null!");
            return;
        }
        
        // Try to find HeroSelectionManager again if it's null
        if (heroSelectionManager == null)
        {
            heroSelectionManager = FindObjectOfType<HeroSelectionManager>();
            Debug.Log("[HeroSelectionUI] Attempting to find HeroSelectionManager again: " + 
                     (heroSelectionManager != null ? "Success" : "Failed"));
        }
        
        // Reset state
        selectedHeroIndex = -1;
        if (readyButton != null)
        {
            readyButton.interactable = false;
        }
        
        if (selectionInfoText != null)
        {
            selectionInfoText.text = "Select your hero";
        }
        
        // Re-enable hero buttons
        for (int i = 0; i < heroButtons.Length; i++)
        {
            if (heroButtons[i] != null)
            {
                heroButtons[i].interactable = true;
                
                // Reset button colors
                Image buttonImage = heroButtons[i].GetComponent<Image>();
                if (buttonImage != null)
                {
                    buttonImage.color = normalButtonColor;
                    
                    // Ensure raycast target is enabled
                    buttonImage.raycastTarget = true;
                }
            }
            else
            {
                Debug.LogError($"[HeroSelectionUI] Hero button at index {i} is null!");
            }
        }
        
        // Hide highlight
        if (selectionHighlight != null)
        {
            selectionHighlight.SetActive(false);
        }
        
        // Clear selected state
        UpdateHeroSelectionUI(-1);
        
        Debug.Log("[HeroSelectionUI] UI initialized and shown");
        
        // Schedule a check after a short delay in case canvas settings need to settle
        StartCoroutine(DelayedCanvasCheck());
    }
    
    private IEnumerator DelayedCanvasCheck()
    {
        yield return new WaitForSeconds(0.2f);
        
        // Re-check canvas and connection panels
        EnsureProperCanvasSetup();
        HideAllConnectionPanels();
        
        // Check if buttons are interactive - add debug sphere to test raycasting
        GameObject testSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        testSphere.transform.position = new Vector3(0, 0, 100);
        testSphere.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
        Destroy(testSphere, 5f);
    }
    
    private IEnumerator DebugButtonsInteractivity()
    {
        yield return new WaitForSeconds(1f);
        
        // Verificar que los botones son interactivos
        for (int i = 0; i < heroButtons.Length; i++)
        {
            if (heroButtons[i] != null)
            {
                bool isInteractable = heroButtons[i].interactable;
                Debug.Log($"[HeroSelectionUI] Hero button {i} interactable: {isInteractable}");
                
                // Verificar componentes del botón
                Image buttonImage = heroButtons[i].GetComponent<Image>();
                if (buttonImage != null)
                {
                    Debug.Log($"[HeroSelectionUI] Hero button {i} has Image component with raycastTarget: {buttonImage.raycastTarget}");
                    
                    // Force raycast target to be enabled
                    buttonImage.raycastTarget = true;
                }
            }
        }
        
        // Verificar canvas y raycaster
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            UnityEngine.UI.GraphicRaycaster raycaster = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (raycaster != null)
            {
                Debug.Log($"[HeroSelectionUI] Canvas has GraphicRaycaster: {raycaster.enabled}");
                
                // Force raycaster to be enabled
                raycaster.enabled = true;
            }
            
            Debug.Log($"[HeroSelectionUI] Canvas render mode: {canvas.renderMode}");
        }
        
        // Force a redraw of the canvas
        Canvas.ForceUpdateCanvases();
    }
    
    public void Hide()
    {
        if (heroSelectionPanel != null)
        {
            heroSelectionPanel.SetActive(false);
        }
        
        // También ocultar el panel de info
        if (heroInfoPanel != null)
        {
            heroInfoPanel.SetActive(false);
        }
    }
    
    // Network callbacks
    private void OnClientConnected(ulong clientId)
    {
        // When a client connects, create a selection indicator for them
        Debug.Log($"[HeroSelectionUI] Client connected: {clientId}");
        CreatePlayerSelectionIndicator(clientId);
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        // When a client disconnects, remove their selection indicator
        RemovePlayerSelectionIndicator(clientId);
    }
    
    private void CreatePlayerSelectionIndicator(ulong clientId)
    {
        if (playerSelectionPrefab != null && playerSelectionsContainer != null)
        {
            // Verificar si ya existe un indicador para este cliente
            if (playerSelectionIndicators.ContainsKey(clientId))
            {
                Debug.Log($"[HeroSelectionUI] Player indicator already exists for client {clientId}");
                return;
            }
            
            GameObject indicator = Instantiate(playerSelectionPrefab, playerSelectionsContainer);
            
            // Set player ID or name
            TextMeshProUGUI playerText = indicator.GetComponentInChildren<TextMeshProUGUI>();
            if (playerText != null)
            {
                bool isLocalPlayer = clientId == NetworkManager.Singleton.LocalClientId;
                playerText.text = isLocalPlayer ? $"Player {clientId} (You)" : $"Player {clientId}";
                
                // Destacar al jugador local
                if (isLocalPlayer)
                {
                    playerText.color = Color.green;
                }
            }
            
            // Store reference
            playerSelectionIndicators[clientId] = indicator;
        }
    }
    
    private void RemovePlayerSelectionIndicator(ulong clientId)
    {
        if (playerSelectionIndicators.TryGetValue(clientId, out GameObject indicator))
        {
            Destroy(indicator);
            playerSelectionIndicators.Remove(clientId);
        }
    }
    
    // Called by HeroSelectionManager when a player updates their selection
    public void UpdatePlayerSelection(ulong clientId, int heroIndex, bool isReady)
    {
        if (playerSelectionIndicators.TryGetValue(clientId, out GameObject indicator))
        {
            // Update the indicator to show selected hero and ready status
            Image heroIcon = indicator.transform.Find("HeroIcon")?.GetComponent<Image>();
            if (heroIcon != null && heroIndex >= 0 && heroSelectionManager != null)
            {
                HeroData heroData = heroSelectionManager.GetHeroData(heroIndex);
                if (heroData != null)
                {
                    heroIcon.sprite = heroData.portrait;
                    heroIcon.color = Color.white; // Make visible
                    heroIcon.preserveAspect = true;
                }
            }
            
            // Update ready status indicator
            GameObject readyIndicator = indicator.transform.Find("ReadyIndicator")?.gameObject;
            if (readyIndicator != null)
            {
                readyIndicator.SetActive(isReady);
            }
            
            // Update player name to include selected hero
            TextMeshProUGUI playerText = indicator.GetComponentInChildren<TextMeshProUGUI>();
            if (playerText != null && heroIndex >= 0 && heroSelectionManager != null)
            {
                HeroData heroData = heroSelectionManager.GetHeroData(heroIndex);
                if (heroData != null)
                {
                    bool isLocalPlayer = clientId == NetworkManager.Singleton.LocalClientId;
                    string playerPrefix = isLocalPlayer ? "You" : $"Player {clientId}";
                    playerText.text = $"{playerPrefix} - {heroData.heroName}";
                    
                    // Colorear según estado
                    if (isReady)
                    {
                        playerText.color = isLocalPlayer ? new Color(0, 0.8f, 0) : new Color(0, 0.6f, 0);
                    }
                    else
                    {
                        playerText.color = isLocalPlayer ? Color.green : Color.white;
                    }
                }
            }
        }
        else
        {
            // Si no existe el indicador, crearlo
            Debug.LogWarning($"[HeroSelectionUI] No indicator found for client {clientId}, creating one...");
            CreatePlayerSelectionIndicator(clientId);
            
            // Intentar actualizar de nuevo
            if (playerSelectionIndicators.ContainsKey(clientId))
            {
                UpdatePlayerSelection(clientId, heroIndex, isReady);
            }
        }
    }
    public void UpdateHeroButtonsDisplay(HeroData[] availableHeroes)
{
    if (heroButtons == null || heroButtons.Length == 0)
    {
        Debug.LogError("[HeroSelectionUI] Hero buttons array is not configured properly");
        return;
    }
    
    int heroCount = availableHeroes?.Length ?? 0;
    Debug.Log($"[HeroSelectionUI] Updating hero buttons with {heroCount} available heroes");
    
    // For each button, try to assign a hero if available
    for (int i = 0; i < heroButtons.Length; i++)
    {
        if (heroButtons[i] == null) continue;
        
        if (i < heroCount && availableHeroes[i] != null)
        {
            // Configure button with hero data
            ConfigureHeroButton(heroButtons[i], availableHeroes[i]);
            heroButtons[i].gameObject.SetActive(true);
        }
        else
        {
            // Hide buttons without a corresponding hero
            heroButtons[i].gameObject.SetActive(false);
        }
    }
}
}