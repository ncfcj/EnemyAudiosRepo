using UnityEngine;

namespace EnemyAudios.Extensions;

public static class AudioTypeExtensions
{
    public static AudioType GetAudioTypeFromFileExtension(this string fileExtension)
    {
        var ext = fileExtension.ToLower();

        var audioType = ext switch
        {
            "mp3" => AudioType.MPEG,
            "wav" => AudioType.WAV,
            "ogg" => AudioType.OGGVORBIS,
            _ => AudioType.UNKNOWN
        };

        return audioType;
    }
}