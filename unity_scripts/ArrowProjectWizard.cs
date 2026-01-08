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
    }

    [Serializable]
    public class RoomObjectSummary
    {
        public string id;
        public string name;
        public Vector3Data position;
        public float radius;
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
    private string sliceFilePath = "";
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
                "Legende: Blau = Objekt, Orange = Agent"
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
        sliceFilePath = "";
    }

    private void LoadArrowFile(string assetPath)
    {
        var fullPath = Path.GetFullPath(assetPath);
        arrowFilePath = fullPath;
        arrowJson = File.ReadAllText(fullPath, Encoding.UTF8);
        statusMessage = "MLDSI geladen.";
        GenerateSlice(true);
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
        GenerateSlice(false);
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

    [Serializable]
    private class SliceObject
    {
        public string objectId;
        public string objectType;
        public Vector3Data position;
        public Vector3Data dimensions;
        public string specification;
    }

    [Serializable]
    private class SliceData
    {
        public float slice_height;
        public SliceObject[] objects;
    }

    private void GenerateSlice(bool showSuccessStatus)
    {
        if (string.IsNullOrEmpty(arrowJson) || string.IsNullOrEmpty(arrowFilePath))
        {
            return;
        }

        var parsed = MiniJson.Deserialize(arrowJson);
        if (parsed == null)
        {
            statusMessage = "MLDSI konnte nicht geparst werden.";
            return;
        }

        var objects = new List<ParsedObject>();
        CollectObjects(parsed, objects);

        if (objects.Count == 0)
        {
            statusMessage = "Keine Objektinformationen für Slice gefunden.";
            return;
        }

        var floorY = FindFloorHeight(objects);
        var sliceHeight = floorY + 0.05f;

        var sliceObjects = new List<SliceObject>();
        foreach (var obj in objects)
        {
            if (!IntersectsSlice(obj, sliceHeight))
            {
                continue;
            }

            sliceObjects.Add(new SliceObject
            {
                objectId = obj.ObjectId,
                objectType = obj.ObjectType,
                position = ToVector3Data(obj.Position),
                dimensions = ToVector3Data(obj.Dimensions),
                specification = obj.Specification
            });
        }

        var sliceData = new SliceData
        {
            slice_height = sliceHeight,
            objects = sliceObjects.ToArray()
        };

        var directory = Path.GetDirectoryName(arrowFilePath);
        var filename = Path.GetFileNameWithoutExtension(arrowFilePath) + "_slice.json";
        sliceFilePath = Path.Combine(directory ?? string.Empty, filename);
        File.WriteAllText(sliceFilePath, JsonUtility.ToJson(sliceData, true), Encoding.UTF8);
        if (showSuccessStatus)
        {
            statusMessage = $"Slice gespeichert: {sliceFilePath}";
        }
    }

    private static Vector3Data ToVector3Data(Vector3 value)
    {
        return new Vector3Data { x = value.x, y = value.y, z = value.z };
    }

    private static float FindFloorHeight(List<ParsedObject> objects)
    {
        foreach (var obj in objects)
        {
            if (string.Equals(obj.ObjectType, "floor", StringComparison.OrdinalIgnoreCase))
            {
                return obj.Position.y;
            }
        }

        var minY = float.PositiveInfinity;
        foreach (var obj in objects)
        {
            minY = Mathf.Min(minY, obj.Position.y);
        }

        return float.IsPositiveInfinity(minY) ? 0f : minY;
    }

    private static bool IntersectsSlice(ParsedObject obj, float sliceHeight)
    {
        var halfHeight = Mathf.Abs(obj.Dimensions.y) * 0.5f;
        var minY = obj.Position.y - halfHeight;
        var maxY = obj.Position.y + halfHeight;
        if (halfHeight <= 0f)
        {
            minY = obj.Position.y;
            maxY = obj.Position.y;
        }

        return minY <= sliceHeight && maxY >= sliceHeight;
    }

    private class ParsedObject
    {
        public string ObjectId { get; }
        public string ObjectType { get; }
        public Vector3 Position { get; }
        public Vector3 Dimensions { get; }
        public string Specification { get; }

        public ParsedObject(string objectId, string objectType, Vector3 position, Vector3 dimensions, string specification)
        {
            ObjectId = objectId;
            ObjectType = objectType;
            Position = position;
            Dimensions = dimensions;
            Specification = specification;
        }
    }

    private static void CollectObjects(object node, List<ParsedObject> results)
    {
        if (node is Dictionary<string, object> dict)
        {
            if (TryParseObject(dict, out var parsed))
            {
                results.Add(parsed);
            }

            foreach (var value in dict.Values)
            {
                CollectObjects(value, results);
            }
            return;
        }

        if (node is List<object> list)
        {
            foreach (var item in list)
            {
                CollectObjects(item, results);
            }
        }
    }

    private static bool TryParseObject(Dictionary<string, object> dict, out ParsedObject parsed)
    {
        parsed = null;
        if (!TryGetVector3(dict, new[] { "position" }, new[] { "x" }, new[] { "y" }, new[] { "z" }, out var position))
        {
            return false;
        }

        Vector3 dimensions = Vector3.zero;
        if (TryGetValue(dict, "dimensions", out var dimensionsObj)
            || TryGetValue(dict, "size", out dimensionsObj))
        {
            TryParseVector3(dimensionsObj, new[] { "w", "width", "x" }, new[] { "h", "height", "y" }, new[] { "d", "depth", "z" }, out dimensions);
        }

        var objectId = GetString(dict, "objectId", "object_id", "id");
        var objectType = GetString(dict, "objectType", "type");
        var specification = GetString(dict, "specification", "description");

        parsed = new ParsedObject(
            string.IsNullOrEmpty(objectId) ? Guid.NewGuid().ToString("N") : objectId,
            string.IsNullOrEmpty(objectType) ? "unknown" : objectType,
            position,
            dimensions,
            specification ?? string.Empty
        );
        return true;
    }

    private static bool TryGetVector3(
        Dictionary<string, object> dict,
        string[] containerKeys,
        string[] xKeys,
        string[] yKeys,
        string[] zKeys,
        out Vector3 value)
    {
        value = Vector3.zero;
        foreach (var key in containerKeys)
        {
            if (!TryGetValue(dict, key, out var container))
            {
                continue;
            }

            if (TryParseVector3(container, xKeys, yKeys, zKeys, out value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseVector3(object value, string[] xKeys, string[] yKeys, string[] zKeys, out Vector3 vector)
    {
        vector = Vector3.zero;
        if (value is Dictionary<string, object> dict)
        {
            vector = new Vector3(
                GetFloat(dict, xKeys, 0f),
                GetFloat(dict, yKeys, 0f),
                GetFloat(dict, zKeys, 0f)
            );
            return true;
        }

        return false;
    }

    private static bool TryGetValue(Dictionary<string, object> dict, string key, out object value)
    {
        value = null;
        return dict != null && dict.TryGetValue(key, out value);
    }

    private static string GetString(Dictionary<string, object> dict, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (dict != null && dict.TryGetValue(key, out var value) && value is string text)
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static float GetFloat(Dictionary<string, object> dict, string[] keys, float fallback)
    {
        foreach (var key in keys)
        {
            if (dict != null && dict.TryGetValue(key, out var value) && TryConvertToFloat(value, out var number))
            {
                return number;
            }
        }

        return fallback;
    }

    private static bool TryConvertToFloat(object value, out float number)
    {
        switch (value)
        {
            case float f:
                number = f;
                return true;
            case double d:
                number = (float)d;
                return true;
            case int i:
                number = i;
                return true;
            case long l:
                number = l;
                return true;
            case string s when float.TryParse(s, out var parsed):
                number = parsed;
                return true;
            default:
                number = 0f;
                return false;
        }
    }

    private static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            return Parser.Parse(json);
        }

        private sealed class Parser : IDisposable
        {
            private readonly StringReader reader;

            private Parser(string jsonString)
            {
                reader = new StringReader(jsonString);
            }

            public static object Parse(string jsonString)
            {
                using (var instance = new Parser(jsonString))
                {
                    return instance.ParseValue();
                }
            }

            public void Dispose()
            {
                reader.Dispose();
            }

            private Dictionary<string, object> ParseObject()
            {
                var table = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                reader.Read();

                while (true)
                {
                    switch (NextToken)
                    {
                        case Token.None:
                            return null;
                        case Token.CurlyClose:
                            reader.Read();
                            return table;
                        default:
                            var name = ParseString();
                            if (name == null)
                            {
                                return null;
                            }

                            if (NextToken != Token.Colon)
                            {
                                return null;
                            }

                            reader.Read();
                            table[name] = ParseValue();
                            break;
                    }
                }
            }

            private List<object> ParseArray()
            {
                var array = new List<object>();

                reader.Read();

                var parsing = true;
                while (parsing)
                {
                    var token = NextToken;

                    switch (token)
                    {
                        case Token.None:
                            return null;
                        case Token.SquaredClose:
                            reader.Read();
                            parsing = false;
                            break;
                        default:
                            array.Add(ParseValue());
                            break;
                    }
                }

                return array;
            }

            private object ParseValue()
            {
                switch (NextToken)
                {
                    case Token.String:
                        return ParseString();
                    case Token.Number:
                        return ParseNumber();
                    case Token.CurlyOpen:
                        return ParseObject();
                    case Token.SquaredOpen:
                        return ParseArray();
                    case Token.True:
                        reader.Read();
                        return true;
                    case Token.False:
                        reader.Read();
                        return false;
                    case Token.Null:
                        reader.Read();
                        return null;
                    default:
                        return null;
                }
            }

            private string ParseString()
            {
                var builder = new StringBuilder();
                reader.Read();

                var parsing = true;
                while (parsing)
                {
                    if (reader.Peek() == -1)
                    {
                        break;
                    }

                    var c = NextChar;
                    switch (c)
                    {
                        case '"':
                            parsing = false;
                            break;
                        case '\\':
                            if (reader.Peek() == -1)
                            {
                                parsing = false;
                                break;
                            }

                            var escaped = NextChar;
                            switch (escaped)
                            {
                                case '"':
                                case '\\':
                                case '/':
                                    builder.Append(escaped);
                                    break;
                                case 'b':
                                    builder.Append('\b');
                                    break;
                                case 'f':
                                    builder.Append('\f');
                                    break;
                                case 'n':
                                    builder.Append('\n');
                                    break;
                                case 'r':
                                    builder.Append('\r');
                                    break;
                                case 't':
                                    builder.Append('\t');
                                    break;
                                case 'u':
                                    var unicode = new char[4];
                                    for (var i = 0; i < 4; i++)
                                    {
                                        unicode[i] = NextChar;
                                    }
                                    builder.Append((char)Convert.ToInt32(new string(unicode), 16));
                                    break;
                            }
                            break;
                        default:
                            builder.Append(c);
                            break;
                    }
                }

                return builder.ToString();
            }

            private object ParseNumber()
            {
                var number = NextWord;
                if (number.IndexOf('.') != -1 || number.IndexOf('e') != -1 || number.IndexOf('E') != -1)
                {
                    if (double.TryParse(number, out var parsedDouble))
                    {
                        return parsedDouble;
                    }
                }

                if (long.TryParse(number, out var parsedLong))
                {
                    return parsedLong;
                }

                return 0;
            }

            private void EatWhitespace()
            {
                while (char.IsWhiteSpace(PeekChar))
                {
                    reader.Read();
                    if (reader.Peek() == -1)
                    {
                        break;
                    }
                }
            }

            private char PeekChar => Convert.ToChar(reader.Peek());

            private char NextChar => Convert.ToChar(reader.Read());

            private string NextWord
            {
                get
                {
                    var builder = new StringBuilder();
                    while (!IsWordBreak(PeekChar))
                    {
                        builder.Append(NextChar);
                        if (reader.Peek() == -1)
                        {
                            break;
                        }
                    }
                    return builder.ToString();
                }
            }

            private Token NextToken
            {
                get
                {
                    EatWhitespace();
                    if (reader.Peek() == -1)
                    {
                        return Token.None;
                    }

                    switch (PeekChar)
                    {
                        case '{':
                            return Token.CurlyOpen;
                        case '}':
                            return Token.CurlyClose;
                        case '[':
                            return Token.SquaredOpen;
                        case ']':
                            return Token.SquaredClose;
                        case ',':
                            reader.Read();
                            return NextToken;
                        case '"':
                            return Token.String;
                        case ':':
                            return Token.Colon;
                        case '0':
                        case '1':
                        case '2':
                        case '3':
                        case '4':
                        case '5':
                        case '6':
                        case '7':
                        case '8':
                        case '9':
                        case '-':
                            return Token.Number;
                    }

                    var word = NextWord;
                    switch (word)
                    {
                        case "false":
                            return Token.False;
                        case "true":
                            return Token.True;
                        case "null":
                            return Token.Null;
                    }

                    return Token.None;
                }
            }

            private static bool IsWordBreak(char c)
            {
                return char.IsWhiteSpace(c) || c == ',' || c == ':' || c == ']' || c == '}';
            }

            private enum Token
            {
                None,
                CurlyOpen,
                CurlyClose,
                SquaredOpen,
                SquaredClose,
                Colon,
                String,
                Number,
                True,
                False,
                Null
            }
        }
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
        if (roomObjects != null)
        {
            foreach (var obj in roomObjects)
            {
                if (obj?.position != null)
                {
                    positions.Add(obj.position);
                }
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
        foreach (var pos in positions)
        {
            minX = Mathf.Min(minX, pos.x);
            maxX = Mathf.Max(maxX, pos.x);
            minZ = Mathf.Min(minZ, pos.z);
            maxZ = Mathf.Max(maxZ, pos.z);
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
            Handles.color = new Color(0.45f, 0.55f, 0.6f, 0.7f);
            foreach (var obj in roomObjects)
            {
                if (obj?.position == null)
                {
                    continue;
                }
                var center = WorldToPreview(obj.position);
                var radius = Mathf.Max(4f, obj.radius * scale);
                Handles.DrawSolidDisc(new Vector3(center.x, center.y, 0f), Vector3.forward, radius);
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
}
#endif
