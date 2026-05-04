using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY_EDITOR
public class ArrowProjectWizard : EditorWindow
{
    [Serializable]
    public class AnalyzeRequest
    {
        public string arrow_json;
    }

    [Serializable]
    public class ChatRequest
    {
        public string session_id;
        public string user_text;
    }

    [Serializable]
    public class CommitRequest
    {
        public string session_id;
        public string display_name;
        public string project_id;
        public string description;
    }

    [Serializable]
    public class DraftProject
    {
        public string display_name;
        public string description;
    }

    [Serializable]
    public class Vector3Data
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public class PlacementSummary
    {
        public string id;
        public string display_name;
        public Vector3Data position;
        public Vector3Data forward;
        public string spawn_point_id;
        public string zone_id;
        public string[] tags;
    }

    [Serializable]
    public class PlacementPreview
    {
        public RoomObjectSummary[] room_objects;
        public PlacementSummary[] agent_placements;
        public RoomBounds room_bounds;  // populated for MLDS scenes
    }

    [Serializable]
    public class RoomObjectSummary
    {
        public string id;
        public string name;
        public Vector3Data position;
        public float radius;
        public float width;   // MLDS: actual footprint width (X)
        public float depth;   // MLDS: actual footprint depth (Z)
    }

    [Serializable]
    public class RoomBounds
    {
        public float min_x;
        public float max_x;
        public float min_z;
        public float max_z;
    }

    [Serializable]
    public class AgentSpec
    {
        public string id;
        public string display_name;
        public string persona;
        public string[] expertise;
        public string[] knowledge_tags;
        public string voice;
        public string voice_style;
        public string voice_gender;
        public string tts_model;
    }

    [Serializable]
    public class KnowledgeEntry
    {
        public string tag;
        public string name;
        public string text;
    }

    [Serializable]
    public class DraftResponse
    {
        public string analysis;
        public string assistant_message;
        public DraftProject project;
        public AgentSpec[] agents;
        public KnowledgeEntry[] knowledge;
        public PlacementPreview placement_preview;
    }

    [Serializable]
    public class AnalyzeResponse
    {
        public string session_id;
        public DraftResponse draft;
    }

    [Serializable]
    public class ChatResponse
    {
        public DraftResponse draft;
    }

    [Serializable]
    public class ProjectMetadata
    {
        public string id;
        public string display_name;
        public string description;
    }

    [Serializable]
    public class CommitResponse
    {
        public string status;
        public ProjectMetadata project;
        public PlacementSummary[] placements;
        public RoomObjectSummary[] room_objects;
        public RoomBounds room_bounds;
    }

    private class EditorCoroutine
    {
        private readonly Stack<IEnumerator> routineStack = new Stack<IEnumerator>();

        public EditorCoroutine(IEnumerator routine)
        {
            if (routine != null)
            {
                routineStack.Push(routine);
            }
        }

        public bool MoveNext()
        {
            while (routineStack.Count > 0)
            {
                var current = routineStack.Peek();
                if (!current.MoveNext())
                {
                    routineStack.Pop();
                    continue;
                }

                if (current.Current is IEnumerator nested)
                {
                    routineStack.Push(nested);
                    return true;
                }

                if (current.Current is AsyncOperation asyncOp)
                {
                    routineStack.Push(WaitForAsync(asyncOp));
                    return true;
                }

                return true;
            }
            return false;
        }

        private IEnumerator WaitForAsync(AsyncOperation op)
        {
            while (!op.isDone)
            {
                yield return null;
            }
        }
    }

    private static readonly List<EditorCoroutine> ActiveCoroutines = new List<EditorCoroutine>();

    private const string DefaultBackendUrl = "http://127.0.0.1:8787";

    [SerializeField]
    private string backendBaseUrl = DefaultBackendUrl;

    private string arrowFilePath = "";
    private string arrowJson = "";
    private string statusMessage = "";
    private string sessionId = "";
    private DraftResponse draft;

