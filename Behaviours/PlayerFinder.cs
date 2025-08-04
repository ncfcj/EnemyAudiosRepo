using BepInEx.Logging;
using Photon.Pun;
using UnityEngine;

namespace EnemyAudios.Behaviours;

public class PlayerFinder : MonoBehaviour
{
    private static PlayerFinder? _instance;
    private static ManualLogSource _logger = BepInEx.Logging.Logger.CreateLogSource("EnemyAudios");
    
    public static EnemyAudioBehaviour? EnemyAudioBehaviour { get; set; }
    
    public static void EnsureInitialized()
    {
        if (_instance != null) 
            return;
        
        _instance = new GameObject(nameof(PlayerFinder)).AddComponent<PlayerFinder>();
        
        DontDestroyOnLoad(_instance.gameObject);
        var logger = EnemyAudioBehaviour.Logger;
        var localPlayer = PhotonNetwork.LocalPlayer;
        
        var data = $"PlayerFinder initialized for Player {localPlayer?.ActorNumber ?? -1}";
        
        logger.LogInfo(data);
    }
    
    private void OnDestroy()
    {
        if (_instance != this) 
            return;
        
        EnemyAudioBehaviour = null;
        _instance = null;
        
        _logger.LogInfo("PlayerFinder destroyed, clearing cache.");
    }
}