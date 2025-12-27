using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY_EDITOR
public class ProjectManagerUI : EditorWindow
{
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
    public class ProjectMetadata
    {
        public string id;
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
        public string[] preferred_zone_ids;
        public string[] preferred_spawn_tags;
    }

    [Serializable]
    public class KnowledgeEntrySummary
    {
        public string tag;
        public string name;
        public string file;
    }

    [Serializable]
    public class KnowledgeListResponse
    {
        public KnowledgeEntrySummary[] knowledge;
    }

    [Serializable]
    public class ProjectDetailResponse
    {
        public ProjectMetadata project;
        public AgentSpec[] agents;
        public KnowledgeEntrySummary[] knowledge;
    }

    [Serializable]
    public class CreateProjectRequest
    {
        public string display_name;
        public string project_id;
        public string description;
    }

    [Serializable]
    public class AgentsSaveRequest
    {
        public AgentSpec[] agents;
    }

    [Serializable]
    public class MetadataSaveRequest
    {
        public string display_name;
        public string description;
    }

    [Serializable]
    public class KnowledgeUpsertRequest
    {
        public string action;
        public string tag;
        public string name;
        public string text;
        public bool overwrite;
    }

    [Serializable]
    public class KnowledgeReadRequest
    {
        public string tag;
        public string name;
    }

    [Serializable]
    public class KnowledgeReadResponse
    {
        public string tag;
        public string name;
        public string text;
    }

    private class AgentUi
    {
        public string id;
        public string displayName;
        public string persona;
        public string expertiseCsv;
        public string knowledgeTagsCsv;
        public string preferredZoneIdsCsv;
        public string preferredSpawnTagsCsv;
    }

    private class EditorCoroutine
    {
        private readonly IEnumerator routine;

        public EditorCoroutine(IEnumerator routine)
        {
            this.routine = routine;
        }

        public bool MoveNext()
        {
            return routine.MoveNext();
        }
    }

    private const string DefaultBackendUrl = "http://127.0.0.1:8787";

    private static readonly List<EditorCoroutine> ActiveCoroutines = new List<EditorCoroutine>();

    [SerializeField]
    private string backendBaseUrl = DefaultBackendUrl;

    private ProjectSummary[] projects = Array.Empty<ProjectSummary>();
    private ProjectMetadata currentProject;
    private readonly List<AgentUi> agentUiList = new List<AgentUi>();
    private KnowledgeEntrySummary[] knowledgeEntries = Array.Empty<KnowledgeEntrySummary>();
    private Vector2 scroll;
    private string statusMessage = "";

    private string newProjectName = "";
    private string newProjectId = "";
    private string newProjectDescription = "";

    private string knowledgeTag = "";
    private string knowledgeName = "";
    private string knowledgeText = "";

    [MenuItem("Tools/Project Manager")]
    public static void OpenWindow()
    {
        var window = GetWindow<ProjectManagerUI>("Project Manager");
        window.minSize = new Vector2(540, 720);
        window.Show();
    }

