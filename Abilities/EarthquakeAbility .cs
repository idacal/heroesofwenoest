using Unity.Netcode;
using UnityEngine;
using System.Collections;

namespace PlayerAbilities
{
public class EarthquakeAbility : BaseAbility
{
    [Header("Configuración de Terremoto")]
    [SerializeField] private float jumpHeight = 5f;           // Altura máxima del salto
    [SerializeField] private float riseTime = 0.8f;           // Tiempo de subida del salto (más lento)
    [SerializeField] private float fallTime = 0.4f;           // Tiempo de caída (más rápido)
    [SerializeField] private float impactPauseTime = 0.5f;    // Tiempo de pausa después del impacto
    [SerializeField] private float earthquakeRadius = 5f;     // Radio del efecto de terremoto
    [SerializeField] private float earthquakeForce = 10f;     // Fuerza de empuje
    [SerializeField] private GameObject earthquakeEffectPrefab; // Efecto visual del terremoto
    [SerializeField] private LayerMask affectedLayers;        // Capas afectadas por el terremoto

    // Estado del terremoto
    private bool isJumping = false;
    private bool isFalling = false;
    private bool isInImpactPause = false;
    private Vector3 jumpStartPosition;
    private Vector3 jumpHighestPosition;
    private float jumpTime = 0f;
    
    // Referencias a componentes externos
    private PlayerAbilityController abilityController;
    
    public override void Initialize(NetworkBehaviour owner)
    {
        base.Initialize(owner);
        abilityName = "Terremoto";
        activationKey = KeyCode.W;
        manaCost = 60f;
        cooldown = 10f;
        
        abilityController = owner.GetComponent<PlayerAbilityController>();
    }
    
    public override bool CanActivate()
    {
        // No permitir activar si ya estamos en medio de un salto o caída
        if (isJumping || isFalling || isInImpactPause)
        {
            return false;
        }
        
        return isReady && playerStats.CurrentMana >= manaCost;
    }
    
    public override void Activate()
    {
        StartJump();
    }
    
    public override void UpdateAbility()
    {
        // Actualizar lógica del salto/terremoto si está activo
        if (isJumping || isFalling)
        {
            UpdateJump();
        }
    }
    
    private void StartJump()
    {
        if (isJumping || isFalling) return;
        
        // Guardar posición inicial para cálculos
        jumpStartPosition = networkOwner.transform.position;
        
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
        
        if (networkOwner.IsOwner)
        {
            Debug.Log("¡Iniciando salto para terremoto!");
        }
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
                networkOwner.transform.position = newPosition;
                
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
                    jumpHighestPosition = networkOwner.transform.position;
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
                networkOwner.transform.position = newPosition;
                
                // Debug para verificar la caída
                if (progress >= 0.98f)
                {
                    // Nos acercamos al final, asegurarnos de que terminaremos en la posición inicial
                    networkOwner.transform.position = new Vector3(
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
                networkOwner.transform.position = jumpStartPosition;
                
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
                networkOwner.StartCoroutine(ImpactPause());
                
                if (networkOwner.IsOwner)
                {
                    Debug.Log("¡IMPACTO! Iniciando pausa de " + impactPauseTime + " segundos");
                }
            }
        }
    }
    
    private IEnumerator ImpactPause()
    {
        // Asegurarse de que estamos en modo pausa
        isInImpactPause = true;
        
        // Informar al controlador de habilidades sobre el estado de pausa
        abilityController.SetInImpactPause(true);
        
        // Informar que estamos en pausa
        if (networkOwner.IsOwner)
        {
            Debug.Log("Inicio de pausa de impacto: " + Time.time);
        }
        
        // Esperar el tiempo de pausa exacto
        yield return new WaitForSeconds(impactPauseTime);
        
        if (networkOwner.IsOwner)
        {
            Debug.Log("Fin de pausa de impacto: " + Time.time);
        }
        
        // Restaurar estado después de la pausa
        isInImpactPause = false;
        
        // Informar al controlador de habilidades que la pausa ha terminado
        abilityController.SetInImpactPause(false);
        
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
    
    [ServerRpc(RequireOwnership = false)]
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
            GameObject effect = Instantiate(earthquakeEffectPrefab, networkOwner.transform.position, Quaternion.identity);
            
            // Escalar el efecto según el radio
            effect.transform.localScale = new Vector3(earthquakeRadius * 2, 1, earthquakeRadius * 2);
            
            // Destruir después de unos segundos
            Destroy(effect, 2.0f);
        }
        
        // Aplicar cámara shake si estamos en el cliente dueño
        if (networkOwner.IsOwner)
        {
            // Aquí podrías añadir un efecto de sacudida de cámara si tienes uno implementado
            // Por ejemplo:
            // CameraShake.Instance.ShakeCamera(0.5f, 1.0f);
        }
        
        // Aplicar efecto de física y daño solo en el servidor
        if (networkOwner.IsServer)
        {
            ApplyEarthquakeEffects();
        }
    }
    
    private void ApplyEarthquakeEffects()
    {
        // Buscar objetos en un radio
        Collider[] hitColliders = Physics.OverlapSphere(networkOwner.transform.position, earthquakeRadius, affectedLayers);
        
        foreach (var hit in hitColliders)
        {
            // Ignorar a nosotros mismos
            if (hit.transform == networkOwner.transform)
                continue;
                
            // Puedes implementar daño aquí con tu sistema actual
            // Ejemplo:
            // var enemyStats = hit.GetComponent<EnemyStats>();
            // if (enemyStats != null) enemyStats.TakeDamage(earthquakeDamage);
            
            // Aplicar fuerza de empuje si tiene Rigidbody
            Rigidbody targetRb = hit.GetComponent<Rigidbody>();
            if (targetRb != null)
            {
                Vector3 direction = (hit.transform.position - networkOwner.transform.position).normalized;
                direction.y = 0.5f; // Agregar componente hacia arriba para que sea más interesante
                
                // Aplicar fuerza explosiva
                targetRb.AddForce(direction * earthquakeForce, ForceMode.Impulse);
            }
        }
    }
    
    // Propiedades públicas para que otros sistemas puedan consultar el estado
    public bool IsJumping => isJumping;
    public bool IsFalling => isFalling;
    public bool IsInImpactPause => isInImpactPause;
}
}