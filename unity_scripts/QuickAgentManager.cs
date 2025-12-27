using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.Networking;

public class QuickAgentManager : MonoBehaviour
{
    [Header("Backend")]
    public string backendBaseUrl = "http://127.0.0.1:8787";
    public string roomPlanPath = "examples/room_plan.example.json";
    public string agentsPath = "examples/agents.example.json";

    [Header("Spawn")]
    public Vector3 spawnArea = new Vector3(12f, 0f, 12f);
    public float spawnHeight = 0.5f;
    public Vector2 boxScaleRange = new Vector2(0.8f, 1.2f);

    [Header("UI")]
    public bool showUi = true;
    public Rect uiRect = new Rect(10, 10, 420, 520);

    [Header("Agent Visuals")]
    public Color activeAgentColor = new Color(1f, 0.85f, 0.2f);
    public float activeAgentEmission = 0.6f;
    public float bubbleHeight = 0.6f;
    public float bubbleDuration = 15f;
    public float bubbleStagger = 5f;
    public float handoffDelay = 5f;
    public float handoffIndicatorDuration = 5f;
    public float handoffLineWidth = 0.06f;

    [Header("Camera Movement")]
    public bool enableFreeMovement = true;
    public float cameraMoveSpeed = 4f;
    public float cameraBoostMultiplier = 2f;
    public float cameraLookSpeed = 2f;
    public float cameraLookClamp = 80f;

    [Serializable]
    public class Vector3Data { public float x; public float y; public float z; }

    [Serializable]
    public class SetupRequestPaths
    {
        public string room_plan_path;
        public string agents_path;
        public string session_id;
        public string project_id;
    }

    [Serializable]
    public class ProjectSummary
    {
        public string id;
        public string display_name;
        public string description;
    }

    [Serializable]
    public class ProjectListResponse
    {
        public ProjectSummary[] projects;
    }

    [Serializable]
    public class AgentPlacement
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
    public class SetupResponse
    {
        public string session_id;
        public AgentPlacement[] agents;
    }

    [Serializable]
    public class ChatRequest
    {
        public string session_id;
        public string active_agent_id;
        public string user_text;
    }

    [Serializable]
    public class ChatEvent
    {
        public string type;
        public string agent_id;
        public string text;
    }

    [Serializable]
    public class Handoff
    {
        public string from;
        public string to;
        public string reason;
    }

    [Serializable]
    public class ChatResponse
    {
        public string session_id;
        public string active_agent_id;
        public Handoff handoff;
        public ChatEvent[] events;
    }

    [Header("Runtime")]
    public string sessionId;
    public string activeAgentId;

    private class AgentVisual
    {
        public GameObject obj;
        public Renderer renderer;
        public Color baseColor;
        public float scale;
    }

    private class BubbleInfo
    {
        public string text;
        public float expiresAt;
    }

    private readonly Dictionary<string, AgentVisual> agentObjects = new Dictionary<string, AgentVisual>();
    private readonly Dictionary<string, BubbleInfo> agentBubbles = new Dictionary<string, BubbleInfo>();
    private readonly List<string> chatLog = new List<string>();
    private AgentPlacement[] lastAgents;
    private string statusMessage = "";
    private string chatInput = "";
    private const string ChatInputControlName = "chatInputField";
    private bool isChatInputFocused = false;
    private Vector2 agentScroll;
    private Vector2 chatScroll;
    private Vector2 uiScroll;
    private Vector2 projectScroll;
    private bool useProjectSelection = true;
    private ProjectSummary[] projects = Array.Empty<ProjectSummary>();
    private int selectedProjectIndex = -1;
    private string selectedProjectId = "";
    private GUIStyle bubbleStyle;
    private GUIStyle bubblePointerStyle;
    private LineRenderer handoffLine;
    private float handoffLineExpiresAt;
    private string handoffFromId;
    private string handoffToId;
    private float cameraYaw;
    private float cameraPitch;
    private bool cameraInitialized;

