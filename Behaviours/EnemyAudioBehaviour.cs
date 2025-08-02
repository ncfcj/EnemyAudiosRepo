using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using EnemyAudios.Extensions;
using EnemyAudios.Models;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Networking;
using Random = UnityEngine.Random;
using Newtonsoft.Json;

namespace EnemyAudios.Behaviours;

public class EnemyAudioBehaviour : MonoBehaviour
{
    public PhotonView? photonView;
    
    private string? _audioFolderPath;
    private readonly List<byte[]> _receivedChunks = [];
    private int _expectedChunkCount;
    private readonly Queue<QueuedAudio> _audioQueue = new();
    private bool _isSendingAudio = false;
    private float _lastAudioDuration = 0f;
    private readonly Queue<string> _recentlyPlayed = new();
    private const string _audioFolderName = "EnemyAudio";
    private const string _jsonFileName = "audioFiles.json";
    
    private static readonly string[] SupportedAudioFormats = ["*.wav", "*.mp3"];
    private const int ChunkSize = 8192;
    
    private float DelayBetweenReproductions = BasePlugin.DelayBetweenReproductions!.Value;

    public static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource(nameof(EnemyAudioBehaviour));
    
    private void Awake()
    {
        photonView = GetPhotonView();
        StartCoroutine(StartEnemyBehavior());
    }
    
    #region Setup and Preparation
    private IEnumerator StartEnemyBehavior()
    {
        CreateFoldersAndJsonFiles();
        
        while (true)
        {
            if (SemiFunc.RunIsLevel())
                PlayRandomAudio();
            
            yield return new WaitForSeconds(DelayBetweenReproductions);
        }
    }
    
    #endregion
    
    #region Audio Playing, Sending and Receiving
    private void PlayRandomAudio()
    {
        var audioFiles = GetAudioFilesFromJsonList();
        
        if (audioFiles.Length == 0)
        {
            Logger.LogError("[EnemyAudio] No audio files with the names and extensions in the json file were found in the EnemyAudio folder.");
            return;
        }
        
        var availableFiles = audioFiles.Where(f => !_recentlyPlayed.Contains(f)).ToArray();

        if (availableFiles.Length == 0)
            availableFiles = audioFiles;
        
        var randomIndex = Random.Range(0, availableFiles.Length);
        var selectedAudioFile = availableFiles[randomIndex];
        var audio = File.ReadAllBytes(selectedAudioFile);
        var extension = Path.GetExtension(selectedAudioFile);
        
        _audioQueue.Enqueue(new QueuedAudio(audio, extension));
        
        var audioName = Path.GetFileNameWithoutExtension(selectedAudioFile);
        
        Logger.LogInfo($"[EnemyAudio] Playing audio: {audioName}");
        
        _recentlyPlayed.Enqueue(selectedAudioFile);
        
        var recentlyPlayedLimit = availableFiles.Length / 2;
        
        if (_recentlyPlayed.Count > recentlyPlayedLimit)
            _recentlyPlayed.Dequeue();

        TryProcessNextAudio();
    }
    
    private void SendAudioToClientsAsync(byte[] audio, string fileExtension)
    {
        var chunkAudioData = ConvertAudioIntoChunks(audio, ChunkSize);
        
        foreach (var (chunk, index) in chunkAudioData.Select((chunk, index) => (chunk, index)))
        {
            if (!PhotonNetwork.IsConnectedAndReady || photonView is null)
            {
                Logger.LogWarning("[EnemyAudio] Cannot send audio.");
                return;
            }
            
            photonView.RPC("ReceiveAudio", RpcTarget.All, chunk, index, chunkAudioData.Count, fileExtension);
        }
        
        Logger.LogInfo("[EnemyAudio] All audio chunks sent.");
        StartCoroutine(WaitForAudioToFinish());
    }
    
