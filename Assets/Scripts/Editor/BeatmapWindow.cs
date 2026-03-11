using UnityEngine;
using UnityEditor;

public class BeatmapWindow : EditorWindow
{
    public BeatmapAsset selectedLevel;
    
    // Audio Preview
    private AudioSource previewSource;
    private GameObject previewObject;
    private float scrubPosition = 0f;
    
    // Paint Tools
    private SaberColor paintColor = SaberColor.Red;
    private CutDirection paintDirection = CutDirection.Any;

    private Vector2 scrollPos;

    [MenuItem("Beat Saber/Beatmap Editor")]
    public static void ShowWindow()
    {
        GetWindow<BeatmapWindow>("Beatmap Editor", true);
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        StopAudio();
    }

    // Helper to safely invoke internal Unity audio preview methods
    private object InvokeAudioUtilMethod(string methodName, params object[] args)
    {
        var type = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        if (type == null) return null;
        
        var methods = type.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        foreach (var m in methods)
        {
            if (m.Name == methodName && m.GetParameters().Length == args.Length)
            {
                return m.Invoke(null, args);
            }
        }
        return null; // Method not found or args length mismatch
    }

    private void OnEditorUpdate()
    {
        if (selectedLevel == null || selectedLevel.songInfo == null) return;
        
        bool isPlaying = (bool?)InvokeAudioUtilMethod("IsClipPlaying", selectedLevel.songInfo) ?? false;
        
        if (isPlaying)
        {
            float? curPos = (float?)InvokeAudioUtilMethod("GetClipPosition", selectedLevel.songInfo);
            if (curPos != null)
            {
                scrubPosition = curPos.Value;
                Repaint();
            }
        }
    }

    private void PlayAudio()
    {
        if (selectedLevel == null || selectedLevel.songInfo == null) return;
        
        if (previewObject == null)
        {
            previewObject = new GameObject("BeatmapAudioPreview");
            previewObject.hideFlags = HideFlags.HideAndDontSave;
            previewSource = previewObject.AddComponent<AudioSource>();
        }
        
        if (previewSource.clip != selectedLevel.songInfo)
        {
            previewSource.clip = selectedLevel.songInfo;
        }
        
        // This reflection call plays the audio directly through the Unity Editor backend exactly at the timeline position
        int samplePos = Mathf.RoundToInt(scrubPosition * previewSource.clip.frequency);
        
        // Try Unity 2020+ signature (Clip, StartSample, Loop)
        object result = InvokeAudioUtilMethod("PlayPreviewClip", previewSource.clip, samplePos, false);
        
        // If the 3 argument version fails, try the older 1 argument version and set position separately
        if (result == null && InvokeAudioUtilMethod("PlayPreviewClip", previewSource.clip) != null)
        {
            InvokeAudioUtilMethod("SetClipSamplePosition", previewSource.clip, samplePos);
        }
    }

    private void StopAudio()
    {
        InvokeAudioUtilMethod("StopAllPreviewClips");
        if (previewSource != null) previewSource.Pause();
    }
    
    private void OnGUI()
    {
        GUILayout.Label("BEATMAP ASSET EDITOR", EditorStyles.boldLabel);
        
        if (GUILayout.Button("＋ Create New Beatmap Level", GUILayout.Height(30)))
        {
            string path = EditorUtility.SaveFilePanelInProject("Save New Beatmap", "New Level", "asset", "Save your beatmap asset");
            if (!string.IsNullOrEmpty(path))
            {
                BeatmapAsset newMap = ScriptableObject.CreateInstance<BeatmapAsset>();
                AssetDatabase.CreateAsset(newMap, path);
                AssetDatabase.SaveAssets();
                selectedLevel = newMap;
                
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = newMap;
            }
        }
        
        GUILayout.Space(10);
        selectedLevel = (BeatmapAsset)EditorGUILayout.ObjectField("Current Level", selectedLevel, typeof(BeatmapAsset), false);

        if (selectedLevel == null)
        {
            EditorGUILayout.HelpBox("Create or select a Beatmap Asset above to start editing.", MessageType.Info);
            return;
        }

        DrawLevelSettings();
        DrawAudioTimeline();
        DrawPaintTools();
        DrawInteractiveGrid();
        DrawBlockTimeline();
    }

