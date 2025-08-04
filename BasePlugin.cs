using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EnemyAudios.Patches;
using HarmonyLib;
using UnityEngine;

namespace EnemyAudios;

[BepInPlugin("nilt0n.EnemyAudios", "EnemyAudios", "1.0")]
public class BasePlugin : BaseUnityPlugin
{
    internal static BasePlugin Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger => Instance.BaseLogger;
    private ManualLogSource BaseLogger => base.Logger;
    private static Harmony? _harmony;
    
    public static ConfigEntry<float>? ConfigVoiceVolume;
    public static Dictionary<string, ConfigEntry<bool>> EnemyConfigEntries = new();
    public static ConfigEntry<int>? DelayBetweenReproductions;

    private void Awake()
    {
        Instance = this;
        gameObject.transform.parent = null;
        gameObject.hideFlags = HideFlags.HideAndDontSave;
        
        Logger.LogInfo("[EnemyAudios] Plugin is loaded!");

        SetupConfigurations();

        _harmony = new Harmony("EnemyAudios");
        _harmony.PatchAll();
        
        EnemyDirectorStartPatch.Initialize(Config);
    }
    
    private void SetupConfigurations()
    {
        ConfigVoiceVolume = Config
            .Bind("General", "Volume", 0.75f, 
                new ConfigDescription("Audio volume.", new AcceptableValueRange<float>(0.0f, 1f)));
        
        DelayBetweenReproductions = Config
            .Bind("General", "Delay", 60, 
                new ConfigDescription("Delay between Reproductions in seconds.", new AcceptableValueRange<int>(10, 120)));
    }
}