    [PunRPC]
    public void ReceiveAudio(byte[] audioChunk, int chunkIndex, int totalChunks, string fileExtension)
    {
        if (chunkIndex == 0)
        {
            _receivedChunks.Clear();
            _expectedChunkCount = totalChunks;
        }
        
        if (chunkIndex >= _expectedChunkCount)
            return;

        if (chunkIndex >= _receivedChunks.Count)
            _receivedChunks.AddRange(Enumerable.Repeat<byte[]>(null!, chunkIndex - _receivedChunks.Count + 1));
        
        _receivedChunks[chunkIndex] = audioChunk;

        if (_receivedChunks.Count < _expectedChunkCount || !_receivedChunks.All<byte[]>((Func<byte[], bool>) (c => c != null)))
            return;
        
        Logger.LogInfo($"[EnemyAudio] All chunks received, Playing audio ...");
        var combinedAudioChunks = CombineChunks(_receivedChunks);
        StartCoroutine(PlayReceivedAudioCoroutine(combinedAudioChunks, fileExtension));
        
        _receivedChunks.Clear();
        _expectedChunkCount = 0;
    }
    
    private IEnumerator PlayReceivedAudioCoroutine(byte[] audioData, string fileExtension)
    {
        var tempPath = Path.Combine(Application.temporaryCachePath, $"tempAudio{fileExtension}");
        File.WriteAllBytes(tempPath, audioData);

        var audioType = fileExtension.GetAudioTypeFromFileExtension();
        
        using (var www = UnityWebRequestMultimedia.GetAudioClip("file://" + tempPath, audioType))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Logger.LogError("[EnemyAudio] Failed to get the audio file: " + www.error);
                yield break;
            }
            
