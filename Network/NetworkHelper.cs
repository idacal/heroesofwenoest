using Unity.Netcode;
using UnityEngine;

public static class NetworkHelper
{
    // Get the local player's GameObject
    public static GameObject GetLocalPlayerObject()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
            return null;
            
        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(localClientId, out var client))
        {
            return client.PlayerObject?.gameObject;
        }
        
        return null;
    }
}