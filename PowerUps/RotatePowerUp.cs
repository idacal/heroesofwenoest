using UnityEngine;

public class RotatePowerUp : MonoBehaviour
{
    public float rotationSpeed = 50f;
    public float bobHeight = 0.5f;
    public float bobSpeed = 1f;

    private Vector3 startPos;

    void Start()
    {
        startPos = transform.position;
    }

    void Update()
    {
        // Rotación continua
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
        
        // Efecto de flotación
        Vector3 pos = startPos;
        pos.y += Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = pos;
    }
}