            var audioClip = DownloadHandlerAudioClip.GetContent(www);
            var enemies = GetAllEnemiesExceptTheGnome();
            
            
            foreach (var enemy in enemies)
            {
                var enableController = GetEnableControllerGameObject(enemy);
                
                if (enableController is null)
                    continue;
    
                var audioSource = GetAudioSource(enableController);
                ConfigureAudioSource(audioSource, audioClip);
                
                audioSource.Play();
                StartCoroutine(DestroyAudioSourceAfterDelay(audioSource, audioClip.length + 0.1f));
            }
        }
        
        Logger.LogInfo("[EnemyAudio] Audio played successfully.");
    }
    
    private void TryProcessNextAudio()
    {
        if (_isSendingAudio || _audioQueue.Count == 0)
            return;

        _isSendingAudio = true;
        var queuedAudio = _audioQueue.Dequeue();
        
        SendAudioToClientsAsync(queuedAudio.Data, queuedAudio.Extension);
    }
    
    private IEnumerator WaitForAudioToFinish()
    {
        while (_expectedChunkCount != 0)
            yield return null;

        var waitForAudioToFinish = _lastAudioDuration + DelayBetweenReproductions;
        
        yield return new WaitForSeconds(waitForAudioToFinish);

        _isSendingAudio = false;
        TryProcessNextAudio();
    }
    #endregion
    
    #region Audio Tuning and Chunking
    private static List<byte[]> ConvertAudioIntoChunks(byte[] audioData, int chunkSize)
    {
        return Enumerable.Range(0, (audioData.Length + chunkSize - 1) / chunkSize)
            .Select(i => audioData.Skip(i * chunkSize).Take(chunkSize).ToArray())
            .ToList();
    }
    
    private void ConfigureAudioSource(AudioSource audioSource, AudioClip audioClip)
    {
        audioSource.clip = audioClip;
        audioSource.volume = BasePlugin.ConfigVoiceVolume!.Value / 2;
        audioSource.spatialBlend = 1f;
        audioSource.dopplerLevel = 0.5f;
        audioSource.minDistance = 1f;
        audioSource.maxDistance = 20f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
    }
    
    private static IEnumerator DestroyAudioSourceAfterDelay(AudioSource audioSource, float delay)
    {
        yield return new WaitForSeconds(delay);
        Destroy(audioSource);
    }
    
    private static byte[] CombineChunks(List<byte[]> chunks)
    {
        byte[] destinationArray = new byte[chunks.Sum(chunk => chunk.Length)];
        int destinationIndex = 0;

        foreach (var chunk in chunks)
        {
            Array.Copy(chunk, 0, destinationArray, destinationIndex, chunk.Length);
            destinationIndex += chunk.Length;
        }

        return destinationArray;
    }
    #endregion
    
    #region Audio Directory Handling
    private void CreateEnemyAudioFolder()
    {
        _audioFolderPath = Path.Combine(Application.dataPath, _audioFolderName);
        
        Directory.CreateDirectory(_audioFolderPath);
        Logger.LogInfo($"[EnemyAudio] {_audioFolderName} folder was created.");
    }
    
    private bool EnemyAudioFolderExists()
    {
        _audioFolderPath = Path.Combine(Application.dataPath, _audioFolderName);
        return Directory.Exists(_audioFolderPath);
    }
    
    private string[] GetAudioFilesFromJsonList()
    {
        if (!EnemyAudioFolderExists())
        {
            Logger.LogError($"[EnemyAudio] {_audioFolderName} folder does not exist.");
            return [];
        }
        
        var audioFileNames = GetAllAudioFilesNamesInJson();
        
        var audioFiles = SupportedAudioFormats
            .SelectMany(format => Directory.GetFiles(_audioFolderPath!, format))
            .ToArray();
        
        var filteredAudioFiles = audioFiles
            .Where(file =>
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var extension = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                return audioFileNames.Any(a =>
                    a.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase) &&
                    a.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();
        
        return filteredAudioFiles;
    }

    private Audio[] GetAllAudioFilesNamesInJson()
    {
        _audioFolderPath = Path.Combine(Application.dataPath, _audioFolderName);
        var jsonPath = Path.Combine(_audioFolderPath, _jsonFileName);
        var json = File.ReadAllText(jsonPath);
        var audiosJson = JsonConvert.DeserializeObject<AudioJson>(json);
        
        if (audiosJson == null || audiosJson.audioList.Length == 0)
        {
            Logger.LogError("[EnemyAudio] No audio files found in the JSON file.");
            return [];
        }
        
        return audiosJson.audioList;
    }

    private void CreateAudioFilesJson()
    {
        _audioFolderPath = Path.Combine(Application.dataPath, _audioFolderName);
        
        var jsonPath = Path.Combine(_audioFolderPath, _jsonFileName);
        var jsonExists = File.Exists(jsonPath);

        if (jsonExists) 
            return;

        var audioJson = new AudioJson
        {
            audioList = new[]
            {
                new Audio { Name = "Exemple1", Extension = "mp3" },
                new Audio { Name = "Exemple2", Extension = "wav" }
            }
        };
        
        var json = JsonConvert.SerializeObject(audioJson, Formatting.Indented);
        
        File.WriteAllText(jsonPath, json);
        Logger.LogInfo($"[EnemyAudio] {_jsonFileName} was created.");
    }

    private bool JsonAudioFilesExists()
    {
        _audioFolderPath = Path.Combine(Application.dataPath, _audioFolderName);
        var jsonPath = Path.Combine(_audioFolderPath, _jsonFileName);
        
        return File.Exists(jsonPath);
    }
    
    private void CreateFoldersAndJsonFiles()
    {
        if (!EnemyAudioFolderExists())
            CreateEnemyAudioFolder();
        
        if (!JsonAudioFilesExists())
            CreateAudioFilesJson();
    }
    #endregion

    #region Getters
    private PhotonView? GetPhotonView()
    {
        var component = GetComponent<PhotonView>();
        
        if (component == null)
        {
            Logger.LogError("[EnemyAudio] PhotonView component was not found.");
            return null;
        }

        return component;
    }
    
    private List<GameObject> GetAllEnemiesExceptTheGnome()
    {
        return GameObject.Find("Level Generator")?.transform.Find("Enemies")?.Cast<Transform>()
            .Where(t => t.name != "Gnome")
            .Select(t => t.gameObject)
            .ToList() ?? [];
    }
    
    private GameObject? GetEnableControllerGameObject(GameObject enemy)
    {
        return enemy.transform.Find("Enable/Controller")?.gameObject;
    }
    
    private AudioSource GetAudioSource(GameObject enableController)
    {
        var audioSource = enableController.GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = enableController.AddComponent<AudioSource>();
        
        return audioSource;
    }
    #endregion
}