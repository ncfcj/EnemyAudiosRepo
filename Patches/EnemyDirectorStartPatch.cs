using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace EnemyAudios.Patches;

[HarmonyPatch(typeof(EnemyDirector))]
internal class EnemyDirectorStartPatch
{
    private static readonly ManualLogSource _logger = Logger.CreateLogSource("EnemyAudio");
    
    private static HashSet<string> FilterEnemies { get; set; } = [];
    private static bool _setupComplete = false;
    private static ConfigFile _configFile = null!;
    
    public static void Initialize(ConfigFile config)
    {
        _configFile = config;
        _logger.LogInfo("[EnemyAudio] EnemyDirectorStartPatch initialized with ConfigFile.");
    }
    
    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    public static void SetupEnemies(EnemyDirector __instance)
    {
        if (_setupComplete)
            return;

        FilterEnemies = new HashSet<string>();

        foreach (var enemySetupList in new[] { __instance.enemiesDifficulty1, __instance.enemiesDifficulty2, __instance.enemiesDifficulty3 })
        {
            foreach (var enemySetup in enemySetupList)
            {
                var spawnObjectName = enemySetup.spawnObjects
                    .FirstOrDefault(obj => !obj.name.Contains("Director"))?.name 
                                      ?? enemySetup.spawnObjects[0].name;
                
                FilterEnemies.Add(spawnObjectName);
            }
        }

        _setupComplete = true;
        SetupEnemyConfig();
    }
    
    private static void SetupEnemyConfig()
    {
        _logger.LogInfo("[EnemyAudio] Setting up enemy config...");
        foreach (var filterEnemy in FilterEnemies)
        {
            filterEnemy.Replace("Enemy - ", "");
            BasePlugin.EnemyConfigEntries[filterEnemy] = _configFile.Bind("Enemies", filterEnemy, true, $"Enables/disables ability for {filterEnemy} to reproduce audios.");
            _logger.LogInfo("[EnemyAudio] Added config entry for enemy: " + filterEnemy);
        }
    }
}