    private void DrawLevelSettings()
    {
        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("Level Settings", EditorStyles.boldLabel);
        
        EditorGUI.BeginChangeCheck();
        selectedLevel.levelName = EditorGUILayout.TextField("Level Name", selectedLevel.levelName);
        selectedLevel.songInfo = (AudioClip)EditorGUILayout.ObjectField("Song (AudioClip)", selectedLevel.songInfo, typeof(AudioClip), false);
        selectedLevel.beatsPerMinute = EditorGUILayout.FloatField("BPM", selectedLevel.beatsPerMinute);
        selectedLevel.songOffsetSeconds = EditorGUILayout.FloatField("Song Offset (sec)", selectedLevel.songOffsetSeconds);
        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(selectedLevel);
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawAudioTimeline()
    {
        GUILayout.Space(10);
        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("Interactive Timeline", EditorStyles.boldLabel);
        
        bool isPlaying = false;
        if (selectedLevel != null && selectedLevel.songInfo != null)
        {
            isPlaying = (bool?)InvokeAudioUtilMethod("IsClipPlaying", selectedLevel.songInfo) ?? false;
        }
        
        float clipLength = selectedLevel.songInfo != null ? selectedLevel.songInfo.length : 100f;
        
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(isPlaying ? "⏸ Pause" : "▶️ Play", GUILayout.Width(80), GUILayout.Height(25)))
        {
            if (isPlaying) StopAudio();
            else PlayAudio();
        }

        // The Scrubber
        EditorGUI.BeginChangeCheck();
        scrubPosition = GUILayout.HorizontalSlider(scrubPosition, 0f, clipLength, GUILayout.ExpandWidth(true));
        if (EditorGUI.EndChangeCheck() && !isPlaying)
        {
            // Just move the visual scrubber position, don't auto-play
        }
        
        GUILayout.Label($"{scrubPosition:F2} / {clipLength:F2}s", GUILayout.Width(80));
        GUILayout.EndHorizontal();

        // Optional spacebar shortcut to just drop a random cube quickly
        Event e = Event.current;
        if (isPlaying && e.type == EventType.KeyDown && e.keyCode == KeyCode.Space)
        {
            AddBlockAtTime(scrubPosition, GridPos.Row1_Col2);
            e.Use(); 
        }
        
        EditorGUILayout.EndVertical();
    }

    private void DrawPaintTools()
    {
        GUILayout.Space(10);
        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("1. Choose Paint Tools", EditorStyles.boldLabel);
        
        GUILayout.BeginHorizontal();
        paintColor = (SaberColor)EditorGUILayout.EnumPopup("Block Color:", paintColor);
        paintDirection = (CutDirection)EditorGUILayout.EnumPopup("Cut Direction:", paintDirection);
        GUILayout.EndHorizontal();
        
        EditorGUILayout.EndVertical();
    }

    private void DrawInteractiveGrid()
    {
        GUILayout.Space(10);
        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label($"2. Click to Paint at exactly {scrubPosition:F2}s", EditorStyles.boldLabel);
        
        // Setup the 4x3 layout
        GridPos[] gridOrder = new GridPos[] 
        {
            GridPos.Row2_Col1, GridPos.Row2_Col2, GridPos.Row2_Col3, GridPos.Row2_Col4, // Top
            GridPos.Row1_Col1, GridPos.Row1_Col2, GridPos.Row1_Col3, GridPos.Row1_Col4, // Mid
            GridPos.Row0_Col1, GridPos.Row0_Col2, GridPos.Row0_Col3, GridPos.Row0_Col4  // Bot
        };

        for (int y = 0; y < 3; y++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace(); // Center the grid
            for (int x = 0; x < 4; x++)
            {
                int index = y * 4 + x;
                GridPos pos = gridOrder[index];
                
                // Color the button based on our selected paint tool so we know what we are holding
                GUI.backgroundColor = (paintColor == SaberColor.Red) ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 0.6f, 1f);
                
                string label = $"{paintDirection.ToString()}";
                if (paintDirection == CutDirection.Any) label = "DOT";
                
                if (GUILayout.Button(label, GUILayout.Width(70), GUILayout.Height(50)))
                {
                    AddBlockAtTime(scrubPosition, pos);
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndVertical();
    }

    private void AddBlockAtTime(float spawnTime, GridPos position)
    {
        BeatData newData = new BeatData 
        { 
            spawnTime = spawnTime, 
            color = paintColor, 
            position = position, 
            direction = paintDirection 
        };
        
        selectedLevel.blocks.Add(newData);
        selectedLevel.blocks.Sort((a, b) => a.spawnTime.CompareTo(b.spawnTime));
        EditorUtility.SetDirty(selectedLevel);
    }

    private void DrawBlockTimeline()
    {
        GUILayout.Space(10);
        GUILayout.Label($"Level Blocks ({selectedLevel.blocks.Count}):", EditorStyles.boldLabel);
        
        scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.Height(300));
        
        for (int i = 0; i < selectedLevel.blocks.Count; i++)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            BeatData data = selectedLevel.blocks[i];
            
            EditorGUI.BeginChangeCheck();
            GUILayout.Label($"{i + 1}.", GUILayout.Width(20));
            GUILayout.Label("Time:", GUILayout.Width(40));
            data.spawnTime = EditorGUILayout.FloatField(data.spawnTime, GUILayout.Width(60));
            
            data.color = (SaberColor)EditorGUILayout.EnumPopup(data.color, GUILayout.Width(60));
            data.position = (GridPos)EditorGUILayout.EnumPopup(data.position, GUILayout.Width(100));
            data.direction = (CutDirection)EditorGUILayout.EnumPopup(data.direction, GUILayout.Width(100));
            
            if (EditorGUI.EndChangeCheck())
            {
                selectedLevel.blocks[i] = data; 
                EditorUtility.SetDirty(selectedLevel);
                selectedLevel.blocks.Sort((a, b) => a.spawnTime.CompareTo(b.spawnTime));
            }
            
            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("X", GUILayout.Width(30)))
            {
                selectedLevel.blocks.RemoveAt(i);
                EditorUtility.SetDirty(selectedLevel);
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
                break;
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
    }
}
