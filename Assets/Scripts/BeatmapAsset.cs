using System.Collections.Generic;
using UnityEngine;

// Defines grid coordinates for the 4x3 play area
public enum GridPos 
{ 
    Row2_Col1, Row2_Col2, Row2_Col3, Row2_Col4, // Top row
    Row1_Col1, Row1_Col2, Row1_Col3, Row1_Col4, // Middle row
    Row0_Col1, Row0_Col2, Row0_Col3, Row0_Col4  // Bottom row
}

// Defines the direction the user MUST swing their saber
public enum CutDirection
{
    Up, Down, Left, Right,
    UpLeft, UpRight, DownLeft, DownRight,
    Any // Acts as a 'dot' block where any angle counts
}

// Data for a single spawned cube in the level
[System.Serializable]
public struct BeatData
{
    [Tooltip("The exact second the cube reaches the player")]
    public float spawnTime; 
    public SaberColor color;
    public GridPos position;
    public CutDirection direction;
}

[CreateAssetMenu(fileName = "New Beatmap Level", menuName = "Beat Saber/Beatmap Asset", order = 1)]
public class BeatmapAsset : ScriptableObject
{
    [Header("Level Setup")]
    public string levelName = "My Custom Level";
    public AudioClip songInfo;
    public float beatsPerMinute = 130f;
    public float songOffsetSeconds = 0f;
    
    [Header("Block Data")]
    public List<BeatData> blocks = new List<BeatData>();
}
