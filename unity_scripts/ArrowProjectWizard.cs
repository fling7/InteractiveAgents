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
            EditorGUILayout.TextArea(draft.analysis, GUILayout.MinHeight(80));
        }

        if (draft.agents != null && draft.agents.Length > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Vorgeschlagene Agenten", EditorStyles.boldLabel);
            foreach (var agent in draft.agents)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField($"{agent.display_name} ({agent.id})");
                if (!string.IsNullOrEmpty(agent.persona))
                {
                    EditorGUILayout.LabelField("Persona", agent.persona);
                }
                if (agent.expertise != null && agent.expertise.Length > 0)
                {
                    EditorGUILayout.LabelField("Expertise", string.Join(", ", agent.expertise));
                }
                if (agent.knowledge_tags != null && agent.knowledge_tags.Length > 0)
                {
                    EditorGUILayout.LabelField("Knowledge Tags", string.Join(", ", agent.knowledge_tags));
                }
                if (!string.IsNullOrEmpty(agent.voice))
                {
                    EditorGUILayout.LabelField("Stimme", agent.voice);
                }
                if (!string.IsNullOrEmpty(agent.voice_gender))
                {
                    EditorGUILayout.LabelField("Stimmgeschlecht", agent.voice_gender);
                }
                if (!string.IsNullOrEmpty(agent.voice_style))
                {
                    EditorGUILayout.LabelField("Stimmtonalität", agent.voice_style);
                }
                if (!string.IsNullOrEmpty(agent.tts_model))
                {
                    EditorGUILayout.LabelField("TTS-Modell", agent.tts_model);
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
                EditorGUILayout.LabelField($"{knowledge.tag}/{knowledge.name}");
                if (!string.IsNullOrEmpty(knowledge.text))
                {
                    EditorGUILayout.TextArea(knowledge.text, GUILayout.MinHeight(60));
                }
                EditorGUILayout.EndVertical();
            }
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

        chatScroll = EditorGUILayout.BeginScrollView(chatScroll, GUILayout.MinHeight(140));
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
        projectDescription = EditorGUILayout.TextField("Beschreibung", projectDescription);

        if (GUILayout.Button("Abschließen", GUILayout.Height(28)))
        {
            CommitProject();
        }
    }

    private void ResetState()
    {
        sessionId = "";
        draft = null;
        chatLog.Clear();
        chatInput = "";
        projectDisplayName = "";
        projectId = "";
        projectDescription = "";
        statusMessage = "";
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
        var payload = new AnalyzeRequest { arrow_json = arrowJson };
        var body = JsonUtility.ToJson(payload);
        var url = backendBaseUrl.TrimEnd('/') + "/projects/arrow/analyze";
        ActiveCoroutines.Add(new EditorCoroutine(SendRequest(url, body, OnAnalyzeResponse)));
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
        var payload = new ChatRequest { session_id = sessionId, user_text = message };
        var body = JsonUtility.ToJson(payload);
        var url = backendBaseUrl.TrimEnd('/') + "/projects/arrow/chat";
        ActiveCoroutines.Add(new EditorCoroutine(SendRequest(url, body, OnChatResponse)));
    }

    private void CommitProject()
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            statusMessage = "Keine aktive Session.";
            return;
        }

        statusMessage = "Projekt wird erstellt...";
        var payload = new CommitRequest
        {
            session_id = sessionId,
            display_name = projectDisplayName,
            project_id = projectId,
            description = projectDescription,
        };
        var body = JsonUtility.ToJson(payload);
        var url = backendBaseUrl.TrimEnd('/') + "/projects/arrow/commit";
        ActiveCoroutines.Add(new EditorCoroutine(SendRequest(url, body, OnCommitResponse)));
    }

    private IEnumerator SendRequest(string url, string jsonBody, Action<string> onSuccess)
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
                yield break;
            }

            onSuccess?.Invoke(request.downloadHandler.text);
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
    }

    private void SyncDraftFields()
    {
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
}
#endif
