using UnityEngine;

/// <summary>
/// Script extremadamente simple que destruye su GameObject después de un tiempo específico.
/// Añade este script al prefab del proyectil para garantizar su destrucción.
/// </summary>
public class ProjectileDestroyer : MonoBehaviour
{
    [SerializeField] private float destroyTime = 3f;

    void Update()
    {
        // En cada frame, programar la destrucción del objeto
        // Esto garantiza que el objeto se destruirá después del tiempo especificado
        Destroy(gameObject, destroyTime);
    }
}