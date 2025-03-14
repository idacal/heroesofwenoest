using Unity.Netcode;
using UnityEngine;
using System.Collections;

namespace PlayerAbilities
{
public class DashAbility : BaseAbility
{
    [Header("Configuración de Dash")]
    [SerializeField] private float dashSpeed = 20f;
    [SerializeField] private float dashInitialDuration = 0.2f; // Duración inicial si no se mantiene presionado
    [SerializeField] private float dashMaxDuration = 2.0f;     // Duración máxima si se mantiene presionado
    [SerializeField] private float dashInitialManaCost = 20f;  // Costo inicial de maná
    [SerializeField] private float dashManaCostPerSecond = 30f; // Maná consumido por segundo al mantener
    
    [Header("Dash Avanzado")]
    [SerializeField] private AnimationCurve dashCurve = new AnimationCurve(
        new Keyframe(0, 0, 0, 2),     // Inicio suave
        new Keyframe(0.5f, 1, 0, 0),  // Pico de velocidad
        new Keyframe(1, 0, -2, 0)     // Final suave
    );
    
    public enum DashType { Instant, Linear, EaseIn, EaseOut, EaseInOut }
    [SerializeField] private DashType dashStyle = DashType.EaseInOut;

    // Estado del dash
    private bool isDashing = false;
    private float dashTimeLeft = 0f;
    private Vector3 dashDirection;
    private float dashProgress = 0f;
    private bool isDashKeyHeld = false;
    
    // Referencias a componentes externos
    private PlayerAbilityController abilityController;
    // NUEVO: Añadir referencia al nuevo sistema
    private PlayerAbilityManager abilityManager;
    
    public override void Initialize(NetworkBehaviour owner)
    {
        base.Initialize(owner);
        abilityName = "Dash";
        activationKey = KeyCode.Q;
        manaCost = dashInitialManaCost;
        cooldown = 3f;
        
        // MODIFICADO: Intentar obtener ambos componentes para compatibilidad
        abilityManager = owner.GetComponent<PlayerAbilityManager>();
        abilityController = owner.GetComponent<PlayerAbilityController>();
        
        if (abilityManager == null && abilityController == null)
        {
            Debug.LogWarning("[DashAbility] Neither PlayerAbilityManager nor PlayerAbilityController found!");
        }
        else
        {
            Debug.Log($"[DashAbility] Initialized successfully. Using abilityManager: {abilityManager != null}, abilityController: {abilityController != null}");
        }
    }
    
    public override bool CanActivate()
    {
        // MODIFICADO: Verificar si hay movimiento activo usando ambos sistemas
        bool isMoving = false;
        
        if (abilityManager != null)
        {
            isMoving = abilityManager.IsMoving();
        }
        else if (abilityController != null)
        {
            isMoving = abilityController.IsMoving();
        }
        
        if (!isMoving)
        {
            if (networkOwner != null && networkOwner.IsOwner)
            {
                Debug.Log("No se puede usar dash: No hay dirección de movimiento");
            }
            return false;
        }
        
        return isReady && playerStats != null && playerStats.CurrentMana >= dashInitialManaCost;
    }
    
    public override void Activate()
    {
        if (isDashing) return;
        
        // MODIFICADO: Obtener la posición objetivo desde el sistema apropiado
        Vector3 targetPosition = Vector3.zero;
        
        if (abilityManager != null)
        {
            targetPosition = abilityManager.GetTargetPosition();
        }
        else if (abilityController != null)
        {
            targetPosition = abilityController.GetTargetPosition();
        }
        else
        {
            // Fallback: usar la posición hacia adelante del jugador
            targetPosition = networkOwner.transform.position + networkOwner.transform.forward * 5f;
            Debug.LogWarning("[DashAbility] No ability controller/manager found. Using fallback direction.");
        }
        
        // Usar la dirección hacia el punto de clic
        dashDirection = (targetPosition - networkOwner.transform.position).normalized;
        dashDirection.y = 0; // Mantener el dash en el plano horizontal
        
        // Iniciar el dash
        isDashing = true;
        dashTimeLeft = dashMaxDuration;
        dashProgress = 0f;
        isDashKeyHeld = true;
        
        // Aplicar un impulso inicial para que el dash se sienta más impactante
        if (rb != null)
        {
            rb.velocity = dashDirection * dashSpeed * 0.5f;
        }
        
        if (networkOwner.IsOwner)
        {
            Debug.Log("¡Dash iniciado en dirección: " + dashDirection);
        }
    }
    
    public override void UpdateAbility()
    {
        // Actualizar lógica del dash si está activo
        if (isDashing)
        {
            UpdateDash();
        }
        
        // Detectar cuando se suelta la tecla de activación
        if (networkOwner.IsOwner && Input.GetKeyUp(activationKey) && isDashKeyHeld)
        {
            isDashKeyHeld = false;
            if (isDashing)
            {
                EndDashEarlyServerRpc();
            }
        }
    }
    
