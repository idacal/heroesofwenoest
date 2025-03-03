using UnityEngine;

public class MOBACamera : MonoBehaviour
{
    [Header("Camera Configuration")]
    [SerializeField] private Transform target;
    [SerializeField] private float cameraHeight = 15f;
    [SerializeField] private float cameraDistance = 12f;
    [SerializeField] private float cameraPitch = 45f; // Ángulo en grados (45° típico para MOBAs)
    
    [Header("Movement")]
    [SerializeField] private float edgeScrollSpeed = 15.0f;
    [SerializeField] private float edgeScrollThreshold = 20.0f;
    [SerializeField] private bool useEdgeScrolling = true;
    [SerializeField] private float mouseWheelZoomSpeed = 5.0f;
    [SerializeField] private Vector2 heightZoomRange = new Vector2(8f, 25f); // Min, Max altura
    [SerializeField] private Vector2 distanceZoomRange = new Vector2(8f, 20f); // Min, Max distancia
    [SerializeField] private float snapToTargetSpeed = 8.0f;
    
    [Header("Map Boundaries")]
    [SerializeField] private bool useBoundaries = true;
    [SerializeField] private float mapMinX = -50f;
    [SerializeField] private float mapMaxX = 50f;
    [SerializeField] private float mapMinZ = -50f;
    [SerializeField] private float mapMaxZ = 50f;
    
    [Header("Controls")]
    [SerializeField] private KeyCode centerOnPlayerKey = KeyCode.Space;
    [SerializeField] private KeyCode cameraDragKey = KeyCode.Mouse2; // Solo botón medio (rueda) para arrastrar
    
    // Variables internas - TODAS PURAMENTE LOCALES
    private Vector3 cameraTargetPosition;
    private float currentZoomFactor = 0.5f; // Factor de zoom normalizado (0-1), donde 0.5 es el zoom inicial
    private bool isDragging = false;
    private Vector3 dragStartPosition;
    private Vector3 dragCurrentPosition;
    private bool snapToPlayer = false;
    
    // Identificador único para esta cámara
    private string cameraId;
    
    private void Awake()
    {
        // Generar ID único para esta cámara
        cameraId = System.Guid.NewGuid().ToString().Substring(0, 8);
        
        // Asegurar que esta cámara NO se sincroniza por red de ninguna manera
        Camera cam = GetComponent<Camera>();
        if (cam != null)
        {
            cam.tag = "MainCamera";
        }
        
        // Verificar que no hay componentes de red
        Unity.Netcode.NetworkObject networkObj = GetComponent<Unity.Netcode.NetworkObject>();
        if (networkObj != null)
        {
            Debug.LogWarning("[CAMERA] Detectado NetworkObject en cámara - Eliminando para evitar sincronización");
            Destroy(networkObj);
        }
        
        // Registrar ID único para debugging
        gameObject.name = $"LocalCamera_{cameraId}";
        
        Debug.Log($"[CAMERA] Inicializando cámara con ID único: {cameraId}");
    }
    
private void Start()
{
    // Rotación inicial (personalizada según tus especificaciones)
    transform.rotation = Quaternion.Euler(45f, 0f, 0f);
    
    // Inicializar posición de la cámara
    if (target != null)
    {
        cameraTargetPosition = target.position;
    }
    else
    {
        // Sin target, usar posición específica
        cameraTargetPosition = new Vector3(0f, 10f, -8f);
    }
    
    UpdateCameraPosition(false); // Posicionamiento inmediato
}
    
    private void LateUpdate()
    {
        // Solo actualizar si tenemos un objetivo
        if (target == null)
        {
            return;
        }
        
        // Zoom con rueda del ratón
        HandleZoom();
        
        // Tecla para centrar en jugador
        if (Input.GetKeyDown(centerOnPlayerKey))
        {
            Debug.Log($"[CAMERA_{cameraId}] Centrando cámara en jugador {target.name}");
            snapToPlayer = true;
        }
        
        // Centrado en jugador
        if (snapToPlayer)
        {
            cameraTargetPosition = Vector3.Lerp(cameraTargetPosition, target.position, snapToTargetSpeed * Time.deltaTime);
            
            // Desactivar snap cuando estamos lo suficientemente cerca
            if (Vector3.Distance(cameraTargetPosition, target.position) < 0.1f)
            {
                snapToPlayer = false;
            }
        }
        
        // Scroll en bordes de pantalla
        if (useEdgeScrolling && !snapToPlayer)
        {
            HandleEdgeScrolling();
        }
        
        // Arrastrar solo con botón MEDIO (no derecho)
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
        
        // Actualizar posición final
        UpdateCameraPosition(true);
    }
    
