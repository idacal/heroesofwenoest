using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using System.Collections.Generic;

public class HeroSelectionUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject heroSelectionPanel;
    [SerializeField] private Button[] heroButtons;
    [SerializeField] private Button readyButton;
    [SerializeField] private TextMeshProUGUI selectionInfoText;
    
    [Header("Hero Info Panel")]
    [SerializeField] private GameObject heroInfoPanel;
    [SerializeField] private Image heroPortrait;
    [SerializeField] private TextMeshProUGUI heroNameText;
    [SerializeField] private TextMeshProUGUI heroDescriptionText;
    [SerializeField] private Image[] abilityIcons; // Q, W, E, R ability icons
    [SerializeField] private TextMeshProUGUI[] abilityNames; // Names for each ability
    [SerializeField] private TextMeshProUGUI[] abilityDescriptions; // Descriptions for each ability
    
    [Header("Player Selection Indicators")]
    [SerializeField] private Transform playerSelectionsContainer;
    [SerializeField] private GameObject playerSelectionPrefab;
    
    // Reference to the hero selection manager
    private HeroSelectionManager heroSelectionManager;
    
    // Track the currently selected hero index (local)
    private int selectedHeroIndex = -1;
    
    // Track player selection indicators by client ID
    private Dictionary<ulong, GameObject> playerSelectionIndicators = new Dictionary<ulong, GameObject>();
    
    private void Awake()
    {
        Debug.Log("[HeroSelectionUI] Awake called");
        
        heroSelectionManager = FindObjectOfType<HeroSelectionManager>();
        
        if (heroSelectionManager == null)
        {
            Debug.LogError("[HeroSelectionUI] HeroSelectionManager not found in the scene!");
        }
        
        // Initially hide the hero selection panel
        if (heroSelectionPanel != null)
        {
            heroSelectionPanel.SetActive(false);
        }
        else
        {
            Debug.LogError("[HeroSelectionUI] heroSelectionPanel is null!");
        }
        
        // Set up hero button listeners
        SetupHeroButtons();
        
        // Set up ready button
        if (readyButton != null)
        {
            readyButton.onClick.AddListener(OnReadyButtonClicked);
            readyButton.interactable = false; // Disable until a hero is selected
            Debug.Log("[HeroSelectionUI] Ready button listener set up");
        }
        else
        {
            Debug.LogError("[HeroSelectionUI] readyButton is null!");
        }
    }
    
    private void Start()
    {
        // Subscribe to network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
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
                
                // Remove any existing listeners to prevent duplicates
                heroButtons[i].onClick.RemoveAllListeners();
                
                heroButtons[i].onClick.AddListener(() => {
                    Debug.Log($"[HeroSelectionUI] Hero button {heroIndex} clicked!");
                    OnHeroButtonClicked(heroIndex);
                });
                
                Debug.Log($"[HeroSelectionUI] Hero button {i} listener set up");
            }
            else
            {
                Debug.LogError($"[HeroSelectionUI] Hero button at index {i} is null!");
            }
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
                    selectionInfoText.text = "Waiting for other players...";
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
                heroButtons[i].GetComponent<Image>().color = (i == heroIndex) 
                    ? new Color(0.8f, 0.8f, 1f) // Selected color
                    : Color.white; // Normal color
            }
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
                    Debug.Log($"[HeroSelectionUI] Set portrait: {(heroData.portrait != null ? heroData.portrait.name : "null")}");
                }
                
                // Update ability info
                for (int i = 0; i < abilityIcons.Length && i < heroData.abilities.Length; i++)
                {
                    if (abilityIcons[i] != null && heroData.abilities[i] != null)
                    {
                        abilityIcons[i].sprite = heroData.abilities[i].icon;
                    }
                    
                    if (abilityNames[i] != null && heroData.abilities[i] != null)
                    {
                        abilityNames[i].text = heroData.abilities[i].abilityName;
                    }
                    
                    if (abilityDescriptions[i] != null && heroData.abilities[i] != null)
                    {
                        abilityDescriptions[i].text = heroData.abilities[i].description;
                    }
                }
            }
            else
            {
                Debug.LogError($"[HeroSelectionUI] HeroData is null for index: {heroIndex}");
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
        
        if (heroSelectionPanel != null)
        {
            heroSelectionPanel.SetActive(true);
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
            }
            else
            {
                Debug.LogError($"[HeroSelectionUI] Hero button at index {i} is null!");
            }
        }
        
        // Clear selected state
        UpdateHeroSelectionUI(-1);
        
        Debug.Log("[HeroSelectionUI] UI initialized and shown");
    }
    
    public void Hide()
    {
        if (heroSelectionPanel != null)
        {
            heroSelectionPanel.SetActive(false);
        }
    }
    
    // Network callbacks
    private void OnClientConnected(ulong clientId)
    {
        // When a client connects, create a selection indicator for them
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
            GameObject indicator = Instantiate(playerSelectionPrefab, playerSelectionsContainer);
            
            // Set player ID or name
            TextMeshProUGUI playerText = indicator.GetComponentInChildren<TextMeshProUGUI>();
            if (playerText != null)
            {
                playerText.text = $"Player {clientId}";
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
            if (heroIcon != null && heroIndex >= 0)
            {
                HeroData heroData = heroSelectionManager.GetHeroData(heroIndex);
                if (heroData != null)
                {
                    heroIcon.sprite = heroData.portrait;
                    heroIcon.color = Color.white; // Make visible
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
            if (playerText != null && heroIndex >= 0)
            {
                HeroData heroData = heroSelectionManager.GetHeroData(heroIndex);
                if (heroData != null)
                {
                    playerText.text = $"Player {clientId} - {heroData.heroName}";
                }
            }
        }
    }
}