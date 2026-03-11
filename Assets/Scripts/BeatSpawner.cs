using System.Collections.Generic;
using UnityEngine;

public class BeatSpawner : MonoBehaviour
{
    [Header("Level Asset")]
    [Tooltip("Drag the Beatmap Asset you created here!")]
    public BeatmapAsset currentLevel;

    [Header("Audio Player")]
    public AudioSource beatMusic; // Drag your spawner's audio source here!

    [Header("Spawner Settings")]
    public Vector3 customSpawnOrigin = new Vector3(0, 1.5f, 20f); 
    public float cubeMoveSpeed = 5f; // Used to calculate when to SPAWN so it ARRIVES at the exact spawnTime
    
    private int nextBlockIndex = 0; // Tracks which block is next

    [Header("Cube Prefab")]
    public GameObject beatCubePrefab; 
    
    [Header("Visuals (Materials)")]
    public Material redCubeMaterial;
    public Material blueCubeMaterial;

    void Start()
    {
        if (currentLevel != null && beatMusic != null && currentLevel.songInfo != null)
        {
            beatMusic.clip = currentLevel.songInfo;
            beatMusic.spatialBlend = 0f; // Force 2D audio
            beatMusic.Play(); 
        }
    }

    void Update()
    {
        if (currentLevel == null) return;
        
        // If we ran out of blocks in the list, stop checking!
        if (nextBlockIndex >= currentLevel.blocks.Count) return;

        float currentTime = (beatMusic != null && beatMusic.isPlaying) ? beatMusic.time : Time.time;
        float adjustedTime = currentTime - currentLevel.songOffsetSeconds;

        // Calculate how many seconds early we need to spawn the block so it reaches the player exactly on the beat.
        // Time = Distance / Speed.
        float travelTimeSeconds = customSpawnOrigin.z / cubeMoveSpeed;
        
        // This is the moment the block needs to SPAWN back in the distance
        float triggerTime = currentLevel.blocks[nextBlockIndex].spawnTime - travelTimeSeconds;

        if (adjustedTime >= triggerTime)
        {
            SpawnCube(currentLevel.blocks[nextBlockIndex]);
            nextBlockIndex++; // Move to the next block in the list
        }
    }

    void SpawnCube(BeatData data)
    {
        if (beatCubePrefab == null) return;

        // 1. Convert GridPos enum to actual X/Y coordinates 
        // Beat Saber standard paths range from X: -0.9 to 0.9, and Y: 0.1 to 1.3
        Vector3 spawnOffset = Vector3.zero;
        string posName = data.position.ToString();
        
        // X Position (Columns 1 to 4)
        if (posName.Contains("Col1")) spawnOffset.x = -0.9f;
        else if (posName.Contains("Col2")) spawnOffset.x = -0.3f;
        else if (posName.Contains("Col3")) spawnOffset.x = 0.3f;
        else if (posName.Contains("Col4")) spawnOffset.x = 0.9f;
        
        // Y Position (Rows 0 to 2)
        if (posName.Contains("Row2")) spawnOffset.y = 1.3f; // Top
        else if (posName.Contains("Row1")) spawnOffset.y = 0.7f; // Middle
        else if (posName.Contains("Row0")) spawnOffset.y = 0.1f; // Bottom

        Vector3 finalSpawnPos = customSpawnOrigin + spawnOffset;

        // 2. Convert CutDirection enum to an exact Z-axis rotation angle
        // 0 = cut down. 180 = cut up. 90 = cut right. -90 = cut left.
        float zRot = 0;
        switch (data.direction)
        {
            case CutDirection.Down: zRot = 0f; break;
            case CutDirection.Up: zRot = 180f; break;
            case CutDirection.Right: zRot = 90f; break;
            case CutDirection.Left: zRot = -90f; break;
            case CutDirection.DownRight: zRot = 45f; break;
            case CutDirection.UpRight: zRot = 135f; break;
            case CutDirection.DownLeft: zRot = -45f; break;
            case CutDirection.UpLeft: zRot = -135f; break;
            case CutDirection.Any: zRot = 0f; break; // Visual only, dot block logic would go here
        }

        Quaternion rotation = Quaternion.Euler(0, 0, zRot);

        // 3. Instantiate the cube prefab!
        GameObject newCubeObj = Instantiate(beatCubePrefab, finalSpawnPos, rotation);

        // 4. Assign Color and Speed
        BeatCube cubeScript = newCubeObj.GetComponent<BeatCube>();
        MeshRenderer mr = newCubeObj.GetComponent<MeshRenderer>();

        if (cubeScript != null)
        {
            cubeScript.requiredColor = data.color;
            cubeScript.moveSpeed = cubeMoveSpeed; // Force it to use the exact speed we calculated travel time with!
            
            // If they picked "Any", we could tell the script to accept any swing angle (optional)
            if (data.direction == CutDirection.Any) cubeScript.ignoreDirectionRequirement = true;
        }

        if (mr != null)
        {
            mr.material = (data.color == SaberColor.Red) ? redCubeMaterial : blueCubeMaterial;
        }

        // Parent it to the spawner for a clean hierarchy
        newCubeObj.transform.SetParent(this.transform);
    }
}
