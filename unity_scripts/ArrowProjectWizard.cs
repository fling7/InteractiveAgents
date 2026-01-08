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
        public string slice_json;
        public string room_slice_json;
    }

    [Serializable]
    public class ChatRequest
    {
        public string session_id;
        public string user_text;
        public string room_slice_json;
    }

    [Serializable]
    public class CommitRequest
    {
        public string session_id;
        public string display_name;
        public string project_id;
        public string description;
        public string room_slice_json;
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
    public class DimensionsData
    {
        public float width;
        public float height;
        public float depth;
        public float x;
        public float y;
        public float z;

        public bool TryGetVector3(out Vector3 vector)
        {
            var widthDepthMagnitude = Mathf.Abs(width) + Mathf.Abs(height) + Mathf.Abs(depth);
            var xyzMagnitude = Mathf.Abs(x) + Mathf.Abs(y) + Mathf.Abs(z);
            if (widthDepthMagnitude > 0f)
            {
                vector = new Vector3(width, height, depth);
                return true;
            }
            if (xyzMagnitude > 0f)
            {
                vector = new Vector3(x, y, z);
                return true;
            }
            vector = Vector3.zero;
            return false;
        }
    }

    [Serializable]
    public class MldsiScene
    {
        public MldsiObject[] objects;
    }

    [Serializable]
    public class MldsiDocument
    {
        public MldsiScene scene;
    }

    [Serializable]
    public class MldsiObject
    {
        public string objectId;
        public string objectType;
        public Vector3Data position;
        public DimensionsData dimensions;
        public DimensionsData size;
        public DimensionsData scale;
        public string specification;
    }

    [Serializable]
    public class SliceDocument
    {
        public float slice_height;
        public SliceObject[] objects;
    }

    [Serializable]
    public class SliceObject
    {
        public string objectId;
        public string objectType;
        public Vector3Data position;
        public DimensionsData dimensions;
        public string specification;
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
    }

    [Serializable]
    public class RoomObjectSummary
    {
        public string id;
        public string name;
        public Vector3Data position;
        public float radius;
        public DimensionsData dimensions;
        public float width;
        public float depth;
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
    private string sliceJson = "";
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
            EditorGUILayout.LabelField("Platzierungsvorschau (OpenAI)", EditorStyles.boldLabel);
            DrawPlacementPreview(
                draft.placement_preview.room_objects,
                draft.placement_preview.agent_placements,
                "Legende: Objekte = Schnitt-Kuben, Agenten = Marker"
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
        sliceJson = "";
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
        GenerateSliceFromArrowJson();
    }

    private void StartAnalyze()
    {
        if (string.IsNullOrEmpty(arrowJson))
        {
            statusMessage = "Bitte zuerst eine MLDSI-JSON laden.";
            return;
        }

        if (string.IsNullOrEmpty(sliceJson))
        {
            GenerateSliceFromArrowJson();
        }
        statusMessage = "Analyse läuft...";
        isAnalyzing = true;
        var payload = new AnalyzeRequest
        {
            arrow_json = arrowJson,
            slice_json = sliceJson,
            room_slice_json = sliceJson,
        };
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
        var payload = new ChatRequest
        {
            session_id = sessionId,
            user_text = message,
            room_slice_json = sliceJson,
        };
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
            room_slice_json = sliceJson,
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
            };
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

    private void DrawPlacementPreview(
        RoomObjectSummary[] roomObjects,
        PlacementSummary[] placements,
        string legend
    )
    {
        const float previewSize = 260f;
        var rect = GUILayoutUtility.GetRect(previewSize, previewSize, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));

        var positions = new List<Vector3Data>();
        var objectBounds = new List<Bounds>();
        if (roomObjects != null)
        {
            foreach (var obj in roomObjects)
            {
                if (obj?.position == null)
                {
                    continue;
                }

                if (TryGetRoomObjectSize(obj, out var size))
                {
                    var bounds = new Bounds(new Vector3(obj.position.x, 0f, obj.position.z), new Vector3(size.x, 0f, size.z));
                    objectBounds.Add(bounds);
                }

                positions.Add(obj.position);
            }
        }
        if (placements != null)
        {
            foreach (var placement in placements)
            {
                if (placement?.position != null)
                {
                    positions.Add(placement.position);
                }
            }
        }

        if (positions.Count == 0)
        {
            GUI.Label(rect, "Keine Platzierungsdaten verfügbar.", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minZ = float.PositiveInfinity;
        var maxZ = float.NegativeInfinity;
        if (objectBounds.Count > 0)
        {
            foreach (var bounds in objectBounds)
            {
                minX = Mathf.Min(minX, bounds.min.x);
                maxX = Mathf.Max(maxX, bounds.max.x);
                minZ = Mathf.Min(minZ, bounds.min.z);
                maxZ = Mathf.Max(maxZ, bounds.max.z);
            }
        }
        else
        {
            foreach (var pos in positions)
            {
                minX = Mathf.Min(minX, pos.x);
                maxX = Mathf.Max(maxX, pos.x);
                minZ = Mathf.Min(minZ, pos.z);
                maxZ = Mathf.Max(maxZ, pos.z);
            }
        }

        var spanX = Mathf.Max(1f, maxX - minX);
        var spanZ = Mathf.Max(1f, maxZ - minZ);
        var padding = 0.5f;
        minX -= padding;
        maxX += padding;
        minZ -= padding;
        maxZ += padding;
        spanX = maxX - minX;
        spanZ = maxZ - minZ;

        var scale = Mathf.Min(rect.width / spanX, rect.height / spanZ);

        Vector2 WorldToPreview(Vector3Data position)
        {
            var x = rect.x + (position.x - minX) * scale;
            var z = rect.y + rect.height - (position.z - minZ) * scale;
            return new Vector2(x, z);
        }

        Handles.BeginGUI();
        if (roomObjects != null)
        {
            var fillColor = new Color(0.45f, 0.55f, 0.6f, 0.35f);
            var outlineColor = new Color(0.45f, 0.55f, 0.6f, 0.9f);
            foreach (var obj in roomObjects)
            {
                if (obj?.position == null)
                {
                    continue;
                }
                if (!TryGetRoomObjectSize(obj, out var size))
                {
                    continue;
                }

                var center = WorldToPreview(obj.position);
                var halfWidth = Mathf.Max(2f, size.x * 0.5f * scale);
                var halfDepth = Mathf.Max(2f, size.z * 0.5f * scale);

                var rectPoints = new Vector3[]
                {
                    new Vector3(center.x - halfWidth, center.y - halfDepth, 0f),
                    new Vector3(center.x - halfWidth, center.y + halfDepth, 0f),
                    new Vector3(center.x + halfWidth, center.y + halfDepth, 0f),
                    new Vector3(center.x + halfWidth, center.y - halfDepth, 0f)
                };
                Handles.DrawSolidRectangleWithOutline(rectPoints, fillColor, outlineColor);
            }
        }

        if (placements != null)
        {
            Handles.color = new Color(0.95f, 0.55f, 0.2f, 0.9f);
            foreach (var placement in placements)
            {
                if (placement?.position == null)
                {
                    continue;
                }
                var center = WorldToPreview(placement.position);
                Handles.DrawSolidDisc(new Vector3(center.x, center.y, 0f), Vector3.forward, 5f);
            }
        }
        Handles.EndGUI();

        var legendRect = GUILayoutUtility.GetRect(rect.width, 18f);
        EditorGUI.LabelField(legendRect, legend, EditorStyles.miniLabel);
    }

    private bool TryGetRoomObjectSize(RoomObjectSummary obj, out Vector3 size)
    {
        size = Vector3.zero;
        if (obj == null)
        {
            return false;
        }

        if (obj.dimensions != null && obj.dimensions.TryGetVector3(out size))
        {
            return true;
        }

        if (obj.width > 0f || obj.depth > 0f)
        {
            size = new Vector3(Mathf.Max(obj.width, 0.01f), 0f, Mathf.Max(obj.depth, 0.01f));
            return true;
        }

        if (obj.radius > 0f)
        {
            var diameter = Mathf.Max(obj.radius * 2f, 0.01f);
            size = new Vector3(diameter, 0f, diameter);
            return true;
        }

        return false;
    }

    private void GenerateSliceFromArrowJson()
    {
        sliceJson = "";
        if (string.IsNullOrEmpty(arrowJson))
        {
            statusMessage = "MLDSI geladen, aber kein Inhalt gefunden.";
            return;
        }

        MldsiDocument document = null;
        try
        {
            document = JsonUtility.FromJson<MldsiDocument>(arrowJson);
        }
        catch (Exception)
        {
            statusMessage = "MLDSI konnte nicht gelesen werden.";
            return;
        }

        var slice = BuildSlice(document);
        if (slice == null)
        {
            statusMessage = "MLDSI geladen, aber kein Slice erstellt.";
            return;
        }

        sliceJson = JsonUtility.ToJson(slice, true);
        if (!string.IsNullOrEmpty(arrowFilePath))
        {
            var slicePath = Path.Combine(
                Path.GetDirectoryName(arrowFilePath) ?? "",
                Path.GetFileNameWithoutExtension(arrowFilePath) + ".slice.json"
            );
            File.WriteAllText(slicePath, sliceJson, Encoding.UTF8);
        }
        statusMessage = "MLDSI geladen und Slice gespeichert.";
    }

    private SliceDocument BuildSlice(MldsiDocument document)
    {
        var objects = document?.scene?.objects;
        if (objects == null || objects.Length == 0)
        {
            return null;
        }

        float? floorMinY = null;
        float? sceneMinY = null;

        foreach (var obj in objects)
        {
            if (obj?.position == null)
            {
                continue;
            }

            if (!TryGetDimensions(obj, out var size))
            {
                continue;
            }

            var minY = obj.position.y - size.y * 0.5f;
            sceneMinY = sceneMinY.HasValue ? Mathf.Min(sceneMinY.Value, minY) : minY;

            if (!string.IsNullOrEmpty(obj.objectType)
                && string.Equals(obj.objectType, "floor", StringComparison.OrdinalIgnoreCase))
            {
                floorMinY = floorMinY.HasValue ? Mathf.Min(floorMinY.Value, minY) : minY;
            }
        }

        if (!sceneMinY.HasValue && !floorMinY.HasValue)
        {
            return null;
        }

        var floorY = floorMinY ?? sceneMinY.Value;
        var sliceHeight = floorY + 0.05f;

        var sliceObjects = new List<SliceObject>();
        foreach (var obj in objects)
        {
            if (obj?.position == null)
            {
                continue;
            }

            if (!TryGetDimensions(obj, out var size))
            {
                continue;
            }

            var minY = obj.position.y - size.y * 0.5f;
            var maxY = obj.position.y + size.y * 0.5f;
            if (sliceHeight < minY || sliceHeight > maxY)
            {
                continue;
            }

            sliceObjects.Add(new SliceObject
            {
                objectId = obj.objectId,
                objectType = obj.objectType,
                position = obj.position,
                dimensions = new DimensionsData
                {
                    width = size.x,
                    height = size.y,
                    depth = size.z
                },
                specification = obj.specification
            });
        }

        return new SliceDocument
        {
            slice_height = sliceHeight,
            objects = sliceObjects.ToArray()
        };
    }

    private bool TryGetDimensions(MldsiObject obj, out Vector3 size)
    {
        size = Vector3.zero;
        if (obj == null)
        {
            return false;
        }

        if (obj.dimensions != null && obj.dimensions.TryGetVector3(out size))
        {
            return true;
        }

        if (obj.size != null && obj.size.TryGetVector3(out size))
        {
            return true;
        }

        if (obj.scale != null && obj.scale.TryGetVector3(out size))
        {
            return true;
        }

        return false;
    }
}
#endif