    private void HandleZoom()
    {
        // Obtener entrada de la rueda del ratón
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        
        if (scrollInput != 0)
        {
            // Ajustar el factor de zoom (0-1) basado en la entrada
            // Positivo = acercar = aumentar factor
            // Negativo = alejar = disminuir factor
            float zoomDelta = scrollInput * 0.1f; // Ajustamos sensibilidad
            
            // Actualizar factor de zoom (limitado entre 0 y 1)
            float newZoomFactor = Mathf.Clamp01(currentZoomFactor + zoomDelta);
            
            if (Mathf.Abs(newZoomFactor - currentZoomFactor) > 0.001f)
            {
                Debug.Log($"[CAMERA_{cameraId}] Zoom Factor: {currentZoomFactor:F2} -> {newZoomFactor:F2}, Input: {scrollInput}");
                currentZoomFactor = newZoomFactor;
            }
        }
    }
    
    private void HandleEdgeScrolling()
    {
        if (isDragging) return; // No hacer edge scroll mientras arrastramos
        
        Vector3 moveDirection = Vector3.zero;
        
        // Bordes horizontales
        if (Input.mousePosition.x < edgeScrollThreshold)
        {
            moveDirection.x = -1;
        }
        else if (Input.mousePosition.x > Screen.width - edgeScrollThreshold)
        {
            moveDirection.x = 1;
        }
        
        // Bordes verticales
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
            // Convertir dirección según rotación de cámara
            moveDirection = Quaternion.Euler(0, transform.eulerAngles.y, 0) * moveDirection;
            
            // Aplicar movimiento
            Vector3 newPosition = cameraTargetPosition + moveDirection.normalized * edgeScrollSpeed * Time.deltaTime;
            cameraTargetPosition = newPosition;
        }
    }
    
    private void HandleMouseDrag()
    {
        // Iniciar arrastre SOLO con botón medio (rueda) - NO botón derecho
        if (Input.GetMouseButtonDown((int)cameraDragKey - (int)KeyCode.Mouse0))
        {
            isDragging = true;
            dragStartPosition = Input.mousePosition;
            dragCurrentPosition = dragStartPosition;
        }
        
        // Actualizar durante arrastre
        if (isDragging)
        {
            dragCurrentPosition = Input.mousePosition;
            Vector3 difference = dragStartPosition - dragCurrentPosition;
            
            if (difference.magnitude > 2) // Pequeño umbral para evitar movimientos accidentales
            {
                // Convertir movimiento del ratón a dirección mundial
                Vector3 dragDirection = new Vector3(difference.x, 0, difference.y) * 0.01f;
                dragDirection = Quaternion.Euler(0, transform.eulerAngles.y, 0) * dragDirection;
                
                // Aplicar movimiento
                Vector3 newPosition = cameraTargetPosition + dragDirection * edgeScrollSpeed;
                cameraTargetPosition = newPosition;
                
                // Actualizar punto de inicio para el próximo frame
                dragStartPosition = dragCurrentPosition;
            }
        }
        
        // Finalizar arrastre SOLO con botón medio (rueda)
        if (Input.GetMouseButtonUp((int)cameraDragKey - (int)KeyCode.Mouse0))
        {
            isDragging = false;
        }
    }
    
    private void UpdateCameraPosition(bool smooth)
    {
        // Calcular altura y distancia basadas en el factor de zoom
        float currentHeight = Mathf.Lerp(heightZoomRange.x, heightZoomRange.y, currentZoomFactor);
        float currentDistance = Mathf.Lerp(distanceZoomRange.x, distanceZoomRange.y, currentZoomFactor);
        
        // Calcular ángulo basado en el factor de zoom (opcional, para un efecto más MOBA)
        // float currentPitch = Mathf.Lerp(60f, 30f, currentZoomFactor); // Más cercano = más empinado
        
        // Calcular posición deseada usando parámetros actuales
        Vector3 directionFromTarget = new Vector3(0, 0, -1); // Dirección base (hacia -Z)
        directionFromTarget = Quaternion.Euler(0, transform.eulerAngles.y, 0) * directionFromTarget;
        
        // Aplicar altura y distancia actuales
        Vector3 desiredPosition = cameraTargetPosition;
        desiredPosition.y = 0; // Ignorar altura del target
        desiredPosition += directionFromTarget * currentDistance;
        desiredPosition.y = currentHeight;
        
        // Aplicar movimiento suave o inmediato
        if (smooth)
        {
            transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * 10f);
        }
        else
        {
            transform.position = desiredPosition;
        }
        
        // Si quieres ajustar también el ángulo con el zoom, descomenta esto:
        // transform.rotation = Quaternion.Lerp(transform.rotation, 
        //                                     Quaternion.Euler(currentPitch, transform.eulerAngles.y, 0),
        //                                     Time.deltaTime * 5f);
    }
    
    // Método público para asignar un objetivo
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        
        if (target != null)
        {
            Debug.Log($"[CAMERA_{cameraId}] Cámara ahora sigue a {target.name}");
            
            // Actualizar posición inmediatamente
            cameraTargetPosition = target.position;
            snapToPlayer = true;
        }
    }
    
    // Método público para obtener el objetivo actual
    public Transform GetTarget()
    {
        return target;
    }
    
    // Método público para centrar en el jugador
    public void CenterOnPlayer()
    {
        if (target != null)
        {
            Debug.Log($"[CAMERA_{cameraId}] Centrando manualmente en {target.name}");
            snapToPlayer = true;
        }
    }
}