using BepInEx.Logging;
using EnemyAudios.Behaviours;
using HarmonyLib;
using Photon.Pun;

namespace EnemyAudios.Patches;

[HarmonyPatch(typeof(PlayerAvatar), "Awake")]
internal class PlayerAvatarPatch
{
    private static readonly ManualLogSource _logger = Logger.CreateLogSource("");
    
    private static void Postfix(PlayerAvatar __instance)
    {
        if (!PhotonNetwork.IsConnectedAndReady)
            return;
        
        var enemyAudioBehaviour = __instance.GetComponent<EnemyAudioBehaviour>();
        
        if (enemyAudioBehaviour is null)
        {
            enemyAudioBehaviour = __instance.gameObject.AddComponent<EnemyAudioBehaviour>();
            _logger.LogInfo("[EnemyAudio] Added EnemyAudioBehaviour component to PlayerAvatar: " + __instance.playerName);
        }
        
        var component = __instance.GetComponent<PhotonView>();
        
        if (component is null || !component.IsMine)
            return;
        
        PlayerFinder.EnemyAudioBehaviour = enemyAudioBehaviour;
        
        _logger.LogInfo("[EnemyAudio] Set EnemyAudioBehaviour for local PlayerAvatar: " + __instance.playerName);
    }
}