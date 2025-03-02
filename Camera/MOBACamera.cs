using UnityEngine;
using Unity.Netcode;

public class MOBACamera : MonoBehaviour
{
    [Header("Configuración de Cámara")]
    [SerializeField] private Transform target;
    [SerializeField] private float cameraHeight = 15f;
    [SerializeField] private float cameraDistance = 12f;
    [SerializeField] private float cameraPitch = 45f; // Ángulo de inclinación en grados (45° es típico para MOBAs)
    [SerializeField] private bool findTargetAutomatically = true;

    [Header("Movimiento")]
    [SerializeField] private float edgeScrollSpeed = 30.0f;
    [SerializeField] private float edgeScrollThreshold = 20.0f;
    [SerializeField] private bool useEdgeScrolling = true;
    [SerializeField] private float mouseWheelZoomSpeed = 5.0f;
    [SerializeField] private Vector2 zoomRange = new Vector2(5f, 20f); // Min, Max
    [SerializeField] private float snapToTargetSpeed = 8.0f; // Velocidad para centrar en el jugador
    
    [Header("Límites del Mapa")]
    [SerializeField] private bool useBoundaries = true;
    [SerializeField] private float mapMinX = -50f;
    [SerializeField] private float mapMaxX = 50f;
    [SerializeField] private float mapMinZ = -50f;
    [SerializeField] private float mapMaxZ = 50f;

    [Header("Controles")]
    [SerializeField] private KeyCode centerOnPlayerKey = KeyCode.Space;

    // Variables internas
    private Vector3 cameraTargetPosition;
    private float currentZoom;
    private bool isDragging = false;
    private Vector3 dragStartPosition;
    private Vector3 dragCurrentPosition;
    private bool snapToPlayer = false;
    
    private void Start()
    {
        // Configurar la rotación inicial de la cámara (isométrica)
        transform.rotation = Quaternion.Euler(cameraPitch, 45f, 0f);
        
        // Inicializar el zoom
        currentZoom = cameraDistance;
        
        if (findTargetAutomatically)
        {
            FindPlayerTarget();
        }
        
        // Inicializar la posición de la cámara
        if (target != null)
        {
            cameraTargetPosition = target.position;
            // Posicionamiento inicial centrado en el jugador
            UpdateCameraPosition(false);
        }
        else
        {
            // Si no hay target, usamos la posición actual de la cámara
            Vector3 directionToGround = Quaternion.Euler(-cameraPitch, -45f, 0f) * Vector3.forward;
            cameraTargetPosition = transform.position + directionToGround * currentZoom;
            cameraTargetPosition.y = 0; // Ignorar altura
        }
    }
    
    private void LateUpdate()
    {
        if (target == null && findTargetAutomatically)
        {
            FindPlayerTarget();
        }
        
        // Manejar zoom con rueda del ratón
        HandleZoom();
        
        // Tecla para centrar la cámara en el jugador
        if (Input.GetKeyDown(centerOnPlayerKey) && target != null)
        {
            snapToPlayer = true;
        }
        
        // Si estamos centrando en el jugador
        if (snapToPlayer && target != null)
        {
            cameraTargetPosition = Vector3.Lerp(cameraTargetPosition, target.position, snapToTargetSpeed * Time.deltaTime);
            
            // Si estamos lo suficientemente cerca, desactivar el snap
            if (Vector3.Distance(cameraTargetPosition, target.position) < 0.1f)
            {
                snapToPlayer = false;
            }
        }
        
        // Manejar el desplazamiento con bordes de pantalla
        if (useEdgeScrolling && !snapToPlayer)
        {
            HandleEdgeScrolling();
        }
        
        // Manejar el arrastre de cámara con el botón central/derecho del ratón
        if (!snapToPlayer)
        {
            HandleMouseDrag();
        }
        
        // Aplicar límites del mapa
        if (useBoundaries)
        {
            cameraTargetPosition.x = Mathf.Clamp(cameraTargetPosition.x, mapMinX, mapMaxX);
            cameraTargetPosition.z = Mathf.Clamp(cameraTargetPosition.z, mapMinZ, mapMaxZ);
        }
        
        // Actualizar la posición de la cámara
        UpdateCameraPosition(true);
    }
    