    private void UpdateDash()
    {
        if (dashTimeLeft > 0 && isDashing)
        {
            // Consumir maná continuamente mientras se hace dash
            float manaCost = dashManaCostPerSecond * Time.deltaTime;
            
            // Si no hay suficiente maná, terminar el dash
            if (playerStats != null && playerStats.CurrentMana < manaCost)
            {
                EndDashEarly();
                return;
            }
            
            // Consumir maná (enviamos al servidor para que lo maneje)
            if (networkOwner.IsOwner)
            {
                ConsumeDashManaServerRpc(manaCost);
            }
            
            // Calcular el progreso del dash para la curva de velocidad
            dashProgress = Mathf.Min(0.5f, 1 - (dashTimeLeft / dashMaxDuration));
            
            float speedMultiplier;
            
            switch (dashStyle)
            {
                case DashType.Instant:
                    speedMultiplier = 1.0f;
                    break;
                case DashType.Linear:
                    speedMultiplier = 1.0f;
                    break;
                case DashType.EaseIn:
                    speedMultiplier = dashProgress < 0.3f ? Mathf.Lerp(0.5f, 1.0f, dashProgress / 0.3f) : 1.0f;
                    break;
                case DashType.EaseOut:
                    speedMultiplier = 1.0f;
                    break;
                case DashType.EaseInOut:
                    // Usamos la curva solo para el inicio, luego mantenemos constante
                    speedMultiplier = dashProgress < 0.3f ? dashCurve.Evaluate(dashProgress) : 1.0f;
                    break;
                default:
                    speedMultiplier = 1.0f;
                    break;
            }
            
            // Actualizar dirección del dash mientras se mantiene presionado
            Vector3 targetPosition = Vector3.zero;
            bool isMoving = false;
            
            // MODIFICADO: Obtener nueva dirección basada en el sistema disponible
            if (abilityManager != null)
            {
                targetPosition = abilityManager.GetTargetPosition();
                isMoving = abilityManager.IsMoving();
            }
            else if (abilityController != null)
            {
                targetPosition = abilityController.GetTargetPosition();
                isMoving = abilityController.IsMoving();
            }
            
            if (isMoving && Vector3.Distance(networkOwner.transform.position, targetPosition) > 1.0f)
            {
                // Reorientar gradualmente el dash hacia el punto de destino
                Vector3 newDirection = (targetPosition - networkOwner.transform.position).normalized;
                newDirection.y = 0;
                
                // Interpolar suavemente para que no cambie bruscamente de dirección
                dashDirection = Vector3.Lerp(dashDirection, newDirection, Time.deltaTime * 3f);
            }
            
            // Aplicar la velocidad según el sistema de movimiento
            if (rb != null)
            {
                rb.velocity = dashDirection * dashSpeed * speedMultiplier;
            }
            else if (characterController != null && characterController.enabled)
            {
                characterController.Move(dashDirection * dashSpeed * speedMultiplier * Time.deltaTime);
            }
            else
            {
                // Fallback en caso de que no haya controlador
                networkOwner.transform.position += dashDirection * dashSpeed * speedMultiplier * Time.deltaTime;
            }
            
            dashTimeLeft -= Time.deltaTime;
        }
        else if (isDashing)
        {
            // El tiempo se ha agotado, terminamos el dash
            EndDashComplete();
        }
    }
    
    private void EndDashEarly()
    {
        // Finalizar el dash suavemente
        isDashing = false;
        
        // Aplicar una desaceleración gradual
        StartCoroutine(SmoothDashEnd());
        
        // Activar cooldown
        if (networkOwner != null)
        {
            networkOwner.StartCoroutine(StartCooldown());
        }
    }
    
    private void EndDashComplete()
    {
        // Finalizar el dash
        isDashing = false;
        
        // Aplicar una desaceleración gradual
        StartCoroutine(SmoothDashEnd());
        
        // Activar cooldown
        if (networkOwner != null)
        {
            networkOwner.StartCoroutine(StartCooldown());
        }
    }
    
    private IEnumerator SmoothDashEnd()
    {
        float endDuration = 0.3f;
        float timer = 0f;
        Vector3 initialVelocity = rb != null ? rb.velocity : dashDirection * dashSpeed;
        
        while (timer < endDuration)
        {
            timer += Time.deltaTime;
            float t = timer / endDuration;
            
            // Reducir gradualmente la velocidad hasta velocidad normal o cero
            if (rb != null)
            {
                rb.velocity = Vector3.Lerp(initialVelocity, Vector3.zero, t);
            }
            
            yield return null;
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void EndDashEarlyServerRpc()
    {
        EndDashEarlyClientRpc();
    }

    [ClientRpc]
    private void EndDashEarlyClientRpc()
    {
        if (isDashing)
        {
            EndDashEarly();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ConsumeDashManaServerRpc(float amount)
    {
        // Consumir maná durante el dash continuo
        if (playerStats != null)
        {
            playerStats.UseMana(amount);
        }
    }
    
    // Propiedades públicas para que otros sistemas puedan consultar el estado
    public bool IsDashing => isDashing;
}
}