using Unity.Netcode;
using UnityEngine;

// Extensión de PlayerStats para manejar la reducción de daño por escudo
public class PlayerStatsExtension : NetworkBehaviour
{
    // Referencia al PlayerStats principal
    private PlayerStats playerStats;
    
    // Variable de red para la reducción de daño actual
    private NetworkVariable<float> damageReduction = new NetworkVariable<float>(
        0f, // Valor por defecto (0% reducción)
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    
    private void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        
        if (playerStats == null)
        {
            Debug.LogError("No se encontró el componente PlayerStats en el mismo GameObject");
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Suscribirse al evento de daño si queremos interceptarlo
        // Esto requeriría modificar PlayerStats para exponer un evento antes de aplicar daño
    }
    
    // Método que será llamado por ShieldAbility para establecer la reducción de daño
    public void SetDamageReduction(float reduction)
    {
        if (!IsServer) 
        {
            SetDamageReductionServerRpc(reduction);
            return;
        }
        
        // Clamping entre 0 y 1 (0% a 100%)
        damageReduction.Value = Mathf.Clamp01(reduction);
        Debug.Log($"Reducción de daño establecida a: {damageReduction.Value * 100}%");
    }
    
    // Método para restablecer la reducción de daño a 0
    public void ResetDamageReduction()
    {
        if (!IsServer)
        {
            ResetDamageReductionServerRpc();
            return;
        }
        
        damageReduction.Value = 0f;
        Debug.Log("Reducción de daño restablecida a 0%");
    }
    
    // Método para obtener la reducción de daño actual
    public float GetDamageReduction()
    {
        return damageReduction.Value;
    }
    
    // Método para aplicar la reducción al daño recibido
    public float ApplyDamageReduction(float damage)
    {
        return damage * (1f - damageReduction.Value);
    }
    
    // ServerRpc para clientes
    [ServerRpc]
    private void SetDamageReductionServerRpc(float reduction)
    {
        SetDamageReduction(reduction);
    }
    
    [ServerRpc]
    private void ResetDamageReductionServerRpc()
    {
        ResetDamageReduction();
    }
}