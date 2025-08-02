namespace EnemyAudios.Models;

public struct QueuedAudio(byte[] data, string extension)
{
    public readonly byte[] Data = data;
    public readonly string Extension = extension;
}