    private string chatInput = "";
    private readonly List<string> chatLog = new List<string>();
    private Vector2 scroll;
    private Vector2 chatScroll;

    private string projectDisplayName = "";
    private string projectId = "";
    private string projectDescription = "";
    private bool isAnalyzing;
    private bool isChatting;
    private bool isCommitting;
    private string committedProjectId = "";
    private Texture2D _previewTex;

    [MenuItem("Tools/MLDSI Project Wizard")]
    public static void ShowWindow()
    {
        var window = GetWindow<ArrowProjectWizard>("MLDSI Project Wizard");
        window.minSize = new Vector2(620, 620);
    }

    private void OnEnable()
    {
        EditorApplication.update += TickCoroutines;
    }

    private void OnDisable()
    {
        EditorApplication.update -= TickCoroutines;
        ActiveCoroutines.Clear();
        if (_previewTex != null) { DestroyImmediate(_previewTex); _previewTex = null; }
    }

    private static void TickCoroutines()
    {
        for (int i = ActiveCoroutines.Count - 1; i >= 0; i--)
        {
            if (!ActiveCoroutines[i].MoveNext())
            {
                ActiveCoroutines.RemoveAt(i);
            }
        }
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Backend", EditorStyles.boldLabel);
        backendBaseUrl = EditorGUILayout.TextField("Backend Base Url", backendBaseUrl);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("MLDSI-Datei", EditorStyles.boldLabel);
        DrawDropZone();

        if (!string.IsNullOrEmpty(arrowFilePath))
        {
            EditorGUILayout.LabelField("Datei", arrowFilePath);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("MLDSI analysieren", GUILayout.Height(28)))
        {
            StartAnalyze();
        }
        if (GUILayout.Button("Zurücksetzen", GUILayout.Height(28)))
        {
            ResetState();
        }
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(statusMessage))
        {
            EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
        }
        DrawLoadingIndicator();