    private void HandleZoom()
    {
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        
        if (scrollInput != 0)
        {
            // Ajustar zoom (negativo porque hacia abajo en la rueda = alejarse)
            currentZoom = Mathf.Clamp(currentZoom - scrollInput * mouseWheelZoomSpeed, zoomRange.x, zoomRange.y);
        }
    }
    
    private void HandleEdgeScrolling()
    {
        if (isDragging) return; // No hacer scroll en bordes si estamos arrastrando
        
        // Detección de bordes
        Vector3 moveDirection = Vector3.zero;
        
        if (Input.mousePosition.x < edgeScrollThreshold)
        {
            moveDirection.x = -1;
        }
        else if (Input.mousePosition.x > Screen.width - edgeScrollThreshold)
        {
            moveDirection.x = 1;
        }
        
        if (Input.mousePosition.y < edgeScrollThreshold)
        {
            moveDirection.z = -1;
        }
        else if (Input.mousePosition.y > Screen.height - edgeScrollThreshold)
        {
            moveDirection.z = 1;
        }
        
        if (moveDirection != Vector3.zero)
        {
            // Convertir dirección según la rotación de la cámara
            moveDirection = Quaternion.Euler(0, transform.eulerAngles.y, 0) * moveDirection;
            
            // Aplicar movimiento
            cameraTargetPosition += moveDirection.normalized * edgeScrollSpeed * Time.deltaTime;
        }
    }
    
    private void HandleMouseDrag()
    {
        // Iniciar arrastre con botón central o derecho
        if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
        {
            isDragging = true;
            dragStartPosition = Input.mousePosition;
            dragCurrentPosition = dragStartPosition;
        }
        
        // Actualizar posición durante arrastre
        if (isDragging)
        {
            dragCurrentPosition = Input.mousePosition;
            Vector3 difference = dragStartPosition - dragCurrentPosition;
            
            if (difference.magnitude > 2) // Pequeño umbral para evitar movimientos accidentales
            {
                // Convertir el movimiento del ratón a dirección de mundo
                Vector3 dragDirection = new Vector3(difference.x, 0, difference.y) * 0.01f;
                dragDirection = Quaternion.Euler(0, transform.eulerAngles.y, 0) * dragDirection;
                
                // Aplicar movimiento
                cameraTargetPosition += dragDirection * edgeScrollSpeed;
                
                // Actualizar punto de inicio para el próximo frame
                dragStartPosition = dragCurrentPosition;
            }
        }
        
        // Finalizar arrastre
        if (Input.GetMouseButtonUp(1) || Input.GetMouseButtonUp(2))
        {
            isDragging = false;
        }
    }
    
    private void UpdateCameraPosition(bool smooth)
    {
        // Calcular la posición deseada de la cámara
        Vector3 directionFromTarget = new Vector3(0, 0, -1); // Dirección base (hacia -Z)
        directionFromTarget = Quaternion.Euler(0, transform.eulerAngles.y, 0) * directionFromTarget;
        
        // Aplicar altura y distancia
        Vector3 desiredPosition = cameraTargetPosition;
        desiredPosition.y = 0; // Ignorar altura del target
        desiredPosition += directionFromTarget * currentZoom;
        desiredPosition.y = cameraHeight;
        
        if (smooth)
        {
            // Movimiento suave
            transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * 10f);
        }
        else
        {
            // Posicionamiento inmediato
            transform.position = desiredPosition;
        }
    }
    
    private void FindPlayerTarget()
    {
        // Buscar jugador local entre todos los jugadores
        PlayerNetwork[] players = FindObjectsOfType<PlayerNetwork>();
        
        foreach (PlayerNetwork player in players)
        {
            if (player.IsLocalPlayer)
            {
                target = player.transform;
                return;
            }
        }
    }
    
    // Método público para asignar un objetivo manualmente
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
    
    // Método público para obtener el objetivo actual
    public Transform GetTarget()
    {
        return target;
    }
    
    // Método público para centrar la cámara en el jugador
    public void CenterOnPlayer()
    {
        if (target != null)
        {
            snapToPlayer = true;
        }
    }
}