    private void Start()
    {
        EnsureSceneBasics();
        StartCoroutine(SetupFromServer());
    }

    private void Update()
    {
        UpdateFreeMovement();

        if (TryGetSelectPosition(out var screenPosition))
        {
            TrySelectAgentFromClick(screenPosition);
        }

        CleanupExpiredBubbles();
        UpdateHandoffLine();
    }

    private void EnsureSceneBasics()
    {
        if (Camera.main == null)
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 12f, -12f);
            camera.transform.rotation = Quaternion.Euler(35f, 0f, 0f);
        }

        if (FindObjectOfType<Light>() == null)
        {
            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
    }

    private IEnumerator SetupFromServer()
    {
        if (useProjectSelection && string.IsNullOrWhiteSpace(selectedProjectId))
        {
            statusMessage = "Kein Projekt ausgewählt.";
            yield break;
        }

        statusMessage = "Setup läuft...";
        var url = $"{backendBaseUrl}/setup";
        var payload = new SetupRequestPaths
        {
            room_plan_path = useProjectSelection ? null : roomPlanPath,
            agents_path = useProjectSelection ? null : agentsPath,
            project_id = useProjectSelection ? selectedProjectId : null
        };
        var json = JsonUtility.ToJson(payload);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            var body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                statusMessage = "Setup fehlgeschlagen: " + req.error;
                chatLog.Add(statusMessage + " | " + req.downloadHandler.text);
                yield break;
            }

            var resp = JsonUtility.FromJson<SetupResponse>(req.downloadHandler.text);
            sessionId = resp.session_id;
            lastAgents = resp.agents ?? Array.Empty<AgentPlacement>();
            statusMessage = $"Setup OK. Agents: {lastAgents.Length}";
            SpawnAgents(lastAgents);
            if (lastAgents.Length > 0)
            {
                SetActiveAgentId(lastAgents[0].id);
            }

            if (useProjectSelection)
            {
                statusMessage = $"Setup OK. Projekt: {selectedProjectId} | Agents: {lastAgents.Length}";
            }
        }
    }

    private IEnumerator RefreshProjects()
    {
        statusMessage = "Projekte laden...";
        var url = $"{backendBaseUrl}/projects";
        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                statusMessage = "Projektliste fehlgeschlagen: " + req.error;
                yield break;
            }

            var resp = JsonUtility.FromJson<ProjectListResponse>(req.downloadHandler.text);
            projects = resp?.projects ?? Array.Empty<ProjectSummary>();
            UpdateProjectSelection();
            statusMessage = $"Projekte geladen: {projects.Length}";
        }
    }

    private void UpdateProjectSelection()
    {
        if (projects.Length == 0)
        {
            selectedProjectIndex = -1;
            selectedProjectId = "";
            return;
        }

        if (!string.IsNullOrWhiteSpace(selectedProjectId))
        {
            for (var i = 0; i < projects.Length; i++)
            {
                if (projects[i].id == selectedProjectId)
                {
                    selectedProjectIndex = i;
                    return;
                }
            }
        }

        selectedProjectIndex = 0;
        selectedProjectId = projects[0].id;
    }

    private void SpawnAgents(AgentPlacement[] agents)
    {
        foreach (var entry in agentObjects)
        {
            if (entry.Value != null && entry.Value.obj != null)
            {
                Destroy(entry.Value.obj);
            }
        }
        agentObjects.Clear();

        for (var i = 0; i < agents.Length; i++)
        {
            var agent = agents[i];
            var id = string.IsNullOrEmpty(agent.id) ? $"agent_{i + 1}" : agent.id;
            var displayName = string.IsNullOrEmpty(agent.display_name) ? id : agent.display_name;

            var pos = new Vector3(
                UnityEngine.Random.Range(-spawnArea.x * 0.5f, spawnArea.x * 0.5f),
                spawnHeight,
                UnityEngine.Random.Range(-spawnArea.z * 0.5f, spawnArea.z * 0.5f)
            );

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"Agent_{displayName}";
            cube.transform.position = pos;
            var scale = UnityEngine.Random.Range(boxScaleRange.x, boxScaleRange.y);
            cube.transform.localScale = Vector3.one * scale;

            var renderer = cube.GetComponent<Renderer>();
            var baseColor = Color.white;
            if (renderer != null)
            {
                baseColor = Color.Lerp(new Color(0.3f, 0.6f, 1f), Color.white, 0.2f * i);
                renderer.material.color = baseColor;
            }

            agentObjects[id] = new AgentVisual
            {
                obj = cube,
                renderer = renderer,
                baseColor = baseColor,
                scale = scale
            };
        }

        UpdateAgentHighlights();
    }

    private bool TryGetSelectPosition(out Vector2 screenPosition)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPosition = Mouse.current.position.ReadValue();
            return true;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
        {
            screenPosition = Input.mousePosition;
            return true;
        }