        DrawDraft();
        DrawChat();
        DrawCommitSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawDropZone()
    {
        var dropRect = GUILayoutUtility.GetRect(0f, 60f, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, "MLDSI-JSON hierhin ziehen");

        var evt = Event.current;
        if (!dropRect.Contains(evt.mousePosition))
        {
            return;
        }

        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    var path = AssetDatabase.GetAssetPath(obj);
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }
                    if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".mldsi", StringComparison.OrdinalIgnoreCase))
                    {
                        LoadArrowFile(path);
                        break;
                    }
                }
            }
            evt.Use();
        }
    }

    private void DrawDraft()
    {
        if (draft == null)
        {
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Analyse", EditorStyles.boldLabel);
        if (!string.IsNullOrEmpty(draft.assistant_message))
        {
            EditorGUILayout.HelpBox(draft.assistant_message, MessageType.None);
        }

        if (!string.IsNullOrEmpty(draft.analysis))
        {
            var wordWrapStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            EditorGUILayout.TextArea(draft.analysis, wordWrapStyle, GUILayout.MinHeight(80));
        }

        if (draft.agents != null && draft.agents.Length > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Vorgeschlagene Agenten", EditorStyles.boldLabel);
            foreach (var agent in draft.agents)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField($"{agent.display_name} ({agent.id})", EditorStyles.wordWrappedLabel);
                if (!string.IsNullOrEmpty(agent.persona))
                {
                    EditorGUILayout.LabelField("Persona", agent.persona, EditorStyles.wordWrappedLabel);
                }
                if (agent.expertise != null && agent.expertise.Length > 0)
                {
                    EditorGUILayout.LabelField("Expertise", string.Join(", ", agent.expertise), EditorStyles.wordWrappedLabel);
                }
                if (agent.knowledge_tags != null && agent.knowledge_tags.Length > 0)
                {
                    EditorGUILayout.LabelField("Knowledge Tags", string.Join(", ", agent.knowledge_tags), EditorStyles.wordWrappedLabel);
                }
                if (!string.IsNullOrEmpty(agent.voice_gender))
                {
                    EditorGUILayout.LabelField("Stimmgeschlecht", agent.voice_gender, EditorStyles.wordWrappedLabel);
                }
                if (!string.IsNullOrEmpty(agent.voice_style))
                {
                    EditorGUILayout.LabelField("Stimmtonalität", agent.voice_style, EditorStyles.wordWrappedLabel);
                }
                if (!string.IsNullOrEmpty(agent.tts_model))
                {
                    EditorGUILayout.LabelField("TTS-Modell", agent.tts_model, EditorStyles.wordWrappedLabel);
                }
                EditorGUILayout.EndVertical();
            }
        }

        if (draft.knowledge != null && draft.knowledge.Length > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Wissenseinträge", EditorStyles.boldLabel);
            foreach (var knowledge in draft.knowledge)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField($"{knowledge.tag}/{knowledge.name}", EditorStyles.wordWrappedLabel);
                if (!string.IsNullOrEmpty(knowledge.text))
                {
                    var wordWrapStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
                    EditorGUILayout.TextArea(knowledge.text, wordWrapStyle, GUILayout.MinHeight(60));
                }
                EditorGUILayout.EndVertical();
            }
        }

        if (draft.placement_preview != null
            && draft.placement_preview.room_objects != null
            && draft.placement_preview.agent_placements != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Platzierungsvorschau", EditorStyles.boldLabel);
            DrawPlacementPreview(
                draft.placement_preview.room_objects,
                draft.placement_preview.agent_placements,
                draft.placement_preview.room_bounds
            );
        }
    }

    private void DrawChat()
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Chat", EditorStyles.boldLabel);

        chatScroll = EditorGUILayout.BeginScrollView(chatScroll, GUILayout.MinHeight(140), GUILayout.ExpandHeight(true));
        foreach (var line in chatLog)
        {
            EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        chatInput = EditorGUILayout.TextField(chatInput);
        if (GUILayout.Button("Senden", GUILayout.Width(80)))
        {
            SendChat();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawCommitSection()
    {
        if (draft == null)
        {
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Projekt erstellen", EditorStyles.boldLabel);
        projectDisplayName = EditorGUILayout.TextField("Name", projectDisplayName);
        projectId = EditorGUILayout.TextField("Projekt-ID (optional)", projectId);
        var wordWrapStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
        EditorGUILayout.LabelField("Beschreibung");
        projectDescription = EditorGUILayout.TextArea(projectDescription, wordWrapStyle, GUILayout.MinHeight(60));

        if (GUILayout.Button("Abschließen", GUILayout.Height(28)))
        {
            CommitProject();
        }
    }

    private void ResetState()
    {
        arrowFilePath = "";
        arrowJson = "";
        sessionId = "";
        draft = null;
        chatLog.Clear();
        chatInput = "";
        projectDisplayName = "";
        projectId = "";
        projectDescription = "";
        statusMessage = "";
        isAnalyzing = false;
        isChatting = false;
        isCommitting = false;
        committedProjectId = "";
    }

    private void LoadArrowFile(string assetPath)
    {
        var fullPath = Path.GetFullPath(assetPath);
        arrowFilePath = fullPath;
        arrowJson = File.ReadAllText(fullPath, Encoding.UTF8);
        statusMessage = "MLDSI geladen.";
    }

    private void StartAnalyze()
    {
        if (string.IsNullOrEmpty(arrowJson))
        {
            statusMessage = "Bitte zuerst eine MLDSI-JSON laden.";
            return;
        }

        statusMessage = "Analyse läuft...";
        isAnalyzing = true;
        var payload = new AnalyzeRequest { arrow_json = arrowJson };
        var body = JsonUtility.ToJson(payload);
        var url = backendBaseUrl.TrimEnd('/') + "/projects/arrow/analyze";
        ActiveCoroutines.Add(new EditorCoroutine(SendRequest(url, body, OnAnalyzeResponse, () => isAnalyzing = false)));
    }

    private void SendChat()
    {
        if (string.IsNullOrEmpty(chatInput))
        {
            return;
        }

        var message = chatInput;
        chatInput = "";
        chatLog.Add("Du: " + message);
        statusMessage = "Chat läuft...";
        isChatting = true;
        var payload = new ChatRequest { session_id = sessionId, user_text = message };
        var body = JsonUtility.ToJson(payload);
        var url = backendBaseUrl.TrimEnd('/') + "/projects/arrow/chat";
        ActiveCoroutines.Add(new EditorCoroutine(SendRequest(url, body, OnChatResponse, () => isChatting = false)));
    }

    private void CommitProject()
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            statusMessage = "Keine aktive Session.";
            return;
        }

        statusMessage = "Projekt wird erstellt...";
        isCommitting = true;
        var payload = new CommitRequest
        {
            session_id = sessionId,
            display_name = projectDisplayName,
            project_id = projectId,
            description = projectDescription,
        };
        var body = JsonUtility.ToJson(payload);
        var url = backendBaseUrl.TrimEnd('/') + "/projects/arrow/commit";
        ActiveCoroutines.Add(new EditorCoroutine(SendRequest(url, body, OnCommitResponse, () => isCommitting = false)));
    }

    private IEnumerator SendRequest(string url, string jsonBody, Action<string> onSuccess, Action onComplete)
    {
        using (var request = new UnityWebRequest(url, "POST"))
        {
            var bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                statusMessage = "Fehler: " + request.error;
                onComplete?.Invoke();
                yield break;
            }

            onSuccess?.Invoke(request.downloadHandler.text);
            onComplete?.Invoke();
        }
    }

    private void OnAnalyzeResponse(string json)
    {
        var response = JsonUtility.FromJson<AnalyzeResponse>(json);
        if (response == null)
        {
            statusMessage = "Antwort konnte nicht gelesen werden.";
            return;
        }

        sessionId = response.session_id;
        draft = response.draft;
        SyncDraftFields();
        RebuildPreviewTexture(draft?.placement_preview);
        statusMessage = "Analyse abgeschlossen.";
        if (!string.IsNullOrEmpty(draft?.assistant_message))
        {
            chatLog.Add("Assistent: " + draft.assistant_message);
        }
    }

    private void OnChatResponse(string json)
    {
        var response = JsonUtility.FromJson<ChatResponse>(json);
        if (response == null)
        {
            statusMessage = "Antwort konnte nicht gelesen werden.";
            return;
        }

        draft = response.draft;
        SyncDraftFields();
        RebuildPreviewTexture(draft?.placement_preview);
        statusMessage = "Chat aktualisiert.";
        if (!string.IsNullOrEmpty(draft?.assistant_message))
        {
            chatLog.Add("Assistent: " + draft.assistant_message);
        }
    }

    private void OnCommitResponse(string json)
    {
        var response = JsonUtility.FromJson<CommitResponse>(json);
        if (response == null)
        {
            statusMessage = "Antwort konnte nicht gelesen werden.";
            return;
        }

        statusMessage = response.project != null
            ? $"Projekt erstellt: {response.project.display_name} ({response.project.id})"
            : "Projekt erstellt.";
        committedProjectId = response.project != null ? response.project.id : "";
        if (draft != null && response.placements != null && response.room_objects != null)
        {
            draft.placement_preview = new PlacementPreview
            {
                room_objects = response.room_objects,
                agent_placements = response.placements,
                room_bounds = response.room_bounds,
            };
            RebuildPreviewTexture(draft.placement_preview);
        }
        EditorUtility.DisplayDialog("Projekt gespeichert", "Alles wurde gespeichert.", "OK");
    }

    private void SyncDraftFields()
    {
        NormalizeKnowledgeTags();
        if (draft?.project == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(projectDisplayName))
        {
            projectDisplayName = draft.project.display_name;
        }
        if (string.IsNullOrEmpty(projectDescription))
        {
            projectDescription = draft.project.description;
        }
    }

    private void NormalizeKnowledgeTags()
    {
        if (draft?.agents == null)
        {
            return;
        }

        var knowledgeEntries = new List<KnowledgeEntry>();
        if (draft.knowledge != null)
        {
            knowledgeEntries.AddRange(draft.knowledge);
        }

        var tagLookup = new Dictionary<string, KnowledgeEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in knowledgeEntries)
        {
            if (!string.IsNullOrEmpty(entry.tag) && !tagLookup.ContainsKey(entry.tag))
            {
                tagLookup.Add(entry.tag, entry);
            }
        }

        foreach (var agent in draft.agents)
        {
            if (!string.IsNullOrEmpty(agent.tts_model)
                && string.Equals(agent.tts_model.Trim(), "standard", StringComparison.OrdinalIgnoreCase))
            {
                agent.tts_model = "";
            }
            if (string.IsNullOrEmpty(agent.tts_model))
            {
                agent.tts_model = "gpt-4o-mini-tts";
            }
            if (agent.knowledge_tags == null)
            {
                continue;
            }

            for (int i = 0; i < agent.knowledge_tags.Length; i++)
            {
                var tag = agent.knowledge_tags[i];
                if (string.IsNullOrEmpty(tag))
                {
                    continue;
                }

                if (tagLookup.TryGetValue(tag, out var existingEntry))
                {
                    agent.knowledge_tags[i] = existingEntry.tag;
                    continue;
                }

                var newEntry = new KnowledgeEntry
                {
                    tag = tag,
                    name = tag,
                    text = ""
                };
                knowledgeEntries.Add(newEntry);
                tagLookup.Add(tag, newEntry);
                agent.knowledge_tags[i] = tag;
            }
        }

        draft.knowledge = knowledgeEntries.ToArray();
    }

    private void DrawLoadingIndicator()
    {
        if (!isAnalyzing && !isChatting && !isCommitting)
        {
            return;
        }

        var spinnerIndex = Mathf.FloorToInt((float)(EditorApplication.timeSinceStartup * 10f) % 12f);
        var spinner = EditorGUIUtility.IconContent($"WaitSpin{spinnerIndex:00}");
        if (spinner != null && spinner.image != null)
        {
            GUILayout.Label(spinner, GUILayout.Width(20), GUILayout.Height(20));
        }

        var loadingMessage = isCommitting ? "Speichert..." : "Warte auf Antwort...";
        EditorGUILayout.LabelField(loadingMessage, EditorStyles.wordWrappedLabel);
        Repaint();
    }

    // ── Texture-based floor plan preview ────────────────────────────────────

    private void RebuildPreviewTexture(PlacementPreview preview)
    {
        if (preview == null) return;

        const int W = 512, H = 512;
        if (_previewTex == null || _previewTex.width != W || _previewTex.height != H)
        {
            if (_previewTex != null) DestroyImmediate(_previewTex);
            _previewTex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp,
            };
        }

        var roomObjects = preview.room_objects;
        var placements  = preview.agent_placements;
        var roomBounds  = preview.room_bounds;

        // ── world bounds ──────────────────────────────────────────────────
        float minX, maxX, minZ, maxZ;
        if (roomBounds != null)
        {
            minX = roomBounds.min_x; maxX = roomBounds.max_x;
            minZ = roomBounds.min_z; maxZ = roomBounds.max_z;
        }
        else
        {
            minX = maxX = minZ = maxZ = 0f;
            bool any = false;
            if (roomObjects != null)
                foreach (var o in roomObjects)
                    if (o?.position != null) { GrowBounds(o.position.x, o.position.z, ref minX, ref maxX, ref minZ, ref maxZ, ref any); }
            if (placements != null)
                foreach (var p in placements)
                    if (p?.position != null) { GrowBounds(p.position.x, p.position.z, ref minX, ref maxX, ref minZ, ref maxZ, ref any); }
            if (!any) { TexClearAndApply(_previewTex, W, H, new Color32(30, 33, 35, 255)); return; }
        }

        const float pad = 0.8f;
        minX -= pad; maxX += pad; minZ -= pad; maxZ += pad;
        float spanX = Mathf.Max(0.1f, maxX - minX);
        float spanZ = Mathf.Max(0.1f, maxZ - minZ);

        // Keep aspect ratio: find the largest drawing area inside W×H
        float scale = Mathf.Min(W / spanX, H / spanZ);
        int dw = Mathf.RoundToInt(spanX * scale);
        int dh = Mathf.RoundToInt(spanZ * scale);
        int ox = (W - dw) / 2;   // left margin in pixels
        int oy = (H - dh) / 2;   // bottom margin in pixels (Tex y=0 is bottom)

        // world → texture pixel  (y=0 at bottom of texture = low-Z in world)
        (int tx, int ty) W2T(float wx, float wz) => (
            ox + Mathf.RoundToInt((wx - minX) * scale),
            oy + Mathf.RoundToInt((wz - minZ) * scale)
        );

        var pixels = new Color32[W * H];

        // Background
        var bg = new Color32(30, 33, 35, 255);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;

        // Room floor fill
        if (roomBounds != null)
        {
            var (x0, y0) = W2T(roomBounds.min_x, roomBounds.min_z);
            var (x1, y1) = W2T(roomBounds.max_x, roomBounds.max_z);
            TexFillRect(pixels, W, H, x0, y0, x1 - x0, y1 - y0, new Color32(46, 51, 56, 255));
            // Wall border (3 px)
            var wall = new Color32(165, 165, 165, 220);
            TexFillRect(pixels, W, H, x0,      y0,      x1 - x0, 3, wall); // south wall
            TexFillRect(pixels, W, H, x0,      y1 - 3,  x1 - x0, 3, wall); // north wall
            TexFillRect(pixels, W, H, x0,      y0,      3, y1 - y0, wall); // west wall
            TexFillRect(pixels, W, H, x1 - 3,  y0,      3, y1 - y0, wall); // east wall
        }

        // Furniture
        if (roomObjects != null)
        {
            var fill    = new Color32(97,  66, 40, 220);
            var outline = new Color32(148, 107, 65, 255);
            foreach (var obj in roomObjects)
            {
                if (obj?.position == null) continue;
                float fw = obj.width > 0 ? obj.width : obj.radius * 2f;
                float fd = obj.depth > 0 ? obj.depth : obj.radius * 2f;
                var (x0, y0) = W2T(obj.position.x - fw * 0.5f, obj.position.z - fd * 0.5f);
                var (x1, y1) = W2T(obj.position.x + fw * 0.5f, obj.position.z + fd * 0.5f);
                if (x1 <= x0) x1 = x0 + 1;
                if (y1 <= y0) y1 = y0 + 1;
                TexFillRect(pixels, W, H, x0, y0, x1 - x0, y1 - y0, fill);
                // 1-px outline
                TexFillRect(pixels, W, H, x0, y0,      x1 - x0, 1, outline);
                TexFillRect(pixels, W, H, x0, y1 - 1,  x1 - x0, 1, outline);
                TexFillRect(pixels, W, H, x0, y0,      1, y1 - y0, outline);
                TexFillRect(pixels, W, H, x1-1, y0,    1, y1 - y0, outline);
            }
        }

        // Agents: forward arrow + filled circle
        if (placements != null)
        {
            var agentColor  = new Color32(242, 128,  38, 255);
            var arrowColor  = new Color32(255, 230, 100, 230);
            foreach (var pl in placements)
            {
                if (pl?.position == null) continue;
                var (cx, cy) = W2T(pl.position.x, pl.position.z);

                // Forward arrow (0.8 m world units)
                if (pl.forward != null && (Mathf.Abs(pl.forward.x) > 0.01f || Mathf.Abs(pl.forward.z) > 0.01f))
                {
                    var (tx, ty) = W2T(pl.position.x + pl.forward.x * 0.8f,
                                       pl.position.z + pl.forward.z * 0.8f);
                    TexDrawLine(pixels, W, H, cx, cy, tx, ty, 2, arrowColor);
                }

                TexFillCircle(pixels, W, H, cx, cy, 5, agentColor);
            }
        }

        _previewTex.SetPixels32(pixels);
        _previewTex.Apply();
        Repaint();
    }

    private void DrawPlacementPreview(
        RoomObjectSummary[] roomObjects,
        PlacementSummary[] placements,
        RoomBounds roomBounds
    )
    {
        // Reserve layout space – texture is displayed here, fully inside the scroll view
        var rect = GUILayoutUtility.GetRect(0f, 300f, GUILayout.ExpandWidth(true));

        if (_previewTex != null)
        {
            GUI.DrawTexture(rect, _previewTex, ScaleMode.ScaleToFit, false);
        }
        else
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.13f, 0.14f));
            EditorGUI.LabelField(rect, "Keine Vorschau – Analyse ausführen.", EditorStyles.centeredGreyMiniLabel);
        }

        EditorGUILayout.LabelField(
            "■ Möbel (2D-Schnitt 0,5 m)   ● Agent   ─ Blickrichtung",
            EditorStyles.miniLabel
        );

        if (_previewTex != null && GUILayout.Button("Vorschau als PNG speichern"))
        {
            var path = EditorUtility.SaveFilePanel("Vorschau speichern", "", "placement_preview", "png");
            if (!string.IsNullOrEmpty(path))
            {
                System.IO.File.WriteAllBytes(path, _previewTex.EncodeToPNG());
                EditorUtility.RevealInFinder(path);
            }
        }
    }

    // ── Texture pixel helpers ────────────────────────────────────────────

    private static void GrowBounds(float x, float z,
        ref float minX, ref float maxX, ref float minZ, ref float maxZ, ref bool any)
    {
        if (!any) { minX = maxX = x; minZ = maxZ = z; any = true; return; }
        if (x < minX) minX = x; if (x > maxX) maxX = x;
        if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
    }

    private static void TexClearAndApply(Texture2D tex, int w, int h, Color32 color)
    {
        var pixels = new Color32[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels32(pixels);
        tex.Apply();
    }

    private static void TexFillRect(Color32[] pixels, int w, int h, int x, int y, int rw, int rh, Color32 color)
    {
        int x1 = Mathf.Clamp(x + rw, 0, w);
        int y1 = Mathf.Clamp(y + rh, 0, h);
        int x0 = Mathf.Clamp(x, 0, w);
        int y0 = Mathf.Clamp(y, 0, h);
        for (int py = y0; py < y1; py++)
            for (int px = x0; px < x1; px++)
                pixels[py * w + px] = color;
    }

    private static void TexFillCircle(Color32[] pixels, int w, int h, int cx, int cy, int r, Color32 color)
    {
        int r2 = r * r;
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (dx * dx + dy * dy > r2) continue;
            int px = cx + dx, py = cy + dy;
            if (px >= 0 && px < w && py >= 0 && py < h)
                pixels[py * w + px] = color;
        }
    }

    private static void TexDrawLine(Color32[] pixels, int w, int h, int x0, int y0, int x1, int y1, int thick, Color32 color)
    {
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy, half = thick / 2;
        for (;;)
        {
            TexFillRect(pixels, w, h, x0 - half, y0 - half, thick, thick, color);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { if (x0 == x1) break; err += dy; x0 += sx; }
            if (e2 <= dx) { if (y0 == y1) break; err += dx; y0 += sy; }
        }
    }
}
#endif
