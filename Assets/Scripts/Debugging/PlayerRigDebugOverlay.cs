#if PLAYER_RIG_DEBUG_OVERLAY
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CIS5680VRGame.Debugging
{
    public sealed class PlayerRigDebugOverlay : MonoBehaviour
    {
        const string RootName = "PlayerRigDebugOverlay";
        const float MarkerRadius = 0.12f;
        const float MarkerHeight = 0.08f;
        const float WarningOffsetMeters = 0.3f;
        const float CriticalOffsetMeters = 0.7f;
        const float LogIntervalSeconds = 1.5f;

        static PlayerRigDebugOverlay s_Instance;

        [SerializeField] bool m_ShowOverlay = true;
        [SerializeField] bool m_ShowWorldMarkers = true;
        [SerializeField] bool m_LogOffsets = true;

        readonly StringBuilder m_TextBuilder = new(768);

        XROrigin m_XROrigin;
        Camera m_Camera;
        CharacterController m_CharacterController;
        Canvas m_Canvas;
        Text m_Label;
        Transform m_RootMarker;
        Transform m_CameraMarker;
        Transform m_ControllerMarker;
        LineRenderer m_RootToCameraLine;
        LineRenderer m_CameraToControllerLine;
        Material m_RootMaterial;
        Material m_CameraMaterial;
        Material m_ControllerMaterial;
        Material m_WarningMaterial;
        Material m_LineMaterial;
        float m_NextLogAt;
        bool m_HasLoggedForScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureInstance();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureInstance();
            if (s_Instance != null)
                s_Instance.ResolveSceneReferences(force: true);
        }

        static void EnsureInstance()
        {
            if (s_Instance != null)
                return;

            GameObject root = new(RootName);
            DontDestroyOnLoad(root);
            s_Instance = root.AddComponent<PlayerRigDebugOverlay>();
        }

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            DontDestroyOnLoad(gameObject);
            CreateMaterials();
            CreateCanvas();
            CreateWorldMarkers();
            ResolveSceneReferences(force: true);
        }

        void OnDestroy()
        {
            if (s_Instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                s_Instance = null;
            }

            DestroyRuntimeMaterial(m_RootMaterial);
            DestroyRuntimeMaterial(m_CameraMaterial);
            DestroyRuntimeMaterial(m_ControllerMaterial);
            DestroyRuntimeMaterial(m_WarningMaterial);
            DestroyRuntimeMaterial(m_LineMaterial);
        }

        void Update()
        {
            if (WasTogglePressed())
                m_ShowOverlay = !m_ShowOverlay;

            ResolveSceneReferences(force: false);

            if (!m_ShowOverlay)
            {
                SetOverlayVisible(false);
                return;
            }

            SetOverlayVisible(true);
            RefreshCanvasCamera();
            RefreshMarkers();
            RefreshText();
            MaybeLogOffsets();
        }

        void ResolveSceneReferences(bool force)
        {
            if (force)
            {
                m_XROrigin = null;
                m_Camera = null;
                m_CharacterController = null;
                m_HasLoggedForScene = false;
                m_NextLogAt = 0f;
            }

            if (m_XROrigin == null)
                m_XROrigin = FindObjectOfType<XROrigin>();

            if (m_XROrigin != null)
            {
                if (m_Camera == null)
                    m_Camera = m_XROrigin.Camera != null ? m_XROrigin.Camera : m_XROrigin.GetComponentInChildren<Camera>(true);

                if (m_CharacterController == null)
                    m_CharacterController = m_XROrigin.GetComponent<CharacterController>();
            }

            if (m_Camera == null)
                m_Camera = Camera.main;

            if (m_CharacterController == null)
                m_CharacterController = FindObjectOfType<CharacterController>();
        }

        void CreateMaterials()
        {
            m_RootMaterial = CreateMaterial(new Color(0.25f, 0.75f, 1f, 0.92f));
            m_CameraMaterial = CreateMaterial(new Color(0.2f, 1f, 0.35f, 0.92f));
            m_ControllerMaterial = CreateMaterial(new Color(1f, 0.9f, 0.2f, 0.92f));
            m_WarningMaterial = CreateMaterial(new Color(1f, 0.22f, 0.12f, 0.96f));
            m_LineMaterial = CreateMaterial(new Color(1f, 1f, 1f, 0.55f));
        }

        static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Sprites/Default");
            Material material = new(shader)
            {
                color = color,
                hideFlags = HideFlags.HideAndDontSave,
            };
            return material;
        }

        void CreateCanvas()
        {
            GameObject canvasObject = new("PlayerRigDebugOverlayCanvas");
            canvasObject.transform.SetParent(transform, false);

            m_Canvas = canvasObject.AddComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_Canvas.sortingOrder = 32000;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panelObject = new("Panel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvasObject.transform, false);
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(18f, -18f);
            panelRect.sizeDelta = new Vector2(620f, 246f);

            Image panelImage = panelObject.GetComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.72f);
            panelImage.raycastTarget = false;

            GameObject labelObject = new("Label", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(panelObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(16f, 12f);
            labelRect.offsetMax = new Vector2(-16f, -12f);

            m_Label = labelObject.GetComponent<Text>();
            m_Label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            m_Label.fontSize = 22;
            m_Label.alignment = TextAnchor.UpperLeft;
            m_Label.horizontalOverflow = HorizontalWrapMode.Wrap;
            m_Label.verticalOverflow = VerticalWrapMode.Overflow;
            m_Label.color = Color.white;
            m_Label.raycastTarget = false;
        }

        void CreateWorldMarkers()
        {
            m_RootMarker = CreateMarker("RigRootMarker", m_RootMaterial);
            m_CameraMarker = CreateMarker("CameraGroundMarker", m_CameraMaterial);
            m_ControllerMarker = CreateMarker("CharacterControllerMarker", m_ControllerMaterial);
            m_RootToCameraLine = CreateLine("RootToCameraGroundLine");
            m_CameraToControllerLine = CreateLine("CameraGroundToControllerLine");
        }

        Transform CreateMarker(string markerName, Material material)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = markerName;
            marker.transform.SetParent(transform, false);
            marker.transform.localScale = Vector3.one * MarkerRadius;

            Collider markerCollider = marker.GetComponent<Collider>();
            if (markerCollider != null)
                Destroy(markerCollider);

            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;

            return marker.transform;
        }

        LineRenderer CreateLine(string lineName)
        {
            GameObject lineObject = new(lineName);
            lineObject.transform.SetParent(transform, false);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = 0.025f;
            line.endWidth = 0.025f;
            line.sharedMaterial = m_LineMaterial;
            line.numCapVertices = 4;
            return line;
        }

        void RefreshCanvasCamera()
        {
            if (m_Canvas == null)
                return;

            if (m_Camera == null)
            {
                m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                m_Canvas.worldCamera = null;
                return;
            }

            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_Canvas.worldCamera = null;
        }

        void RefreshMarkers()
        {
            if (!TryGetDebugPoints(out DebugPoints points))
            {
                SetWorldMarkersVisible(false);
                return;
            }

            SetWorldMarkersVisible(m_ShowWorldMarkers);
            if (!m_ShowWorldMarkers)
                return;

            float cameraOffset = PlanarDistance(points.RigRoot, points.CameraGround);
            float controllerOffset = PlanarDistance(points.CameraGround, points.ControllerGround);
            bool warning = cameraOffset >= WarningOffsetMeters || controllerOffset >= WarningOffsetMeters;

            SetMarker(m_RootMarker, points.RigRoot + Vector3.up * MarkerHeight, m_RootMaterial);
            SetMarker(m_CameraMarker, points.CameraGround + Vector3.up * (MarkerHeight + 0.08f), m_CameraMaterial);
            SetMarker(m_ControllerMarker, points.ControllerGround + Vector3.up * (MarkerHeight + 0.16f), warning ? m_WarningMaterial : m_ControllerMaterial);
            SetLine(m_RootToCameraLine, points.RigRoot + Vector3.up * 0.03f, points.CameraGround + Vector3.up * 0.03f);
            SetLine(m_CameraToControllerLine, points.CameraGround + Vector3.up * 0.09f, points.ControllerGround + Vector3.up * 0.09f);
        }

        static void SetMarker(Transform marker, Vector3 position, Material material)
        {
            if (marker == null)
                return;

            marker.position = position;
            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
        }

        static void SetLine(LineRenderer line, Vector3 start, Vector3 end)
        {
            if (line == null)
                return;

            line.SetPosition(0, start);
            line.SetPosition(1, end);
        }

        void RefreshText()
        {
            if (m_Label == null)
                return;

            if (!TryGetDebugPoints(out DebugPoints points))
            {
                m_Label.text = "Player Rig Debug Overlay\nNo XR Origin / Camera / CharacterController found.";
                return;
            }

            Vector3 rootToCamera = PlanarDelta(points.RigRoot, points.CameraGround);
            Vector3 rootToController = PlanarDelta(points.RigRoot, points.ControllerGround);
            Vector3 cameraToController = PlanarDelta(points.CameraGround, points.ControllerGround);
            float rootToCameraDistance = rootToCamera.magnitude;
            float rootToControllerDistance = rootToController.magnitude;
            float cameraToControllerDistance = cameraToController.magnitude;
            string status = ResolveStatus(rootToCameraDistance, cameraToControllerDistance);

            m_TextBuilder.Clear();
            m_TextBuilder.AppendLine($"Player Rig Debug Overlay [{status}]  F9 toggle");
            m_TextBuilder.AppendLine($"Scene: {SceneManager.GetActiveScene().name}");
            m_TextBuilder.AppendLine($"Rig Root:      {FormatVector(points.RigRoot)}");
            m_TextBuilder.AppendLine($"Camera Ground: {FormatVector(points.CameraGround)}");
            m_TextBuilder.AppendLine($"CC Ground:     {FormatVector(points.ControllerGround)}");
            m_TextBuilder.AppendLine($"Root -> Camera: {rootToCameraDistance:0.000}m  {FormatVector(rootToCamera)}");
            m_TextBuilder.AppendLine($"Root -> CC:     {rootToControllerDistance:0.000}m  {FormatVector(rootToController)}");
            m_TextBuilder.AppendLine($"Camera -> CC:   {cameraToControllerDistance:0.000}m  {FormatVector(cameraToController)}");
            m_TextBuilder.AppendLine($"Camera local:   {FormatVector(points.CameraLocalInRig)}");
            m_TextBuilder.AppendLine($"Rig euler:      {FormatVector(m_XROrigin != null ? m_XROrigin.transform.eulerAngles : Vector3.zero)}");
            m_Label.text = m_TextBuilder.ToString();

            m_Label.color = status == "CRITICAL"
                ? new Color(1f, 0.48f, 0.35f, 1f)
                : status == "WARN"
                    ? new Color(1f, 0.92f, 0.42f, 1f)
                    : Color.white;
        }

        void MaybeLogOffsets()
        {
            if (!m_LogOffsets || !TryGetDebugPoints(out DebugPoints points))
                return;

            float rootToCamera = PlanarDistance(points.RigRoot, points.CameraGround);
            float cameraToController = PlanarDistance(points.CameraGround, points.ControllerGround);

            if (!m_HasLoggedForScene)
            {
                m_HasLoggedForScene = true;
                m_NextLogAt = Time.unscaledTime + LogIntervalSeconds;
                Debug.Log(
                    $"[PlayerRigDebugOverlay] {SceneManager.GetActiveScene().name} initial {ResolveStatus(rootToCamera, cameraToController)}: " +
                    $"root->camera={rootToCamera:0.000}m, camera->cc={cameraToController:0.000}m, " +
                    $"root={FormatVector(points.RigRoot)}, cameraGround={FormatVector(points.CameraGround)}, ccGround={FormatVector(points.ControllerGround)}, " +
                    $"cameraLocal={FormatVector(points.CameraLocalInRig)}, rigEuler={FormatVector(m_XROrigin != null ? m_XROrigin.transform.eulerAngles : Vector3.zero)}",
                    this);
                return;
            }

            if (rootToCamera < WarningOffsetMeters && cameraToController < WarningOffsetMeters)
                return;

            if (Time.unscaledTime < m_NextLogAt)
                return;

            m_NextLogAt = Time.unscaledTime + LogIntervalSeconds;
            Debug.LogWarning(
                $"[PlayerRigDebugOverlay] {SceneManager.GetActiveScene().name} offset {ResolveStatus(rootToCamera, cameraToController)}: " +
                $"root->camera={rootToCamera:0.000}m, camera->cc={cameraToController:0.000}m, " +
                $"root={FormatVector(points.RigRoot)}, cameraGround={FormatVector(points.CameraGround)}, ccGround={FormatVector(points.ControllerGround)}, " +
                $"cameraLocal={FormatVector(points.CameraLocalInRig)}, rigEuler={FormatVector(m_XROrigin != null ? m_XROrigin.transform.eulerAngles : Vector3.zero)}",
                this);
        }

        bool TryGetDebugPoints(out DebugPoints points)
        {
            points = default;
            if (m_XROrigin == null || m_Camera == null)
                return false;

            Vector3 rigRoot = m_XROrigin.transform.position;
            Vector3 cameraWorld = m_Camera.transform.position;
            Vector3 cameraGround = new(cameraWorld.x, rigRoot.y, cameraWorld.z);

            Vector3 controllerGround = cameraGround;
            if (m_CharacterController != null)
            {
                Vector3 controllerCenter = m_CharacterController.bounds.center;
                controllerGround = new(controllerCenter.x, rigRoot.y, controllerCenter.z);
            }

            points = new DebugPoints
            {
                RigRoot = rigRoot,
                CameraGround = cameraGround,
                ControllerGround = controllerGround,
                CameraLocalInRig = m_XROrigin.transform.InverseTransformPoint(cameraWorld),
            };
            return true;
        }

        void SetOverlayVisible(bool visible)
        {
            if (m_Canvas != null && m_Canvas.gameObject.activeSelf != visible)
                m_Canvas.gameObject.SetActive(visible);

            SetWorldMarkersVisible(visible && m_ShowWorldMarkers);
        }

        void SetWorldMarkersVisible(bool visible)
        {
            SetTransformVisible(m_RootMarker, visible);
            SetTransformVisible(m_CameraMarker, visible);
            SetTransformVisible(m_ControllerMarker, visible);
            if (m_RootToCameraLine != null && m_RootToCameraLine.gameObject.activeSelf != visible)
                m_RootToCameraLine.gameObject.SetActive(visible);

            if (m_CameraToControllerLine != null && m_CameraToControllerLine.gameObject.activeSelf != visible)
                m_CameraToControllerLine.gameObject.SetActive(visible);
        }

        static void SetTransformVisible(Transform target, bool visible)
        {
            if (target != null && target.gameObject.activeSelf != visible)
                target.gameObject.SetActive(visible);
        }

        static Vector3 PlanarDelta(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            return delta;
        }

        static float PlanarDistance(Vector3 a, Vector3 b)
        {
            return PlanarDelta(a, b).magnitude;
        }

        static string ResolveStatus(float rootToCameraDistance, float cameraToControllerDistance)
        {
            float worst = Mathf.Max(rootToCameraDistance, cameraToControllerDistance);
            if (worst >= CriticalOffsetMeters)
                return "CRITICAL";

            if (worst >= WarningOffsetMeters)
                return "WARN";

            return "OK";
        }

        static string FormatVector(Vector3 value)
        {
            return $"({value.x:0.000}, {value.y:0.000}, {value.z:0.000})";
        }

        static bool WasTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.F9);
#else
            return false;
#endif
        }

        static void DestroyRuntimeMaterial(Material material)
        {
            if (material == null)
                return;

            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);
        }

        struct DebugPoints
        {
            public Vector3 RigRoot;
            public Vector3 CameraGround;
            public Vector3 ControllerGround;
            public Vector3 CameraLocalInRig;
        }
    }
}
#endif