    private void OnEnable()
    {
        StartEditorCoroutine(RefreshProjects());
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Projekt-Manager", EditorStyles.boldLabel);

        EditorGUILayout.Space(4f);
        backendBaseUrl = EditorGUILayout.TextField("Backend Base Url", backendBaseUrl);
        if (!string.IsNullOrEmpty(statusMessage))
        {
            EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
        }

        EditorGUILayout.Space(6f);
        if (GUILayout.Button("Projekte aktualisieren"))
        {
            StartEditorCoroutine(RefreshProjects());
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Neues Projekt", EditorStyles.boldLabel);
        newProjectName = LabeledTextField("Name", newProjectName);
        newProjectId = LabeledTextField("ID (optional)", newProjectId);
        newProjectDescription = LabeledTextField("Beschreibung", newProjectDescription);
        if (GUILayout.Button("Projekt erstellen"))
        {
            StartEditorCoroutine(CreateProject());
        }

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Vorhandene Projekte", EditorStyles.boldLabel);
        if (projects.Length == 0)
        {
            EditorGUILayout.LabelField("Keine Projekte gefunden.");
        }
        else
        {
            foreach (var project in projects)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{project.display_name} ({project.id})", GUILayout.Width(320));
                if (GUILayout.Button("Laden"))
                {
                    StartEditorCoroutine(LoadProject(project.id));
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUILayout.Space(12f);
        if (currentProject != null)
        {
            EditorGUILayout.LabelField("Aktuelles Projekt: " + currentProject.display_name, EditorStyles.boldLabel);
            currentProject.display_name = LabeledTextField("Projektname", currentProject.display_name);
            currentProject.description = LabeledTextField("Beschreibung", currentProject.description);
            if (GUILayout.Button("Metadaten speichern"))
            {
                StartEditorCoroutine(SaveMetadata());
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Agenten", EditorStyles.boldLabel);
            if (GUILayout.Button("Agent hinzufügen"))
            {
                agentUiList.Add(new AgentUi
                {
                    id = "agent_" + (agentUiList.Count + 1),
                    displayName = "Neuer Agent",
                    persona = "",
                    expertiseCsv = "",
                    knowledgeTagsCsv = "",
                    preferredZoneIdsCsv = "",
                    preferredSpawnTagsCsv = "",
                });
            }

            for (var i = 0; i < agentUiList.Count; i++)
            {
                var agent = agentUiList[i];
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Agent " + (i + 1));
                if (GUILayout.Button("Entfernen", GUILayout.Width(90)))
                {
                    agentUiList.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                agent.id = LabeledTextField("ID", agent.id);
                agent.displayName = LabeledTextField("Name", agent.displayName);
                EditorGUILayout.LabelField("Persona");
                agent.persona = EditorGUILayout.TextArea(agent.persona ?? "", GUILayout.MinHeight(60));
                agent.expertiseCsv = LabeledTextField("Expertise (CSV)", agent.expertiseCsv);
                agent.knowledgeTagsCsv = LabeledTextField("Wissen-Tags (CSV)", agent.knowledgeTagsCsv);
                agent.preferredZoneIdsCsv = LabeledTextField("Bevorzugte Zonen (CSV)", agent.preferredZoneIdsCsv);
                agent.preferredSpawnTagsCsv = LabeledTextField("Bevorzugte Spawn-Tags (CSV)", agent.preferredSpawnTagsCsv);
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("Agenten speichern"))
            {
                StartEditorCoroutine(SaveAgents());
            }

            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("Wissensdatenbank", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            knowledgeTag = LabeledTextField("Tag", knowledgeTag);
            knowledgeName = LabeledTextField("Name", knowledgeName);
            EditorGUILayout.LabelField("Text");
            knowledgeText = EditorGUILayout.TextArea(knowledgeText ?? "", GUILayout.MinHeight(80));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Wissen speichern"))
            {
                StartEditorCoroutine(UpsertKnowledge());
            }
            if (GUILayout.Button("Felder leeren"))
            {
                knowledgeTag = "";
                knowledgeName = "";
                knowledgeText = "";
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.LabelField("Vorhandenes Wissen", EditorStyles.boldLabel);
            if (knowledgeEntries.Length == 0)
            {
                EditorGUILayout.LabelField("Keine Einträge.");
            }
            else
            {
                foreach (var entry in knowledgeEntries)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"{entry.tag}/{entry.name}", GUILayout.Width(220));
                    if (GUILayout.Button("Laden", GUILayout.Width(70)))
                    {
                        StartEditorCoroutine(ReadKnowledge(entry.tag, entry.name));
                    }
                    if (GUILayout.Button("Löschen", GUILayout.Width(70)))
                    {
                        StartEditorCoroutine(DeleteKnowledge(entry.tag, entry.name));
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private static void StartEditorCoroutine(IEnumerator routine)
    {
        if (routine == null)
        {
            return;
        }

        if (ActiveCoroutines.Count == 0)
        {
            EditorApplication.update += UpdateCoroutines;
        }

        ActiveCoroutines.Add(new EditorCoroutine(routine));
    }

    private static void UpdateCoroutines()
    {
        for (var i = ActiveCoroutines.Count - 1; i >= 0; i--)
        {
            if (!ActiveCoroutines[i].MoveNext())
            {
                ActiveCoroutines.RemoveAt(i);
            }
        }

        if (ActiveCoroutines.Count == 0)
        {
            EditorApplication.update -= UpdateCoroutines;
        }
    }

    private static string LabeledTextField(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, GUILayout.Width(150));
        value = EditorGUILayout.TextField(value ?? "");
        EditorGUILayout.EndHorizontal();
        return value;
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
                statusMessage = "Fehler: " + req.error;
                yield break;
            }
            var resp = JsonUtility.FromJson<ProjectListResponse>(req.downloadHandler.text);
            projects = resp?.projects ?? Array.Empty<ProjectSummary>();
            statusMessage = "Projekte geladen: " + projects.Length;
        }
    }

    private IEnumerator CreateProject()
    {
        statusMessage = "Projekt erstellen...";
        var payload = new CreateProjectRequest
        {
            display_name = newProjectName,
            project_id = string.IsNullOrWhiteSpace(newProjectId) ? null : newProjectId,
            description = newProjectDescription,
        };
        var url = $"{backendBaseUrl}/projects/create";
        yield return SendJson(url, payload);
        yield return RefreshProjects();
    }

    private IEnumerator LoadProject(string projectId)
    {
        statusMessage = "Projekt laden...";
        var url = $"{backendBaseUrl}/projects/{projectId}";
        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                statusMessage = "Fehler: " + req.error + " | " + req.downloadHandler.text;
                yield break;
            }
            var resp = JsonUtility.FromJson<ProjectDetailResponse>(req.downloadHandler.text);
            if (resp == null || resp.project == null)
            {
                statusMessage = "Projekt konnte nicht geladen werden.";
                yield break;
            }
            currentProject = resp.project;
            knowledgeEntries = resp.knowledge ?? Array.Empty<KnowledgeEntrySummary>();
            LoadAgentsUi(resp.agents ?? Array.Empty<AgentSpec>());
            statusMessage = "Projekt geladen: " + currentProject.display_name;
        }
    }

    private void LoadAgentsUi(AgentSpec[] agents)
    {
        agentUiList.Clear();
        foreach (var agent in agents)
        {
            agentUiList.Add(new AgentUi
            {
                id = agent.id ?? "",
                displayName = agent.display_name ?? "",
                persona = agent.persona ?? "",
                expertiseCsv = JoinCsv(agent.expertise),
                knowledgeTagsCsv = JoinCsv(agent.knowledge_tags),
                preferredZoneIdsCsv = JoinCsv(agent.preferred_zone_ids),
                preferredSpawnTagsCsv = JoinCsv(agent.preferred_spawn_tags),
            });
        }
    }

    private IEnumerator SaveMetadata()
    {
        if (currentProject == null)
        {
            yield break;
        }
        statusMessage = "Metadaten speichern...";
        var url = $"{backendBaseUrl}/projects/{currentProject.id}/metadata";
        var payload = new MetadataSaveRequest
        {
            display_name = currentProject.display_name,
            description = currentProject.description,
        };
        yield return SendJson(url, payload);
    }

    private IEnumerator SaveAgents()
    {
        if (currentProject == null)
        {
            yield break;
        }
        statusMessage = "Agenten speichern...";
        var agents = new List<AgentSpec>();
        foreach (var agent in agentUiList)
        {
            agents.Add(new AgentSpec
            {
                id = agent.id,
                display_name = agent.displayName,
                persona = agent.persona,
                expertise = ParseCsv(agent.expertiseCsv),
                knowledge_tags = ParseCsv(agent.knowledgeTagsCsv),
                preferred_zone_ids = ParseCsv(agent.preferredZoneIdsCsv),
                preferred_spawn_tags = ParseCsv(agent.preferredSpawnTagsCsv),
            });
        }
        var payload = new AgentsSaveRequest { agents = agents.ToArray() };
        var url = $"{backendBaseUrl}/projects/{currentProject.id}/agents";
        yield return SendJson(url, payload);
    }

    private IEnumerator UpsertKnowledge()
    {
        if (currentProject == null)
        {
            yield break;
        }
        statusMessage = "Wissen speichern...";
        var url = $"{backendBaseUrl}/projects/{currentProject.id}/knowledge";
        var payload = new KnowledgeUpsertRequest
        {
            action = "upsert",
            tag = knowledgeTag,
            name = knowledgeName,
            text = knowledgeText,
            overwrite = true,
        };
        yield return SendJson(url, payload);
        yield return RefreshKnowledge();
    }

    private IEnumerator DeleteKnowledge(string tag, string name)
    {
        if (currentProject == null)
        {
            yield break;
        }
        statusMessage = "Wissen löschen...";
        var url = $"{backendBaseUrl}/projects/{currentProject.id}/knowledge";
        var payload = new KnowledgeUpsertRequest
        {
            action = "delete",
            tag = tag,
            name = name,
            text = "",
            overwrite = true,
        };
        yield return SendJson(url, payload);
        yield return RefreshKnowledge();
    }

    private IEnumerator ReadKnowledge(string tag, string name)
    {
        if (currentProject == null)
        {
            yield break;
        }
        statusMessage = "Wissen laden...";
        var url = $"{backendBaseUrl}/projects/{currentProject.id}/knowledge/read";
        var payload = new KnowledgeReadRequest
        {
            tag = tag,
            name = name,
        };
        using (var req = BuildPost(url, payload))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                statusMessage = "Fehler: " + req.error + " | " + req.downloadHandler.text;
                yield break;
            }
            var resp = JsonUtility.FromJson<KnowledgeReadResponse>(req.downloadHandler.text);
            if (resp != null)
            {
                knowledgeTag = resp.tag ?? "";
                knowledgeName = resp.name ?? "";
                knowledgeText = resp.text ?? "";
            }
        }
    }

    private IEnumerator RefreshKnowledge()
    {
        if (currentProject == null)
        {
            yield break;
        }
        var url = $"{backendBaseUrl}/projects/{currentProject.id}/knowledge";
        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                statusMessage = "Fehler: " + req.error;
                yield break;
            }
            var resp = JsonUtility.FromJson<KnowledgeListResponse>(req.downloadHandler.text);
            knowledgeEntries = resp?.knowledge ?? Array.Empty<KnowledgeEntrySummary>();
            statusMessage = "Wissen aktualisiert.";
        }
    }

    private IEnumerator SendJson(string url, object payload)
    {
        using (var req = BuildPost(url, payload))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                statusMessage = "Fehler: " + req.error + " | " + req.downloadHandler.text;
            }
            else
            {
                statusMessage = "OK";
            }
        }
    }

    private static UnityWebRequest BuildPost(string url, object payload)
    {
        var json = JsonUtility.ToJson(payload);
        var body = Encoding.UTF8.GetBytes(json);
        var req = new UnityWebRequest(url, "POST");
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        return req;
    }

    private static string[] ParseCsv(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }
        var parts = raw.Split(',');
        var list = new List<string>();
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                list.Add(trimmed);
            }
        }
        return list.ToArray();
    }

    private static string JoinCsv(string[] values)
    {
        if (values == null || values.Length == 0)
        {
            return "";
        }
        return string.Join(", ", values);
    }
}
#endif
