using Unity.Netcode;
using UnityEngine;
using System.Collections;

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
    // No inicializar aquí para evitar problemas de serialización
    [SerializeField] private Ability[] abilities = new Ability[4];
    
    [Header("Configuración de Dash")]
    [SerializeField] private float dashSpeed = 20f;
    [SerializeField] private float dashInitialDuration = 0.2f; // Duración inicial si no se mantiene presionado
    [SerializeField] private float dashMaxDuration = 2.0f;     // Duración máxima si se mantiene presionado
    [SerializeField] private float dashInitialManaCost = 20f;  // Costo inicial de maná
    [SerializeField] private float dashManaCostPerSecond = 30f; // Maná consumido por segundo al mantener
    [SerializeField] private GameObject clickIndicatorPrefab; // Indicador visual para el clic
    
    [Header("Dash Avanzado")]
    [SerializeField] private AnimationCurve dashCurve = new AnimationCurve(
        new Keyframe(0, 0, 0, 2),     // Inicio suave
        new Keyframe(0.5f, 1, 0, 0),  // Pico de velocidad
        new Keyframe(1, 0, -2, 0)     // Final suave
    );
    
    public enum DashType { Instant, Linear, EaseIn, EaseOut, EaseInOut }
    [SerializeField] private DashType dashStyle = DashType.EaseInOut;

    [Header("Configuración de Terremoto")]
    [SerializeField] private float jumpHeight = 5f;           // Altura máxima del salto
    [SerializeField] private float riseTime = 0.8f;           // Tiempo de subida del salto (más lento)
    [SerializeField] private float fallTime = 0.4f;           // Tiempo de caída (más rápido)
    [SerializeField] private float impactPauseTime = 0.5f;    // Tiempo de pausa después del impacto
    [SerializeField] private float earthquakeRadius = 5f;     // Radio del efecto de terremoto
    [SerializeField] private float earthquakeForce = 10f;     // Fuerza de empuje
    [SerializeField] private GameObject earthquakeEffectPrefab; // Efecto visual del terremoto
    [SerializeField] private LayerMask affectedLayers;        // Capas afectadas por el terremoto

    // Referencias
    private PlayerStats playerStats;
    private CharacterController characterController;
    private Rigidbody rb;
    private PlayerNetwork playerNetwork; // Referencia al script de movimiento del jugador

    // Estado del dash
    private bool isDashing = false;
    private float dashTimeLeft = 0f;
    private Vector3 dashDirection;
    private float dashProgress = 0f;
    private bool isDashKeyHeld = false;
    
    // Estado del terremoto
    private bool isJumping = false;
    private bool isFalling = false;
    private bool isInImpactPause = false;
    private Vector3 jumpStartPosition;
    private Vector3 jumpHighestPosition;
    private float jumpTime = 0f;
    
    // Variables para movimiento con clic
    private Vector3 targetPosition;
    private bool isMoving = false;

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
            abilities[2] = new Ability { name = "Ability 3", activationKey = KeyCode.E, manaCost = 70f, cooldown = 8f };
        
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
        playerNetwork = GetComponent<PlayerNetwork>();
        
        // Intentar obtener CharacterController o Rigidbody
        characterController = GetComponent<CharacterController>();
        rb = GetComponent<Rigidbody>();
        
        if (rb == null && characterController == null)
        {
            Debug.LogWarning("No se encontró CharacterController ni Rigidbody. El dash podría no funcionar correctamente.");
        }
        
        // Inicializar todas las habilidades como listas
        for (int i = 0; i < abilities.Length; i++)
        {
            abilities[i].isReady = true;
        }
        
        // Asegurarse de que no estamos en estado de dash al inicio
        isDashing = false;
        isJumping = false;
        isFalling = false;
        isInImpactPause = false;
    }

    private void Update()
    {
        // Solo procesamos entrada si somos el dueño del objeto
        if (!IsOwner) return;

        // Procesar clic para movimiento solo si no estamos en pausa de impacto
        if (!isInImpactPause)
        {
            HandleMouseInput();
        }
        else
        {
            // Si estamos en pausa de impacto, ignorar cualquier entrada de movimiento
            if (Input.GetMouseButtonDown(1))
            {
                // Consumir el evento pero no hacer nada
                Debug.Log("Movimiento bloqueado durante la pausa de impacto");
            }
        }

        // Comprobar todas las habilidades
        for (int i = 0; i < abilities.Length; i++)
        {
            // Iniciar habilidad al presionar (solo si no estamos en ningún estado especial)
            if (Input.GetKeyDown(abilities[i].activationKey) && abilities[i].isReady && !isDashing && !isJumping && !isFalling && !isInImpactPause)
            {
                // Dash (habilidad 0) maneja diferente el costo de maná
                if (i == 0) // Dash
                {
                    // Solo permitir dash si estamos en movimiento (como en Sphere.cs)
                    if (isMoving && playerStats.CurrentMana >= dashInitialManaCost)
                    {
                        UseAbilityServerRpc(i);
                        isDashKeyHeld = true;
                    }
                    else if (!isMoving)
                    {
                        Debug.Log("No se puede usar dash: No hay dirección de movimiento");
                    }
                }
                else if (i == 1) // Terremoto
                {
                    if (playerStats.CurrentMana >= abilities[i].manaCost)
                    {
                        UseAbilityServerRpc(i);
                    }
                }
                else
                {
                    // Otras habilidades
                    if (playerStats.CurrentMana >= abilities[i].manaCost)
                    {
                        UseAbilityServerRpc(i);
                    }
                }
            }
            
            // Detectar cuando se suelta la tecla Q (dash)
            if (i == 0 && Input.GetKeyUp(abilities[i].activationKey) && isDashKeyHeld)
            {
                isDashKeyHeld = false;
                if (isDashing)
                {
                    EndDashEarlyServerRpc();
                }
            }
        }

        // Si estamos en un dash, actualizar
        if (isDashing)
        {
            UpdateDash();
        }
        // Si estamos en salto/terremoto, actualizar
        else if (isJumping || isFalling)
        {
            UpdateJump();
        }
        // Si estamos en pausa de impacto, asegurar que el personaje no se mueve
        else if (isInImpactPause)
        {
            // Forzar posición fija durante la pausa
            transform.position = jumpStartPosition;
            
            // Asegurar que el Rigidbody no se mueve
            if (rb != null && !rb.isKinematic)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        else if (isMoving && !isDashing && !isJumping && !isFalling && !isInImpactPause)
        {
            // Mover hacia la posición de destino si no estamos en ningún estado especial
            MoveTowardsTarget();
        }
    }
    private void HandleMouseInput()
    {
        // Detectar clic derecho para movimiento
        if (Input.GetMouseButtonDown(1))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit))
            {
                // Si estamos en pausa de impacto, no permitir movimiento
                if (isInImpactPause)
                {
                    Debug.Log("No se puede mover durante la pausa de impacto");
                    return;
                }
                
                // Guardar posición de destino
                targetPosition = hit.point;
                isMoving = true;
                
                // Mostrar indicador visual de clic
                if (clickIndicatorPrefab != null)
                {
                    // Instanciar en posición ligeramente elevada para evitar problemas de z-fighting
                    Vector3 spawnPosition = hit.point + new Vector3(0, 0.1f, 0);
                    Instantiate(clickIndicatorPrefab, spawnPosition, Quaternion.identity);
                }
                
                // Enviar evento de movimiento al servidor si estamos en red
                SetMovementTargetServerRpc(targetPosition);
            }
        }
    }
    
    private void MoveTowardsTarget()
    {
        // Este método es opcional, puedes implementarlo si quieres que el jugador
        // se mueva automáticamente hacia el punto de clic como en Sphere.cs
        // Para este ejemplo, suponemos que otro script ya maneja el movimiento básico
        
        // No moverse si estamos en pausa de impacto
        if (isInImpactPause) return;
        
        // Verificar si hemos llegado al destino
        if (Vector3.Distance(transform.position, targetPosition) < 0.1f)
        {
            isMoving = false;
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
            }
        }
    }

    [ServerRpc]
    private void UseAbilityServerRpc(int abilityIndex)
    {
        if (abilityIndex < 0 || abilityIndex >= abilities.Length)
        {
            Debug.LogError($"Índice de habilidad fuera de rango: {abilityIndex}");
            return;
        }

        Ability ability = abilities[abilityIndex];
        
        // Para el dash, solo consumimos el coste inicial aquí
        float manaCost = (abilityIndex == 0) ? dashInitialManaCost : ability.manaCost;
        
        // Solo el servidor verifica el costo de maná
        if (playerStats.UseMana(manaCost))
        {
            // La habilidad fue exitosa, procesarla según su tipo
            ProcessAbilitySuccessClientRpc(abilityIndex);
            
            // Para el dash no iniciamos cooldown aquí, lo haremos al finalizar
            if (abilityIndex != 0)
            {
                StartCoroutine(StartCooldown(abilityIndex));
            }
        }
        else
        {
            // Maná insuficiente
            AbilityFailedClientRpc(abilityIndex);
        }
    }

    [ServerRpc]
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

    [ServerRpc]
    private void ConsumeDashManaServerRpc(float amount)
    {
        // Consumir maná durante el dash continuo
        playerStats.UseMana(amount);
    }
    
    [ServerRpc]
    private void SetMovementTargetServerRpc(Vector3 position)
    {
        // No permitir establecer destino durante la pausa
        if (isInImpactPause) return;
        
        // Sincronizar la posición de destino en todos los clientes
        SetMovementTargetClientRpc(position);
    }
    
    [ClientRpc]
    private void SetMovementTargetClientRpc(Vector3 position)
    {
        // No permitir establecer destino durante la pausa
        if (isInImpactPause) return;
        
        // Solo actualizar si no somos el propietario (el propietario ya lo ha actualizado)
        if (!IsOwner)
        {
            targetPosition = position;
            isMoving = true;
        }
    }

    [ClientRpc]
    private void ProcessAbilitySuccessClientRpc(int abilityIndex)
    {
        if (abilityIndex < 0 || abilityIndex >= abilities.Length)
            return;
        
        // Para el dash no iniciamos cooldown aquí, lo haremos al finalizar
        if (abilityIndex != 0)
        {
            StartCoroutine(StartCooldown(abilityIndex));
        }
        
        // Procesar la habilidad según su índice
        switch (abilityIndex)
        {
            case 0: // Dash
                StartDash();
                break;
            case 1: // Terremoto
                StartJump();
                break;
            // Aquí puedes añadir más casos para otras habilidades
            default:
                Debug.LogWarning($"Habilidad no implementada: {abilityIndex}");
                break;
        }
    }

    [ClientRpc]
    private void AbilityFailedClientRpc(int abilityIndex)
    {
        if (!IsOwner) return;
        
        if (abilityIndex < 0 || abilityIndex >= abilities.Length)
            return;
            
        Debug.Log($"No tienes suficiente maná para usar {abilities[abilityIndex].name}");
    }

    private IEnumerator StartCooldown(int abilityIndex)
    {
        if (abilityIndex < 0 || abilityIndex >= abilities.Length)
            yield break;
            
        Ability ability = abilities[abilityIndex];
        ability.isReady = false;
        
        // Guardar el tiempo en que terminará el cooldown
        ability.cooldownEndTime = Time.time + ability.cooldown;
        
        Debug.Log($"Habilidad {ability.name} en cooldown por {ability.cooldown} segundos");
        
        yield return new WaitForSeconds(ability.cooldown);
        
        ability.isReady = true;
        Debug.Log($"Habilidad {ability.name} lista para usar");
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
            Ability ability = abilities[index];
            if (!ability.isReady)
            {
                return Mathf.Max(0, ability.cooldownEndTime - Time.time);
            }
        }
        return 0f;
    }

    // IMPLEMENTACIÓN DE LA HABILIDAD DE TERREMOTO MEJORADA
    
    private void StartJump()
    {
        if (isJumping || isFalling) return;
        
        // Guardar posición inicial para cálculos
        jumpStartPosition = transform.position;
        
        // Iniciar fase de salto
        isJumping = true;
        jumpTime = 0f;
        
        // Desactivar gravedad y preparar para el salto
        if (rb != null)
        {
            rb.useGravity = false;
            rb.velocity = Vector3.zero; // Detener cualquier movimiento previo
        }
        
        // Si hay character controller, desactivarlo durante el salto para control manual
        if (characterController != null)
        {
            characterController.enabled = false;
        }
        
        Debug.Log("¡Iniciando salto para terremoto!");
    }
    
    private void UpdateJump()
    {
        jumpTime += Time.deltaTime;
        
        if (isJumping)
        {
            // Fase de ascenso (con tiempo ajustable)
            if (jumpTime <= riseTime)
            {
                // Usamos una curva sinusoidal para un movimiento más natural
                float progress = jumpTime / riseTime; // Normalizado de 0 a 1
                float height = jumpHeight * Mathf.Sin(progress * Mathf.PI / 2);
                
                // Calcular nueva posición
                Vector3 newPosition = new Vector3(
                    jumpStartPosition.x,
                    jumpStartPosition.y + height,
                    jumpStartPosition.z
                );
                
                // Aplicar posición
                transform.position = newPosition;
                
                // Si estamos en el punto más alto, guardarlo para referencia
                if (progress >= 0.99f)
                {
                    jumpHighestPosition = newPosition;
                }
            }
            else
            {
                // Cambiar a fase de caída
                isJumping = false;
                isFalling = true;
                
                // Guardar la posición más alta alcanzada para iniciar la caída desde ahí
                if (jumpHighestPosition == Vector3.zero)
                {
                    // Si por alguna razón no se guardó, usamos la posición actual
                    jumpHighestPosition = transform.position;
                }
                
                jumpTime = 0f; // Reiniciar tiempo para la fase de caída
            }
        }
        else if (isFalling)
        {
            // Fase de descenso (más rápida que la subida)
            if (jumpTime <= fallTime)
            {
                // Curva de caída acelerada
                float progress = jumpTime / fallTime; // Normalizado de 0 a 1
                
                // Utilizar una curva cuadrática para que la caída sea más rápida hacia el final
                float t = progress;
                float heightFactor = 1 - (t * t); // Caída acelerada
                
                // La altura al inicio de la caída debe ser jumpHeight
                float fallDistance = jumpHighestPosition.y - jumpStartPosition.y;
                
                // Calcular nueva posición (descendiendo desde altura máxima)
                Vector3 newPosition = new Vector3(
                    jumpStartPosition.x,
                    jumpStartPosition.y + (fallDistance * heightFactor),
                    jumpStartPosition.z
                );
                
                // Aplicar posición
                transform.position = newPosition;
                
                // Debug para verificar la caída
                if (progress >= 0.98f)
                {
                    // Nos acercamos al final, asegurarnos de que terminaremos en la posición inicial
                    transform.position = new Vector3(
                        jumpStartPosition.x,
                        jumpStartPosition.y + 0.01f, // Pequeño offset para evitar problemas de física
                        jumpStartPosition.z
                    );
                }
            }
            else
            {
                // El salto ha terminado, producir efecto de terremoto
                isFalling = false;
                isInImpactPause = true;
                
                // Restaurar posición exactamente a la inicial
                transform.position = jumpStartPosition;
                
                // Configurar Rigidbody para la pausa
                if (rb != null)
                {
                    rb.useGravity = true;
                    rb.velocity = Vector3.zero; // Detener al impactar
                    rb.isKinematic = true; // Evitar que fuerzas externas lo muevan durante la pausa
                }
                
                // Activar efecto de terremoto
                TriggerEarthquakeEffectServerRpc();
                
                // Iniciar la pausa después del impacto como una corrutina para no bloquear la ejecución
                StartCoroutine(ImpactPause());
                
                Debug.Log("¡IMPACTO! Iniciando pausa de " + impactPauseTime + " segundos");
            }
        }
    }
    
    private IEnumerator ImpactPause()
    {
        // Asegurarse de que estamos en modo pausa
        isInImpactPause = true;
        
        // Informar que estamos en pausa
        Debug.Log("Inicio de pausa de impacto: " + Time.time);
        
        // Esperar el tiempo de pausa exacto
        yield return new WaitForSeconds(impactPauseTime);
        
        Debug.Log("Fin de pausa de impacto: " + Time.time);
        
        // Restaurar estado después de la pausa
        isInImpactPause = false;
        
        // Restaurar controladores y física
        if (characterController != null)
        {
            characterController.enabled = true;
        }
        
        if (rb != null)
        {
            rb.isKinematic = false;
        }
        
        // Resetear las variables de salto
        jumpHighestPosition = Vector3.zero;
    }
    
    [ServerRpc]
    private void TriggerEarthquakeEffectServerRpc()
    {
        // Sincronizar el efecto de terremoto en todos los clientes
        TriggerEarthquakeEffectClientRpc();
    }
    
    [ClientRpc]
    private void TriggerEarthquakeEffectClientRpc()
    {
        // Mostrar efecto visual
        if (earthquakeEffectPrefab != null)
        {
            GameObject effect = Instantiate(earthquakeEffectPrefab, transform.position, Quaternion.identity);
            
            // Escalar el efecto según el radio
            effect.transform.localScale = new Vector3(earthquakeRadius * 2, 1, earthquakeRadius * 2);
            
            // Destruir después de unos segundos
            Destroy(effect, 2.0f);
        }
        
        // Aplicar cámara shake si estamos en el cliente dueño
        if (IsOwner)
        {
            // Aquí podrías añadir un efecto de sacudida de cámara si tienes uno implementado
            // Por ejemplo:
            // CameraShake.Instance.ShakeCamera(0.5f, 1.0f);
        }
        
        // Aplicar efecto de física y daño solo en el servidor
        if (IsServer)
        {
            ApplyEarthquakeEffects();
        }
    }
    
    private void ApplyEarthquakeEffects()
    {
        // Buscar objetos en un radio
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, earthquakeRadius, affectedLayers);
        
        foreach (var hit in hitColliders)
        {
            // Ignorar a nosotros mismos
            if (hit.transform == transform)
                continue;
                
            // Puedes implementar daño aquí con tu sistema actual
            // Ejemplo:
            // var enemyStats = hit.GetComponent<EnemyStats>();
            // if (enemyStats != null) enemyStats.TakeDamage(earthquakeDamage);
            
            // Aplicar fuerza de empuje si tiene Rigidbody
            Rigidbody targetRb = hit.GetComponent<Rigidbody>();
            if (targetRb != null)
            {
                Vector3 direction = (hit.transform.position - transform.position).normalized;
                direction.y = 0.5f; // Agregar componente hacia arriba para que sea más interesante
                
                // Aplicar fuerza explosiva
                targetRb.AddForce(direction * earthquakeForce, ForceMode.Impulse);
            }
        }
    }

    // Implementación del dash mejorado basado en Sphere.cs
    private void StartDash()
    {
        if (isDashing) return;
        
        // Usar la dirección hacia el punto de clic (como en Sphere)
        if (isMoving)
        {
            dashDirection = (targetPosition - transform.position).normalized;
            dashDirection.y = 0; // Mantener el dash en el plano horizontal
        }
        else
        {
            // Fallback a la dirección hacia donde mira el personaje
            dashDirection = transform.forward.normalized;
        }
        
        // Iniciar el dash
        isDashing = true;
        dashTimeLeft = dashMaxDuration;
        dashProgress = 0f;
        
        // Aplicar un impulso inicial para que el dash se sienta más impactante
        if (rb != null)
        {
            rb.velocity = dashDirection * dashSpeed * 0.5f;
        }
        
        Debug.Log("¡Dash iniciado en dirección: " + dashDirection);
    }

    private void UpdateDash()
    {
        if (dashTimeLeft > 0 && isDashing)
        {
            // Consumir maná continuamente mientras se hace dash
            float manaCost = dashManaCostPerSecond * Time.deltaTime;
            
            // Si no hay suficiente maná, terminar el dash
            if (playerStats.CurrentMana < manaCost)
            {
                EndDashEarly();
                return;
            }
            
            // Consumir maná (enviamos al servidor para que lo maneje)
            if (IsOwner)
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
            // Similar a Sphere.cs que cambia la dirección del dash durante el movimiento
            if (isMoving && Vector3.Distance(transform.position, targetPosition) > 1.0f)
            {
                // Reorientar gradualmente el dash hacia el punto de destino
                Vector3 newDirection = (targetPosition - transform.position).normalized;
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
                transform.position += dashDirection * dashSpeed * speedMultiplier * Time.deltaTime;
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
        StartCoroutine(StartCooldown(0)); // El dash es la habilidad 0
    }
    
    private void EndDashComplete()
    {
        // Finalizar el dash
        isDashing = false;
        
        // Aplicar una desaceleración gradual
        StartCoroutine(SmoothDashEnd());
        
        // Activar cooldown
        StartCoroutine(StartCooldown(0)); // El dash es la habilidad 0
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
    
    // Método público para verificar si el jugador está en pausa de impacto
    public bool IsInImpactPause()
    {
        return isInImpactPause;
    }
}