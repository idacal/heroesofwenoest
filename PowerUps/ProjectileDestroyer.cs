using UnityEngine;
using Unity.Netcode;

public class ProjectileDestroyer : MonoBehaviour
{
    [SerializeField] private float destroyTime = 3f;
    private float timeCreated;
    private bool hasSentRequest = false;

    private void Start()
    {
        timeCreated = Time.time;
    }
    
    private void Update()
    {
        // Si ha pasado el tiempo y no hemos enviado la solicitud aún
        if (!hasSentRequest && Time.time > timeCreated + destroyTime)
        {
            hasSentRequest = true;
            
            // Verificar si este objeto tiene un CombatProjectile
            CombatProjectile projectile = GetComponent<CombatProjectile>();
            if (projectile != null)
            {
                // Solicitar destrucción de forma segura
                projectile.RequestDestroyServerRpc();
                
                // Desactivar este componente para no seguir intentando
                this.enabled = false;
            }
            else
            {
                // Si no tiene CombatProjectile pero tiene NetworkObject, solo desactivarlo
                NetworkObject netObj = GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    // Simplemente desactivamos el GameObject sin destruirlo
                    gameObject.SetActive(false);
                    this.enabled = false;
                }
                else
                {
                    // Si no tiene componentes de red, podemos destruirlo directamente
                    Destroy(gameObject);
                }
            }
        }
    }
}