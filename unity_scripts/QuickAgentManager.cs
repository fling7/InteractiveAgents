using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
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

    [Header("Animation")]
    public string animationResourceFolder = "Characters";

    [Header("TTS")]
    public bool enableTts = true;
    public float ttsCooldownSeconds = 0.5f;

    [Header("Camera Movement")]
    public bool enableFreeMovement = true;
    public float cameraMoveSpeed = 4f;
    public float cameraBoostMultiplier = 2f;
    public float cameraLookSpeed = 2f;
    public float cameraLookClamp = 80f;

    [Header("FPV-Modus")]
    public KeyCode fpvToggleKey = KeyCode.F1;
    public float fpvEyeHeight = 1.7f;

    [Header("FPV-Interaktion")]
    public float fpvInteractionRadius = 3f;
    public KeyCode fpvChatKey = KeyCode.T;
    public bool fpvProximityHandoff = true;

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
        public string voice;
        public string voice_style;
        public string tts_model;
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

    [Serializable]
    public class TtsRequest
    {
        public string text;
        public string voice;
        public string voice_style;
        public string tts_model;
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
        public AudioSource audioSource;
        public PlayableGraph animGraph;
    }

    private class BubbleInfo
    {
        public string text;
        public float expiresAt;
    }

    private class AgentVoiceSettings
    {
        public string voice;
        public string voiceStyle;
        public string ttsModel;
    }

    private readonly Dictionary<string, AgentVisual> agentObjects = new Dictionary<string, AgentVisual>();
    private readonly Dictionary<string, BubbleInfo> agentBubbles = new Dictionary<string, BubbleInfo>();
    private readonly Dictionary<string, AgentVoiceSettings> agentVoices = new Dictionary<string, AgentVoiceSettings>();
    private readonly Dictionary<string, AudioClip> ttsCache = new Dictionary<string, AudioClip>();
    private readonly HashSet<string> ttsInFlight = new HashSet<string>();
    private readonly Dictionary<string, float> ttsLastRequest = new Dictionary<string, float>();
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
    private bool _fpvActive;
    private Vector3 _fpvSavedPos;
    private Quaternion _fpvSavedRot;
    private bool _fpvChatOpen;
    private bool _fpvChatJustOpened;
    private string _fpvChatInput = "";
    private string _fpvNearestAgentId = "";
    private string _pendingHandoffAgentId = "";
    private ChatEvent[] _pendingHandoffEvents;
    private const string FpvChatControlName = "fpvChatField";

    private void Start()
    {
        EnsureSceneBasics();
        StartCoroutine(SetupFromServer());
    }

    private void Update()
    {
        CheckFpvToggle();
        if (_fpvActive) UpdateFpvProximity();
        UpdatePendingAgentPulse();
        UpdateFreeMovement();

        if (!_fpvActive && TryGetSelectPosition(out var screenPosition))
        {
            TrySelectAgentFromClick(screenPosition);
        }

        CleanupExpiredBubbles();
        UpdateHandoffLine();
    }

    private void CheckFpvToggle()
    {
        var pressed = false;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null && kb.f1Key.wasPressedThisFrame) pressed = true;
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(fpvToggleKey)) pressed = true;
#endif
        if (pressed) ToggleFpv();
    }

    private void ToggleFpv()
    {
        var cam = Camera.main;
        if (cam == null) return;

        if (!_fpvActive)
        {
            _fpvSavedPos = cam.transform.position;
            _fpvSavedRot = cam.transform.rotation;

            cam.transform.position = ComputeRoomCenter();
            cam.transform.rotation = Quaternion.identity;
            cameraYaw   = 0f;
            cameraPitch = 0f;
            cameraInitialized = true;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
            _fpvActive = true;
        }
        else
        {
            cam.transform.position = _fpvSavedPos;
            cam.transform.rotation = _fpvSavedRot;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            _fpvActive        = false;
            cameraInitialized = false;
            _fpvChatOpen      = false;
            _fpvChatJustOpened = false;
            _fpvChatInput     = "";
            _fpvNearestAgentId = "";
            if (!string.IsNullOrEmpty(_pendingHandoffAgentId)
                && agentObjects.TryGetValue(_pendingHandoffAgentId, out var pv) && pv?.renderer != null)
            {
                pv.renderer.material.SetColor("_EmissionColor", Color.black);
                pv.renderer.material.DisableKeyword("_EMISSION");
            }
            _pendingHandoffAgentId = "";
            _pendingHandoffEvents  = null;
        }
    }

    private Vector3 ComputeRoomCenter()
    {
        var sum   = Vector3.zero;
        var count = 0;
        foreach (var v in agentObjects.Values)
        {
            if (v?.obj == null) continue;
            sum += v.obj.transform.position;
            count++;
        }
        var xz = count > 0 ? sum / count : Vector3.zero;
        return new Vector3(xz.x, fpvEyeHeight, xz.z);
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
            UpdateAgentVoices(lastAgents);
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

    private void UpdateAgentVoices(AgentPlacement[] agents)
    {
        agentVoices.Clear();
        if (agents == null)
        {
            return;
        }

        foreach (var agent in agents)
        {
            if (string.IsNullOrWhiteSpace(agent.id))
            {
                continue;
            }

            agentVoices[agent.id] = new AgentVoiceSettings
            {
                voice = agent.voice,
                voiceStyle = agent.voice_style,
                ttsModel = agent.tts_model
            };
        }
    }

    private void SpawnAgents(AgentPlacement[] agents)
    {
        foreach (var entry in agentObjects)
        {
            if (entry.Value != null)
            {
                if (entry.Value.animGraph.IsValid())
                    entry.Value.animGraph.Destroy();
                if (entry.Value.obj != null)
                    Destroy(entry.Value.obj);
            }
        }
        agentObjects.Clear();

        var idleClips = LoadAllClipsFromFolder(animationResourceFolder);
        if (idleClips.Length == 0)
            Debug.LogWarning($"[QuickAgentManager] Keine AnimationClips in Resources/{animationResourceFolder} gefunden.");

        var characterPrefabs = Resources.LoadAll<GameObject>("Characters");
        var useCharacters = characterPrefabs != null && characterPrefabs.Length > 0;
        if (!useCharacters)
        {
            Debug.LogWarning("[QuickAgentManager] Keine Prefabs in Resources/Characters – nutze Würfel als Fallback.");
        }

        for (var i = 0; i < agents.Length; i++)
        {
            var agent = agents[i];
            var id = string.IsNullOrEmpty(agent.id) ? $"agent_{i + 1}" : agent.id;
            var displayName = string.IsNullOrEmpty(agent.display_name) ? id : agent.display_name;

            var pos = GetAgentSpawnPosition(agent);

            GameObject agentGo;
            Renderer mainRenderer;
            float visualScale;

            if (useCharacters)
            {
                var prefab = characterPrefabs[UnityEngine.Random.Range(0, characterPrefabs.Length)];
                agentGo = Instantiate(prefab, pos, Quaternion.identity);
                agentGo.name = $"Agent_{displayName}";
                ApplyAgentForward(agent, agentGo.transform);

                // Capsule collider on root for click selection
                if (agentGo.GetComponent<Collider>() == null)
                {
                    var cap = agentGo.AddComponent<CapsuleCollider>();
                    cap.height = 1.8f;
                    cap.radius = 0.3f;
                    cap.center = new Vector3(0f, 0.9f, 0f);
                }

                // Small disc indicator below feet for active-agent highlighting
                var indicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                indicator.name = "SelectionIndicator";
                indicator.transform.SetParent(agentGo.transform, false);
                indicator.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                indicator.transform.localScale = new Vector3(0.5f, 0.01f, 0.5f);
                if (indicator.TryGetComponent<Collider>(out var indicatorCol))
                    Destroy(indicatorCol);
                mainRenderer = indicator.GetComponent<Renderer>();
                visualScale = 1.8f;
            }
            else
            {
                agentGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                agentGo.name = $"Agent_{displayName}";
                agentGo.transform.position = pos;
                ApplyAgentForward(agent, agentGo.transform);
                var scale = UnityEngine.Random.Range(boxScaleRange.x, boxScaleRange.y);
                agentGo.transform.localScale = Vector3.one * scale;
                mainRenderer = agentGo.GetComponent<Renderer>();
                visualScale = scale;
            }

            var baseColor = Color.Lerp(new Color(0.3f, 0.6f, 1f), Color.white, 0.2f * i);
            if (mainRenderer != null)
            {
                mainRenderer.material.color = baseColor;
            }

            var audioSource = agentGo.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;

            var idleClip = idleClips.Length > 0
                ? idleClips[UnityEngine.Random.Range(0, idleClips.Length)]
                : null;

            var animGraph = new PlayableGraph();
            if (useCharacters && idleClip != null)
            {
                var animator = agentGo.GetComponent<Animator>();
                if (animator == null)
                    animator = agentGo.AddComponent<Animator>();
                animGraph = PlayableGraph.Create($"Idle_{id}");
                var clipPlayable = AnimationClipPlayable.Create(animGraph, idleClip);
                var output = AnimationPlayableOutput.Create(animGraph, "Idle", animator);
                output.SetSourcePlayable(clipPlayable);
                animGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                animGraph.Play();
            }

            agentObjects[id] = new AgentVisual
            {
                obj = agentGo,
                renderer = mainRenderer,
                baseColor = baseColor,
                scale = visualScale,
                audioSource = audioSource,
                animGraph = animGraph,
            };
        }

        UpdateAgentHighlights();
    }

    private AnimationClip[] LoadAllClipsFromFolder(string folder)
    {
        var assets = Resources.LoadAll<AnimationClip>(folder);
        var result = new List<AnimationClip>();
        foreach (var clip in assets)
        {
            if (!clip.name.StartsWith("__preview__"))
                result.Add(clip);
        }
        return result.ToArray();
    }

    private void OnDestroy()
    {
        foreach (var entry in agentObjects)
        {
            if (entry.Value != null && entry.Value.animGraph.IsValid())
                entry.Value.animGraph.Destroy();
        }
    }

    private Vector3 GetAgentSpawnPosition(AgentPlacement agent)
    {
        if (agent != null && agent.position != null)
        {
            return new Vector3(agent.position.x, agent.position.y, agent.position.z);
        }

        return new Vector3(
            UnityEngine.Random.Range(-spawnArea.x * 0.5f, spawnArea.x * 0.5f),
            spawnHeight,
            UnityEngine.Random.Range(-spawnArea.z * 0.5f, spawnArea.z * 0.5f)
        );
    }

    private void ApplyAgentForward(AgentPlacement agent, Transform target)
    {
        if (agent == null || target == null || agent.forward == null)
        {
            return;
        }

        var forward = new Vector3(agent.forward.x, agent.forward.y, agent.forward.z);
        if (forward.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        target.rotation = Quaternion.LookRotation(forward, Vector3.up);
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
        if (!Physics.Raycast(ray, out var hit))
        {
            return;
        }

        // Walk up the hierarchy so clicks on child meshes of FBX characters still register
        var hitTransform = hit.collider.transform;
        while (hitTransform != null)
        {
            foreach (var pair in agentObjects)
            {
                if (pair.Value != null && pair.Value.obj != null
                    && pair.Value.obj.transform == hitTransform)
                {
                    SetActiveAgentId(pair.Key, true);
                    return;
                }
            }
            hitTransform = hitTransform.parent;
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

            var isHandoff = resp.handoff != null && !string.IsNullOrEmpty(resp.handoff.to);
            if (isHandoff && _fpvActive && fpvProximityHandoff)
            {
                // From-agent events go to log now; to-agent events are deferred until arrival
                var fromEvents = FilterEvents(resp.events, resp.handoff.from, include: true);
                var toEvents   = FilterEvents(resp.events, resp.handoff.from, include: false);
                AppendChatEvents(fromEvents);
                _pendingHandoffAgentId = resp.active_agent_id;
                _pendingHandoffEvents  = toEvents;
                StartCoroutine(ShowHandoffOnly(resp, fromEvents));
            }
            else
            {
                SetActiveAgentId(resp.active_agent_id);
                AppendChatEvents(resp.events);
                StartCoroutine(ShowChatBubbles(resp));
            }
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

        if (_fpvActive)
        {
            DrawFpvHud();
            isChatInputFocused = _fpvChatOpen;
            return;
        }

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
            StartCoroutine(PlayAgentSpeech(ev.agent_id, text));
            yield return new WaitForSeconds(bubbleStagger);
        }
    }

    private IEnumerator ShowHandoffOnly(ChatResponse resp, ChatEvent[] fromEvents)
    {
        if (resp.handoff == null) yield break;

        // Show forwarding agent's own speech immediately
        foreach (var ev in fromEvents)
        {
            var text = NormalizeChatText(ev.text);
            if (string.IsNullOrWhiteSpace(text)) continue;
            SetBubble(ev.agent_id, text, bubbleDuration);
            StartCoroutine(PlayAgentSpeech(ev.agent_id, text));
            yield return new WaitForSeconds(bubbleStagger);
        }

        // Then show the forwarding indicator
        var handoffText = $"Leitet weiter an {resp.handoff.to}";
        if (!string.IsNullOrWhiteSpace(resp.handoff.reason))
            handoffText = $"{handoffText}\n{resp.handoff.reason}";
        SetBubble(resp.handoff.from, handoffText, handoffIndicatorDuration);
        ShowHandoffLine(resp.handoff.from, resp.handoff.to, handoffIndicatorDuration + handoffDelay);
        yield return new WaitForSeconds(handoffIndicatorDuration + handoffDelay);
        ClearBubble(resp.handoff.from);
    }

    private static ChatEvent[] FilterEvents(ChatEvent[] events, string agentId, bool include)
    {
        if (events == null) return Array.Empty<ChatEvent>();
        var result = new List<ChatEvent>();
        foreach (var e in events)
            if (include ? e.agent_id == agentId : e.agent_id != agentId)
                result.Add(e);
        return result.ToArray();
    }

    private void TriggerPendingHandoffArrival()
    {
        var agentId = _pendingHandoffAgentId;
        var events  = _pendingHandoffEvents;

        // Reset emission before clearing pending state
        if (agentObjects.TryGetValue(agentId, out var visual) && visual?.renderer != null)
        {
            visual.renderer.material.SetColor("_EmissionColor", Color.black);
            visual.renderer.material.DisableKeyword("_EMISSION");
        }

        _pendingHandoffAgentId = "";
        _pendingHandoffEvents  = null;
        SetActiveAgentId(agentId);
        if (events != null && events.Length > 0)
        {
            AppendChatEvents(events);
            StartCoroutine(ShowChatBubbles(new ChatResponse
            {
                session_id      = sessionId,
                active_agent_id = agentId,
                events          = events
            }));
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

    private AgentVoiceSettings GetAgentVoiceSettings(string agentId)
    {
        if (!string.IsNullOrWhiteSpace(agentId) && agentVoices.TryGetValue(agentId, out var settings))
        {
            return settings;
        }

        return new AgentVoiceSettings
        {
            voice = null,
            voiceStyle = null,
            ttsModel = null
        };
    }

    private bool IsTtsRateLimited(string agentId)
    {
        if (ttsCooldownSeconds <= 0f || string.IsNullOrWhiteSpace(agentId))
        {
            return false;
        }

        if (ttsLastRequest.TryGetValue(agentId, out var lastTime))
        {
            return Time.time - lastTime < ttsCooldownSeconds;
        }

        return false;
    }

    private void RecordTtsRequest(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return;
        }

        ttsLastRequest[agentId] = Time.time;
    }

    private void PlayAgentClip(string agentId, AudioClip clip)
    {
        if (clip == null || string.IsNullOrWhiteSpace(agentId))
        {
            return;
        }

        if (agentObjects.TryGetValue(agentId, out var visual) && visual != null && visual.audioSource != null)
        {
            visual.audioSource.PlayOneShot(clip);
        }
    }

    private IEnumerator PlayAgentSpeech(string agentId, string text)
    {
        if (!enableTts || string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(agentId))
        {
            yield break;
        }

        var key = $"{agentId}::{text}";
        if (ttsCache.TryGetValue(key, out var cachedClip))
        {
            Debug.Log($"[TTS] Cache hit für Agent {agentId} (text_len={text.Length}).");
            PlayAgentClip(agentId, cachedClip);
            yield break;
        }

        if (ttsInFlight.Contains(key))
        {
            Debug.Log($"[TTS] Anfrage bereits in-flight für Agent {agentId} (text_len={text.Length}).");
            yield break;
        }

        if (IsTtsRateLimited(agentId))
        {
            Debug.Log($"[TTS] Rate limit aktiv für Agent {agentId} (text_len={text.Length}).");
            yield break;
        }

        ttsInFlight.Add(key);
        RecordTtsRequest(agentId);

        var voiceSettings = GetAgentVoiceSettings(agentId);
        var payload = new TtsRequest
        {
            text = text,
            voice = voiceSettings.voice,
            voice_style = voiceSettings.voiceStyle,
            tts_model = voiceSettings.ttsModel
        };
        var json = JsonUtility.ToJson(payload);
        var url = $"{backendBaseUrl}/tts";
        Debug.Log(
            "[TTS] Sende Anfrage: "
            + $"agent={agentId}, text_len={text.Length}, voice={payload.voice}, model={payload.tts_model}"
        );

        using (var req = new UnityWebRequest(url, "POST"))
        {
            var body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            ttsInFlight.Remove(key);

            if (req.result != UnityWebRequest.Result.Success)
            {
                statusMessage = "TTS fehlgeschlagen: " + req.error;
                chatLog.Add(statusMessage + " | " + req.downloadHandler.text);
                Debug.LogWarning($"[TTS] Fehler: agent={agentId}, error={req.error}");
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(req);
            if (clip == null)
            {
                statusMessage = "TTS fehlgeschlagen: Kein AudioClip.";
                chatLog.Add(statusMessage);
                Debug.LogWarning($"[TTS] Kein AudioClip: agent={agentId}");
                yield break;
            }

            ttsCache[key] = clip;
            Debug.Log($"[TTS] AudioClip erhalten: agent={agentId}, length={clip.length:0.00}s");
            PlayAgentClip(agentId, clip);
        }
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

        if (isChatInputFocused && !_fpvActive)
        {
            return;
        }

        if (_fpvChatOpen)
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
        if (mouse != null && (_fpvActive || mouse.rightButton.isPressed))
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

        if (_fpvActive || Input.GetMouseButton(1))
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
            Vector3 direction;
            if (_fpvActive)
            {
                // Horizontal movement (WASD) uses yaw only so height stays locked
                var horizontal = Quaternion.Euler(0f, cameraYaw, 0f) * new Vector3(move.x, 0f, move.z);
                var vertical   = new Vector3(0f, move.y, 0f);
                direction = (horizontal + vertical).normalized;
            }
            else
            {
                direction = cam.transform.TransformDirection(move.normalized);
            }
            cam.transform.position += direction * speed * Time.deltaTime;
        }
    }

    private void UpdatePendingAgentPulse()
    {
        if (string.IsNullOrEmpty(_pendingHandoffAgentId)) return;
        if (!agentObjects.TryGetValue(_pendingHandoffAgentId, out var visual) || visual?.renderer == null) return;
        var pulse = Mathf.Sin(Time.time * 4f) * 0.5f + 0.5f;
        var emissive = activeAgentColor * (pulse * 2.5f);
        var mat = visual.renderer.material;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", emissive);
    }

    private void UpdateFpvProximity()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var camPos = cam.transform.position;
        var nearest = "";
        var nearestDist = fpvInteractionRadius + 1f;

        foreach (var pair in agentObjects)
        {
            if (pair.Value?.obj == null) continue;
            var d = Vector3.Distance(camPos, pair.Value.obj.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = pair.Key; }
        }

        if (nearestDist <= fpvInteractionRadius)
        {
            _fpvNearestAgentId = nearest;
            // Trigger pending handoff arrival before auto-switching active agent
            if (!string.IsNullOrEmpty(_pendingHandoffAgentId) && nearest == _pendingHandoffAgentId)
            {
                TriggerPendingHandoffArrival();
            }
            else if (!string.IsNullOrEmpty(nearest) && nearest != activeAgentId)
            {
                SetActiveAgentId(nearest);
            }
        }
        else
        {
            _fpvNearestAgentId = "";
            if (_fpvChatOpen)
            {
                _fpvChatOpen = false;
                _fpvChatInput = "";
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

#if ENABLE_INPUT_SYSTEM
        if (!string.IsNullOrEmpty(_fpvNearestAgentId) && !_fpvChatOpen)
        {
            var kb = Keyboard.current;
            if (kb != null && kb.tKey.wasPressedThisFrame)
                ToggleFpvChat();
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (!string.IsNullOrEmpty(_fpvNearestAgentId) && !_fpvChatOpen && Input.GetKeyDown(fpvChatKey))
            ToggleFpvChat();
#endif
    }

    private void ToggleFpvChat()
    {
        _fpvChatOpen = !_fpvChatOpen;
        if (_fpvChatOpen)
        {
            _fpvChatJustOpened = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            _fpvChatInput = "";
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void DrawFpvDirectionArrow()
    {
        if (string.IsNullOrEmpty(_pendingHandoffAgentId)) return;
        if (!agentObjects.TryGetValue(_pendingHandoffAgentId, out var visual) || visual?.obj == null) return;

        var cam = Camera.main;
        if (cam == null) return;

        var sw = Screen.width;
        var sh = Screen.height;
        var center = new Vector2(sw * 0.5f, sh * 0.5f);

        // Agent position slightly above feet for better aim
        var agentWorldPos = visual.obj.transform.position + Vector3.up * 1.0f;
        var screenPos = cam.WorldToScreenPoint(agentWorldPos);
        var isBehind = screenPos.z < 0f;

        // Convert to IMGUI coords (Y flipped)
        var guiPos = new Vector2(screenPos.x, sh - screenPos.y);
        var dir = isBehind ? (center - guiPos).normalized : (guiPos - center).normalized;
        if (dir.sqrMagnitude < 0.001f) dir = Vector2.up;

        // Angle: ▲ points toward -Y in IMGUI ("up"), rotate clockwise to match direction
        var angle = Mathf.Atan2(dir.x, -dir.y) * Mathf.Rad2Deg;

        const float radius   = 130f;
        const float arrowSize = 38f;
        var arrowCenter = center + dir * radius;

        var pulse = Mathf.Sin(Time.time * 3f) * 0.3f + 0.7f;
        var oldColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, pulse);

        var savedMatrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, arrowCenter);

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 30,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        style.normal.textColor = new Color(1f, 0.88f, 0.1f);
        GUI.Label(new Rect(arrowCenter.x - arrowSize * 0.5f, arrowCenter.y - arrowSize * 0.5f,
            arrowSize, arrowSize), "▲", style);

        GUI.matrix = savedMatrix;
        GUI.color = oldColor;
    }

    private void DrawFpvHud()
    {
        var sw = Screen.width;
        var sh = Screen.height;

        // Crosshair dot
        if (!_fpvChatOpen)
        {
            const int dotSize = 6;
            GUI.DrawTexture(
                new Rect(sw * 0.5f - dotSize * 0.5f, sh * 0.5f - dotSize * 0.5f, dotSize, dotSize),
                Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 0f);
        }

        // Direction arrow toward pending handoff agent
        DrawFpvDirectionArrow();

        // Nearby agent nameplate
        if (!string.IsNullOrEmpty(_fpvNearestAgentId))
        {
            var agentName = _fpvNearestAgentId;
            if (lastAgents != null)
            {
                foreach (var a in lastAgents)
                {
                    if (a.id == _fpvNearestAgentId)
                    {
                        agentName = string.IsNullOrEmpty(a.display_name) ? a.id : a.display_name;
                        break;
                    }
                }
            }

            var isPendingArrival = _fpvNearestAgentId == _pendingHandoffAgentId
                                   && !string.IsNullOrEmpty(_pendingHandoffAgentId);
            var plateStyle = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            plateStyle.normal.textColor = isPendingArrival ? new Color(1f, 0.85f, 0.2f) : Color.white;
            string plateText;
            if (_fpvChatOpen)
                plateText = agentName;
            else if (isPendingArrival)
                plateText = $"★  {agentName}  —  wartet auf dich";
            else
                plateText = $"{agentName}   [{fpvChatKey} = Chat öffnen]";
            GUI.Box(new Rect(sw * 0.5f - 200f, sh * 0.5f + 40f, 400f, 28f), plateText, plateStyle);
        }

        // Chat overlay
        if (_fpvChatOpen)
        {
            const float panelW = 560f;
            const float panelH = 110f;
            var panelX = sw * 0.5f - panelW * 0.5f;
            var panelY = sh - panelH - 60f;

            // Handle Enter / Esc BEFORE the TextField consumes the event
            var ev = Event.current;
            if (ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter)
                {
                    SendFpvChat();
                    ev.Use();
                }
                else if (ev.keyCode == KeyCode.Escape)
                {
                    _fpvChatOpen = false;
                    _fpvChatInput = "";
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    ev.Use();
                    return; // skip rest of HUD this frame
                }
            }

            GUI.Box(new Rect(panelX - 4f, panelY - 4f, panelW + 8f, panelH + 8f), GUIContent.none);

            var agentName = _fpvNearestAgentId;
            if (lastAgents != null)
            {
                foreach (var a in lastAgents)
                {
                    if (a.id == _fpvNearestAgentId)
                    {
                        agentName = string.IsNullOrEmpty(a.display_name) ? a.id : a.display_name;
                        break;
                    }
                }
            }

            var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            labelStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            GUI.Label(new Rect(panelX, panelY + 2f, panelW, 20f),
                $"Gespräch mit: {agentName}  |  Enter = Senden  |  Esc = Schließen", labelStyle);

            GUI.SetNextControlName(FpvChatControlName);
            _fpvChatInput = GUI.TextField(
                new Rect(panelX, panelY + 24f, panelW - 90f, 32f), _fpvChatInput, 512);

            // Request focus only on the frame the chat was opened
            if (_fpvChatJustOpened)
            {
                GUI.FocusControl(FpvChatControlName);
                _fpvChatJustOpened = false;
            }

            if (GUI.Button(new Rect(panelX + panelW - 86f, panelY + 24f, 86f, 32f), "Senden"))
                SendFpvChat();

            // Last two chat lines for context
            var logStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            logStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            var recent = new List<string>();
            for (var i = chatLog.Count - 1; i >= 0 && recent.Count < 2; i--)
                recent.Insert(0, chatLog[i]);
            GUI.Label(new Rect(panelX, panelY + 62f, panelW, 40f),
                string.Join("\n", recent), logStyle);
        }

        // Pending handoff indicator — pulsing banner when target is not yet in range
        if (!string.IsNullOrEmpty(_pendingHandoffAgentId) && _fpvNearestAgentId != _pendingHandoffAgentId)
        {
            var pendingName = _pendingHandoffAgentId;
            if (lastAgents != null)
            {
                foreach (var a in lastAgents)
                {
                    if (a.id == _pendingHandoffAgentId)
                    {
                        pendingName = string.IsNullOrEmpty(a.display_name) ? a.id : a.display_name;
                        break;
                    }
                }
            }

            var pulse = Mathf.Sin(Time.time * 3f) * 0.25f + 0.75f;
            var oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, pulse);

            var pendingStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize  = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            pendingStyle.normal.textColor = new Color(1f, 0.9f, 0.1f);
            GUI.Box(new Rect(sw * 0.5f - 300f, 40f, 600f, 36f),
                $"★   {pendingName} wartet auf dich  —  lauf hin!   ★", pendingStyle);

            GUI.color = oldColor;
        }

        // Bottom hint bar (hidden while chat is open)
        if (!_fpvChatOpen)
        {
            var hudStyle = new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            hudStyle.normal.textColor = Color.white;
            var hint = $"FPV-Modus  |  WASD = bewegen  QE = hoch/runter  Shift = schneller  |  {fpvToggleKey} = beenden";
            GUI.Box(new Rect(sw * 0.5f - 320f, 8f, 640f, 26f), hint, hudStyle);
        }
    }

    private void SendFpvChat()
    {
        var text = _fpvChatInput.Trim();
        if (string.IsNullOrEmpty(text)) return;
        chatLog.Add($"[Du] {text}");
        _fpvChatInput = "";
        StartCoroutine(SendChat(text));
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
