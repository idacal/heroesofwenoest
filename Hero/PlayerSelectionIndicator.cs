using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Script para el prefab de indicadores de selección de jugador
/// que mostrará quién está preparado y qué héroe ha seleccionado
/// </summary>
public class PlayerSelectionIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private Image heroIcon;
    [SerializeField] private GameObject readyIndicator;
    [SerializeField] private Image backgroundPanel;
    
    [Header("Colors")]
    [SerializeField] private Color defaultColor = new Color(0.2f, 0.2f, 0.2f, 0.7f);
    [SerializeField] private Color readyColor = new Color(0.2f, 0.6f, 0.2f, 0.7f);
    [SerializeField] private Color localPlayerColor = new Color(0.2f, 0.2f, 0.5f, 0.7f);
    [SerializeField] private Color localPlayerReadyColor = new Color(0.2f, 0.5f, 0.5f, 0.7f);
    
    private bool isLocalPlayer = false;
    private bool isReady = false;
    private ulong clientId;
    
    /// <summary>
    /// Inicializa el indicador con los detalles del jugador
    /// </summary>
    public void Initialize(ulong playerId, bool isLocal)
    {
        clientId = playerId;
        isLocalPlayer = isLocal;
        
        if (playerNameText != null)
        {
            playerNameText.text = isLocalPlayer ? $"Player {playerId} (You)" : $"Player {playerId}";
            
            // Destacar al jugador local
            if (isLocalPlayer)
            {
                playerNameText.color = Color.green;
            }
        }
        
        // Inicialmente no está listo
        SetReadyState(false);
        
        // Inicialmente sin héroe seleccionado
        ClearHeroSelection();
    }
    
    /// <summary>
    /// Establece el estado de "listo" del jugador
    /// </summary>
    public void SetReadyState(bool ready)
    {
        isReady = ready;
        
        // Mostrar/ocultar indicador de listo
        if (readyIndicator != null)
        {
            readyIndicator.SetActive(ready);
        }
        
        // Cambiar color según estado
        if (backgroundPanel != null)
        {
            backgroundPanel.color = GetAppropriateColor();
        }
        
        // Actualizar texto si es necesario
        UpdatePlayerNameText();
    }
    
    /// <summary>
    /// Establece el héroe seleccionado por el jugador
    /// </summary>
    public void SetSelectedHero(Sprite heroSprite, string heroName)
    {
        if (heroIcon != null && heroSprite != null)
        {
            heroIcon.gameObject.SetActive(true);
            heroIcon.sprite = heroSprite;
            heroIcon.color = Color.white;
            heroIcon.preserveAspect = true;
        }
        
        // Actualizar texto con nombre del héroe
        UpdatePlayerNameText(heroName);
    }
    
    /// <summary>
    /// Limpia la selección de héroe
    /// </summary>
    public void ClearHeroSelection()
    {
        if (heroIcon != null)
        {
            heroIcon.gameObject.SetActive(false);
            heroIcon.sprite = null;
        }
        
        // Actualizar texto sin héroe seleccionado
        UpdatePlayerNameText();
    }
    
    /// <summary>
    /// Actualiza el texto del nombre del jugador con información adicional
    /// </summary>
    private void UpdatePlayerNameText(string heroName = null)
    {
        if (playerNameText != null)
        {
            string playerPrefix = isLocalPlayer ? "You" : $"Player {clientId}";
            string readyStatus = isReady ? " (Ready)" : "";
            string heroInfo = !string.IsNullOrEmpty(heroName) ? $" - {heroName}" : "";
            
            playerNameText.text = $"{playerPrefix}{heroInfo}{readyStatus}";
            
            // Color según estado
            if (isReady)
            {
                playerNameText.color = isLocalPlayer ? new Color(0, 0.8f, 0) : new Color(0, 0.6f, 0);
            }
            else
            {
                playerNameText.color = isLocalPlayer ? Color.green : Color.white;
            }
        }
    }
    
    /// <summary>
    /// Devuelve el color apropiado según el estado
    /// </summary>
    private Color GetAppropriateColor()
    {
        if (isLocalPlayer)
        {
            return isReady ? localPlayerReadyColor : localPlayerColor;
        }
        else
        {
            return isReady ? readyColor : defaultColor;
        }
    }
    
    /// <summary>
    /// Muestra un efecto visual cuando un jugador cambia de selección
    /// </summary>
    public void PlaySelectionChangedEffect()
    {
        // Implementar un efecto visual sutil, como un parpadeo
        StartCoroutine(FlashEffect());
    }
    
    private System.Collections.IEnumerator FlashEffect()
    {
        if (backgroundPanel != null)
        {
            Color originalColor = backgroundPanel.color;
            Color flashColor = new Color(1f, 1f, 0.5f, 0.7f); // Amarillo suave
            
            float duration = 0.5f;
            float elapsed = 0f;
            
            while (elapsed < duration)
            {
                float t = elapsed / duration;
                // Primero cambia al color de flash y luego vuelve al original
                backgroundPanel.color = Color.Lerp(
                    Color.Lerp(originalColor, flashColor, Mathf.Sin(t * Mathf.PI)), 
                    originalColor, 
                    t
                );
                
                elapsed += Time.deltaTime;
                yield return null;
            }
            
            // Asegurar que vuelve al color original
            backgroundPanel.color = originalColor;
        }
    }
}