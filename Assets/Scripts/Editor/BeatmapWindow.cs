using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class BeatmapWindow : EditorWindow
{
    public BeatmapAsset selectedLevel;
    
    // Audio Preview
    private AudioSource previewSource;
    private GameObject previewObject;
    private float scrubPosition = 0f;
    
    // Playback
    private bool isPlaying = false;
    private double lastUpdateTime;
    
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

    // ─── Playback ──────────────────────────────────────────────────────────────

    private void OnEditorUpdate()
    {
        if (isPlaying && selectedLevel != null && selectedLevel.songInfo != null)
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - lastUpdateTime);
            lastUpdateTime = now;

            scrubPosition += dt;

            if (scrubPosition >= selectedLevel.songInfo.length)
            {
                scrubPosition = selectedLevel.songInfo.length;
                StopAudio();
            }
            Repaint();
        }
    }

    private void PlayAudio()
    {
        if (selectedLevel == null || selectedLevel.songInfo == null) return;

        isPlaying = true;
        lastUpdateTime = EditorApplication.timeSinceStartup;

        if (previewObject == null)
        {
            previewObject = EditorUtility.CreateGameObjectWithHideFlags("BeatmapAudioPreview", HideFlags.HideAndDontSave);
            previewSource = previewObject.AddComponent<AudioSource>();
        }

        previewSource.clip = selectedLevel.songInfo;
        previewSource.spatialBlend = 0f;
        previewSource.time = scrubPosition;
        previewSource.playOnAwake = false;
        previewSource.loop = false;

        // Try editor preview API via reflection
        var type = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        if (type != null)
        {
            var playPreview = type.GetMethod("PlayPreviewClip", new System.Type[] { typeof(AudioClip), typeof(int), typeof(bool) });
            if (playPreview != null)
                playPreview.Invoke(null, new object[] { previewSource.clip, Mathf.RoundToInt(scrubPosition * previewSource.clip.frequency), false });
        }
    }

    private void StopAudio()
    {
        isPlaying = false;

        var type = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        if (type != null)
        {
            var stopAll = type.GetMethod("StopAllPreviewClips", new System.Type[0]);
            if (stopAll != null) stopAll.Invoke(null, null);
        }

        if (previewSource != null) previewSource.Stop();
    }


    // ─── GUI ───────────────────────────────────────────────────────────────────

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
        selectedLevel.levelName      = EditorGUILayout.TextField("Level Name", selectedLevel.levelName);
        selectedLevel.songInfo       = (AudioClip)EditorGUILayout.ObjectField("Song (AudioClip)", selectedLevel.songInfo, typeof(AudioClip), false);
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
        GUILayout.Label("Transport", EditorStyles.boldLabel);

        float clipLength = selectedLevel.songInfo != null ? selectedLevel.songInfo.length : 100f;

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(isPlaying ? "⏸ Pause" : "▶ Play", GUILayout.Width(80), GUILayout.Height(25)))
        {
            if (isPlaying) StopAudio();
            else PlayAudio();
        }

        EditorGUI.BeginChangeCheck();
        scrubPosition = GUILayout.HorizontalSlider(scrubPosition, 0f, clipLength, GUILayout.ExpandWidth(true));
        EditorGUI.EndChangeCheck();

        EditorGUI.BeginChangeCheck();
        float typedTime = EditorGUILayout.DelayedFloatField(scrubPosition, GUILayout.Width(60));
        if (EditorGUI.EndChangeCheck())
            scrubPosition = Mathf.Clamp(typedTime, 0f, clipLength);

        GUILayout.Label($"/ {clipLength:F2}s", GUILayout.Width(60));
        GUILayout.EndHorizontal();

        // Nudge buttons
        GUILayout.BeginHorizontal();
        GUILayout.Label("Nudge:", GUILayout.Width(45));
        if (GUILayout.Button("⏮ 0s",   GUILayout.Width(45))) scrubPosition = 0f;
        if (GUILayout.Button("−1s",     GUILayout.Width(35))) scrubPosition = Mathf.Max(0f, scrubPosition - 1f);
        if (GUILayout.Button("+1s",     GUILayout.Width(35))) scrubPosition = Mathf.Min(clipLength, scrubPosition + 1f);
        if (GUILayout.Button("−0.1",    GUILayout.Width(40))) scrubPosition = Mathf.Max(0f, scrubPosition - 0.1f);
        if (GUILayout.Button("+0.1",    GUILayout.Width(40))) scrubPosition = Mathf.Min(clipLength, scrubPosition + 0.1f);
        if (GUILayout.Button("−0.01",   GUILayout.Width(45))) scrubPosition = Mathf.Max(0f, scrubPosition - 0.01f);
        if (GUILayout.Button("+0.01",   GUILayout.Width(45))) scrubPosition = Mathf.Min(clipLength, scrubPosition + 0.01f);
        GUILayout.EndHorizontal();

        // Spacebar hotkey: place block at default position
        Event e = Event.current;
        if (isPlaying && e.type == EventType.KeyDown && e.keyCode == KeyCode.Space)
        {
            AddBlockAtTime(scrubPosition, GridPos.Row1_Col2);
            e.Use();
        }

        EditorGUILayout.EndVertical();
    }

    // ─── Waveform View ─────────────────────────────────────────────────────────


    // ─── Paint Tools ───────────────────────────────────────────────────────────

    private void DrawPaintTools()
    {
        GUILayout.Space(6);
        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("1. Choose Paint Tools", EditorStyles.boldLabel);

        GUILayout.BeginHorizontal();
        paintColor     = (SaberColor)EditorGUILayout.EnumPopup("Block Color:", paintColor);
        paintDirection = (CutDirection)EditorGUILayout.EnumPopup("Cut Direction:", paintDirection);
        GUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    // ─── 4×3 Grid ──────────────────────────────────────────────────────────────

    private void DrawInteractiveGrid()
    {
        GUILayout.Space(6);
        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label($"2. Click to Paint at {scrubPosition:F2}s", EditorStyles.boldLabel);

        GridPos[] gridOrder = new GridPos[]
        {
            GridPos.Row2_Col1, GridPos.Row2_Col2, GridPos.Row2_Col3, GridPos.Row2_Col4,
            GridPos.Row1_Col1, GridPos.Row1_Col2, GridPos.Row1_Col3, GridPos.Row1_Col4,
            GridPos.Row0_Col1, GridPos.Row0_Col2, GridPos.Row0_Col3, GridPos.Row0_Col4
        };

        for (int y = 0; y < 3; y++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            for (int x = 0; x < 4; x++)
            {
                GridPos pos  = gridOrder[y * 4 + x];
                GUI.backgroundColor = paintColor == SaberColor.Red ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 0.6f, 1f);
                string label = paintDirection == CutDirection.Any ? "DOT" : paintDirection.ToString();
                if (GUILayout.Button(label, GUILayout.Width(70), GUILayout.Height(50)))
                    AddBlockAtTime(scrubPosition, pos);
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
            color     = paintColor,
            position  = position,
            direction = paintDirection
        };

        selectedLevel.blocks.Add(newData);
        selectedLevel.blocks.Sort((a, b) => a.spawnTime.CompareTo(b.spawnTime));
        EditorUtility.SetDirty(selectedLevel);
    }

    // ─── Block List ────────────────────────────────────────────────────────────

    private void DrawBlockTimeline()
    {
        GUILayout.Space(10);
        GUILayout.Label($"Level Blocks ({selectedLevel.blocks.Count}):", EditorStyles.boldLabel);

        scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.Height(200));

        for (int i = 0; i < selectedLevel.blocks.Count; i++)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            BeatData data = selectedLevel.blocks[i];

            Color rowTint = data.color == SaberColor.Red ? new Color(1f, 0.85f, 0.85f) : new Color(0.85f, 0.9f, 1f);
            GUI.backgroundColor = rowTint;

            EditorGUI.BeginChangeCheck();
            GUILayout.Label($"{i + 1}.", GUILayout.Width(20));
            GUILayout.Label("t:", GUILayout.Width(15));
            float newTime = EditorGUILayout.DelayedFloatField(data.spawnTime, GUILayout.Width(55));
            bool timeChanged = EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            data.color     = (SaberColor)EditorGUILayout.EnumPopup(data.color,     GUILayout.Width(55));
            data.position  = (GridPos)EditorGUILayout.EnumPopup(data.position,     GUILayout.Width(100));
            data.direction = (CutDirection)EditorGUILayout.EnumPopup(data.direction, GUILayout.Width(90));
            bool otherChanged = EditorGUI.EndChangeCheck();

            if (timeChanged)
            {
                data.spawnTime = newTime;
                selectedLevel.blocks[i] = data;
                EditorUtility.SetDirty(selectedLevel);
                selectedLevel.blocks.Sort((a, b) => a.spawnTime.CompareTo(b.spawnTime));
            }
            else if (otherChanged)
            {
                selectedLevel.blocks[i] = data;
                EditorUtility.SetDirty(selectedLevel);
            }

            // Jump-to button
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("→", GUILayout.Width(25)))
                scrubPosition = data.spawnTime;

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", GUILayout.Width(25)))
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