#endif

        screenPosition = default;
        return false;
    }

    private void TrySelectAgentFromClick(Vector2 screenPosition)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        var ray = cam.ScreenPointToRay(screenPosition);
        if (Physics.Raycast(ray, out var hit))
        {
            foreach (var pair in agentObjects)
            {
                if (pair.Value != null && pair.Value.obj == hit.collider.gameObject)
                {
                    SetActiveAgentId(pair.Key, true);
                    return;
                }
            }
        }
    }

    private IEnumerator SendChat(string message)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            statusMessage = "Kein sessionId. Setup zuerst ausführen.";
            yield break;
        }

        if (string.IsNullOrEmpty(activeAgentId))
        {
            statusMessage = "Kein aktiver Agent ausgewählt.";
            yield break;
        }

        var url = $"{backendBaseUrl}/chat";
        var payload = new ChatRequest
        {
            session_id = sessionId,
            active_agent_id = activeAgentId,
            user_text = message
        };
        var json = JsonUtility.ToJson(payload);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            var body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                statusMessage = "Chat fehlgeschlagen: " + req.error;
                chatLog.Add(statusMessage + " | " + req.downloadHandler.text);
                yield break;
            }

            var resp = JsonUtility.FromJson<ChatResponse>(req.downloadHandler.text);
            sessionId = resp.session_id;
            SetActiveAgentId(resp.active_agent_id);
            AppendChatEvents(resp.events);
            StartCoroutine(ShowChatBubbles(resp));
        }
    }

    private void AppendChatEvents(ChatEvent[] events)
    {
        if (events == null)
        {
            return;
        }

        foreach (var ev in events)
        {
            var agentLabel = string.IsNullOrWhiteSpace(ev.agent_id) ? "System" : ev.agent_id;
            if (!string.IsNullOrWhiteSpace(ev.type))
            {
                agentLabel = $"{agentLabel}/{ev.type}";
            }

            var text = NormalizeChatText(ev.text);
            chatLog.Add($"[{agentLabel}] {text}");
        }
    }

    private string NormalizeChatText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var normalized = text.Replace("\\n", "\n").Trim();
        if (normalized.StartsWith("{") && normalized.EndsWith("}"))
        {
            var extracted = TryExtractSayFromJson(normalized);
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                return extracted.Trim();
            }

            extracted = TryExtractSayFromLooseJson(normalized);
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                return extracted.Trim();
            }
        }

        var jsonStart = normalized.IndexOf('{');
        var jsonEnd = normalized.LastIndexOf('}');
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            var jsonCandidate = normalized.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var extracted = TryExtractSayFromJson(jsonCandidate);
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                return extracted.Trim();
            }

            extracted = TryExtractSayFromLooseJson(jsonCandidate);
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                return extracted.Trim();
            }
        }

        return normalized;
    }

    private string TryExtractSayFromJson(string json)
    {
        try
        {
            var parsed = JsonUtility.FromJson<StructuredNpcReply>(json);
            if (parsed != null && !string.IsNullOrWhiteSpace(parsed.say))
            {
                return parsed.say;
            }
            if (parsed != null)
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(parsed.antwort))
                {
                    parts.Add(parsed.antwort.Trim());
                }
                if (!string.IsNullOrWhiteSpace(parsed.rueckfrage))
                {
                    var followUp = parsed.rueckfrage.Trim();
                    if (!string.IsNullOrWhiteSpace(followUp))
                    {
                        parts.Add($"Rückfrage: {followUp}");
                    }
                }
                if (parts.Count > 0)
                {
                    return string.Join("\n\n", parts);
                }
            }
        }
        catch
        {
            // Ignore JSON parse failures and fall back to raw text.
        }

        return null;
    }

    private string TryExtractSayFromLooseJson(string jsonLike)
    {
        if (string.IsNullOrWhiteSpace(jsonLike))
        {
            return null;
        }

        var antwort = ExtractLooseField(jsonLike, "antwort");
        var rueckfrage = ExtractLooseField(jsonLike, "rueckfrage");
        if (string.IsNullOrWhiteSpace(antwort) && string.IsNullOrWhiteSpace(rueckfrage))
        {
            return null;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(antwort))
        {
            parts.Add(antwort.Trim());
        }

        if (!string.IsNullOrWhiteSpace(rueckfrage))
        {
            parts.Add($"Rückfrage: {rueckfrage.Trim()}");
        }

        return string.Join("\n\n", parts);
    }

    private string ExtractLooseField(string jsonLike, string field)
    {
        var pattern = $"\\\"{Regex.Escape(field)}\\\"\\s*:\\s*\\\"(?<value>[\\s\\S]*?)\\\"";
        var match = Regex.Match(jsonLike, pattern, RegexOptions.Singleline);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["value"].Value;
        return value.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\"", "\"");
    }

    private void OnGUI()
    {
        DrawAgentBubbles();
        if (!showUi)
        {
            isChatInputFocused = false;
            return;
        }

        var maxWidth = Mathf.Min(uiRect.width, Screen.width - uiRect.x - 10f);
        var maxHeight = Mathf.Min(uiRect.height, Screen.height - uiRect.y - 10f);
        var clampedRect = new Rect(uiRect.x, uiRect.y, maxWidth, maxHeight);

        if (Event.current.type == EventType.MouseDown && !clampedRect.Contains(Event.current.mousePosition))
        {
            GUI.FocusControl(string.Empty);
            isChatInputFocused = false;
        }

        GUILayout.BeginArea(clampedRect, GUI.skin.box);
        uiScroll = GUILayout.BeginScrollView(uiScroll);
        GUILayout.Label("Quick Agent Manager");
        GUILayout.Space(4);

        GUILayout.Label($"Status: {statusMessage}");
        GUILayout.Label($"Session: {sessionId}");
        GUILayout.Label($"Aktiv: {activeAgentId}");

        GUILayout.Space(6);
        GUILayout.Label("Projekt auswählen:");
        var sourceIndex = useProjectSelection ? 0 : 1;
        sourceIndex = GUILayout.Toolbar(sourceIndex, new[] { "Projekt", "Pfade" });
        useProjectSelection = sourceIndex == 0;

        if (useProjectSelection)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Projektliste laden"))
            {
                StartCoroutine(RefreshProjects());
            }
            GUILayout.EndHorizontal();

            if (projects.Length == 0)
            {
                GUILayout.Label("Keine Projekte geladen.");
            }
            else
            {
                projectScroll = GUILayout.BeginScrollView(projectScroll, GUILayout.Height(140f));
                for (var i = 0; i < projects.Length; i++)
                {
                    var project = projects[i];
                    var label = $"{project.display_name} ({project.id})";
                    var isSelected = i == selectedProjectIndex;
                    var previousColor = GUI.backgroundColor;
                    if (isSelected)
                    {
                        GUI.backgroundColor = new Color(0.35f, 0.7f, 1f, 1f);
                    }

                    if (GUILayout.Button(label))
                    {
                        selectedProjectIndex = i;
                        selectedProjectId = project.id;
                    }

                    GUI.backgroundColor = previousColor;
                }
                GUILayout.EndScrollView();

                if (selectedProjectIndex >= 0)
                {
                    var selected = projects[selectedProjectIndex];
                    GUILayout.Label($"Aktuelles Projekt: {selected.display_name} ({selected.id})");
                }
            }
        }
        else
        {
            GUILayout.Label("Room-Plan Pfad:");
            roomPlanPath = GUILayout.TextField(roomPlanPath);
            GUILayout.Label("Agenten Pfad:");
            agentsPath = GUILayout.TextField(agentsPath);
        }

        if (GUILayout.Button("Setup erneut vom Server"))
        {
            StartCoroutine(SetupFromServer());
        }

        GUILayout.Space(6);
        GUILayout.Label("Agenten wählen:");
        agentScroll = GUILayout.BeginScrollView(agentScroll, GUILayout.Height(120));
        if (lastAgents != null)
        {
            foreach (var agent in lastAgents)
            {
                var id = string.IsNullOrEmpty(agent.id) ? "(unbekannt)" : agent.id;
                var label = string.IsNullOrEmpty(agent.display_name) ? id : $"{agent.display_name} ({id})";
                if (GUILayout.Button(label))
                {
                    SetActiveAgentId(id, true);
                }
            }
        }
        GUILayout.EndScrollView();

        GUILayout.Space(6);
        GUILayout.Label("Chat:");
        GUI.SetNextControlName(ChatInputControlName);
        chatInput = GUILayout.TextField(chatInput);
        isChatInputFocused = GUI.GetNameOfFocusedControl() == ChatInputControlName;
        if (Event.current.type == EventType.KeyDown
            && (Event.current.keyCode == KeyCode.Return
                || Event.current.keyCode == KeyCode.KeypadEnter
                || Event.current.character == '\n'
                || Event.current.character == '\r')
            && GUI.GetNameOfFocusedControl() == ChatInputControlName)
        {
            TrySendChatFromInput();
            Event.current.Use();
        }
        if (GUILayout.Button("Senden"))
        {
            TrySendChatFromInput();
        }
        if (GUILayout.Button("Chat leeren"))
        {
            chatLog.Clear();
        }

        chatScroll = GUILayout.BeginScrollView(chatScroll, GUILayout.Height(160));
        var chatText = string.Join("\n", chatLog);
        GUILayout.TextArea(chatText, GUILayout.ExpandHeight(true));
        GUILayout.EndScrollView();

        GUILayout.Space(6);
        GUILayout.Label("Interaktion: Linksklick auf Box wählt Agenten.");
        GUILayout.Label("Freie Kamera: WASD + QE, rechte Maustaste zum Umschauen.");
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void SetActiveAgentId(string id, bool updateStatus = false)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        activeAgentId = id;
        if (updateStatus)
        {
            statusMessage = $"Aktiver Agent: {activeAgentId}";
        }
        UpdateAgentHighlights();
    }

    private void UpdateAgentHighlights()
    {
        foreach (var pair in agentObjects)
        {
            var visual = pair.Value;
            if (visual == null || visual.renderer == null)
            {
                continue;
            }

            var isActive = pair.Key == activeAgentId;
            var color = isActive ? activeAgentColor : visual.baseColor;
            visual.renderer.material.color = color;
            if (visual.renderer.material.HasProperty("_EmissionColor"))
            {
                if (isActive)
                {
                    visual.renderer.material.EnableKeyword("_EMISSION");
                    visual.renderer.material.SetColor("_EmissionColor", color * activeAgentEmission);
                }
                else
                {
                    visual.renderer.material.SetColor("_EmissionColor", Color.black);
                }
            }
        }
    }

    private IEnumerator ShowChatBubbles(ChatResponse resp)
    {
        if (resp == null)
        {
            yield break;
        }

        if (resp.handoff != null
            && !string.IsNullOrWhiteSpace(resp.handoff.from)
            && !string.IsNullOrWhiteSpace(resp.handoff.to))
        {
            var handoffText = $"Leitet weiter an {resp.handoff.to}";
            if (!string.IsNullOrWhiteSpace(resp.handoff.reason))
            {
                handoffText = $"{handoffText}\n{resp.handoff.reason}";
            }

            SetBubble(resp.handoff.from, handoffText, handoffIndicatorDuration);
            ShowHandoffLine(resp.handoff.from, resp.handoff.to, handoffIndicatorDuration + handoffDelay);
            yield return new WaitForSeconds(handoffIndicatorDuration + handoffDelay);
            ClearBubble(resp.handoff.from);
        }

        if (resp.events == null)
        {
            yield break;
        }

        foreach (var ev in resp.events)
        {
            if (string.IsNullOrWhiteSpace(ev.agent_id))
            {
                continue;
            }

            var text = NormalizeChatText(ev.text);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            SetBubble(ev.agent_id, text, bubbleDuration);
            yield return new WaitForSeconds(bubbleStagger);
        }
    }

    private void SetBubble(string agentId, string text, float duration)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return;
        }

        agentBubbles[agentId] = new BubbleInfo
        {
            text = text,
            expiresAt = Time.time + duration
        };
    }

    private void ClearBubble(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return;
        }

        agentBubbles.Remove(agentId);
    }

    private void CleanupExpiredBubbles()
    {
        if (agentBubbles.Count == 0)
        {
            return;
        }

        var now = Time.time;
        var toRemove = new List<string>();
        foreach (var pair in agentBubbles)
        {
            if (pair.Value == null || pair.Value.expiresAt <= now)
            {
                toRemove.Add(pair.Key);
            }
        }

        for (var i = 0; i < toRemove.Count; i++)
        {
            agentBubbles.Remove(toRemove[i]);
        }
    }

    private void DrawAgentBubbles()
    {
        if (agentBubbles.Count == 0)
        {
            return;
        }

        var cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        EnsureBubbleStyles();

        foreach (var pair in agentBubbles)
        {
            if (!agentObjects.TryGetValue(pair.Key, out var visual) || visual == null || visual.obj == null)
            {
                continue;
            }

            var content = pair.Value != null ? pair.Value.text : "";
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var worldPos = visual.obj.transform.position + Vector3.up * (bubbleHeight + visual.scale * 0.5f);
            var screenPos = cam.WorldToScreenPoint(worldPos);
            if (screenPos.z <= 0f)
            {
                continue;
            }

            var maxWidth = 220f;
            var height = bubbleStyle.CalcHeight(new GUIContent(content), maxWidth);
            var rect = new Rect(
                screenPos.x - maxWidth * 0.5f,
                Screen.height - screenPos.y - height - 16f,
                maxWidth,
                height
            );

            GUI.Box(rect, content, bubbleStyle);
            var pointerRect = new Rect(rect.x, rect.yMax - 4f, rect.width, 16f);
            GUI.Label(pointerRect, "▼", bubblePointerStyle);
        }
    }

    private void EnsureBubbleStyles()
    {
        if (bubbleStyle != null)
        {
            return;
        }

        bubbleStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            fontSize = 12
        };
        bubbleStyle.padding = new RectOffset(8, 8, 6, 6);

        bubblePointerStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperCenter,
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };
    }

    private void ShowHandoffLine(string fromId, string toId, float duration)
    {
        if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId))
        {
            return;
        }

        if (handoffLine == null)
        {
            var lineObject = new GameObject("HandoffLine");
            handoffLine = lineObject.AddComponent<LineRenderer>();
            handoffLine.material = new Material(Shader.Find("Sprites/Default"));
            handoffLine.positionCount = 2;
            handoffLine.startWidth = handoffLineWidth;
            handoffLine.endWidth = handoffLineWidth;
            handoffLine.numCapVertices = 4;
        }

        handoffFromId = fromId;
        handoffToId = toId;
        handoffLine.startColor = Color.yellow;
        handoffLine.endColor = Color.yellow;
        handoffLine.gameObject.SetActive(true);
        handoffLineExpiresAt = Time.time + duration;
        UpdateHandoffLinePositions();
    }

    private void UpdateHandoffLine()
    {
        if (handoffLine == null || !handoffLine.gameObject.activeSelf)
        {
            return;
        }

        if (Time.time > handoffLineExpiresAt)
        {
            handoffLine.gameObject.SetActive(false);
            return;
        }

        UpdateHandoffLinePositions();
    }

    private void UpdateHandoffLinePositions()
    {
        if (handoffLine == null)
        {
            return;
        }

        if (!agentObjects.TryGetValue(handoffFromId, out var fromVisual)
            || !agentObjects.TryGetValue(handoffToId, out var toVisual)
            || fromVisual == null
            || toVisual == null
            || fromVisual.obj == null
            || toVisual.obj == null)
        {
            return;
        }

        var fromPos = fromVisual.obj.transform.position + Vector3.up * (fromVisual.scale * 0.6f);
        var toPos = toVisual.obj.transform.position + Vector3.up * (toVisual.scale * 0.6f);
        handoffLine.SetPosition(0, fromPos);
        handoffLine.SetPosition(1, toPos);
    }

    private void TrySendChatFromInput()
    {
        if (string.IsNullOrWhiteSpace(chatInput))
        {
            return;
        }

        var toSend = chatInput.Trim();
        chatLog.Add($"[Du] {toSend}");
        chatInput = "";
        StartCoroutine(SendChat(toSend));
    }

    private void UpdateFreeMovement()
    {
        if (!enableFreeMovement)
        {
            return;
        }

        if (isChatInputFocused)
        {
            return;
        }

        var cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        if (!cameraInitialized)
        {
            var euler = cam.transform.rotation.eulerAngles;
            cameraYaw = euler.y;
            cameraPitch = euler.x;
            cameraInitialized = true;
        }

        var move = Vector3.zero;
        var lookDelta = Vector2.zero;
        var isLooking = false;
        var isBoost = false;

#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed) move += Vector3.forward;
            if (keyboard.sKey.isPressed) move += Vector3.back;
            if (keyboard.aKey.isPressed) move += Vector3.left;
            if (keyboard.dKey.isPressed) move += Vector3.right;
            if (keyboard.qKey.isPressed) move += Vector3.down;
            if (keyboard.eKey.isPressed) move += Vector3.up;
            isBoost = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        }

        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.isPressed)
        {
            isLooking = true;
            lookDelta = mouse.delta.ReadValue();
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.W)) move += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) move += Vector3.back;
        if (Input.GetKey(KeyCode.A)) move += Vector3.left;
        if (Input.GetKey(KeyCode.D)) move += Vector3.right;
        if (Input.GetKey(KeyCode.Q)) move += Vector3.down;
        if (Input.GetKey(KeyCode.E)) move += Vector3.up;
        isBoost = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetMouseButton(1))
        {
            isLooking = true;
            lookDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        }
#endif

        if (isLooking)
        {
            cameraYaw += lookDelta.x * cameraLookSpeed;
            cameraPitch = Mathf.Clamp(cameraPitch - lookDelta.y * cameraLookSpeed, -cameraLookClamp, cameraLookClamp);
            cam.transform.rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
        }

        if (move.sqrMagnitude > 0.001f)
        {
            var speed = cameraMoveSpeed * (isBoost ? cameraBoostMultiplier : 1f);
            var direction = cam.transform.TransformDirection(move.normalized);
            cam.transform.position += direction * speed * Time.deltaTime;
        }
    }

    [System.Serializable]
    private class StructuredNpcReply
    {
        public string say;
        public string handoff_to;
        public string handoff_reason;
        public float confidence;
        public string antwort;
        public string rueckfrage;
    }
}
