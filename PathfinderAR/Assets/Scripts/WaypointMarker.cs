// WaypointMarker.cs
// A single 3D AR waypoint indicator placed in the Unity scene.
// Uses only built-in Unity primitives - no TextMeshPro dependency.
// The ARNavigator script creates one of these per route waypoint.
//
// Visual design:
//   • A vertical cylinder pole in PathFinder blue.
//   • A sphere beacon on top that pulses for the active (next) waypoint.
//   • A world-space canvas label showing the waypoint name.

using UnityEngine;
using UnityEngine.UI;

namespace PathFinderAR
{
    public class WaypointMarker : MonoBehaviour
    {
        [Header("Visual")]
        public Color InactiveColor = new Color(0.15f, 0.55f, 0.95f, 0.8f);  // PathFinder blue
        public Color ActiveColor   = new Color(0.0f,  0.95f, 0.45f, 1.0f);  // Bright green
        public Color ArrivedColor  = new Color(1.0f,  0.75f, 0.0f,  1.0f);  // Gold
        public float PulseSpeed    = 2.5f;

        private GameObject _poleObject;
        private GameObject _beaconObject;
        private Text       _labelText;       // UnityEngine.UI.Text (no TMP needed)

        private bool  _isActive;
        private bool  _isArrived;
        private float _pulseSeed;

        // ── Unity lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            _pulseSeed = Random.value * Mathf.PI * 2f;

            // Cylinder pole
            _poleObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _poleObject.name = "Pole";
            _poleObject.transform.SetParent(transform);
            _poleObject.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            _poleObject.transform.localScale    = new Vector3(0.06f, 0.7f, 0.06f);
            RemoveCollider(_poleObject);

            // Sphere beacon
            _beaconObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _beaconObject.name = "Beacon";
            _beaconObject.transform.SetParent(transform);
            _beaconObject.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            _beaconObject.transform.localScale    = new Vector3(0.25f, 0.25f, 0.25f);
            RemoveCollider(_beaconObject);

            // World-space Canvas label
            CreateLabel();

            Refresh();
        }

        private void CreateLabel()
        {
            // World-space canvas so it renders in 3D space
            var canvasGO = new GameObject("LabelCanvas");
            canvasGO.transform.SetParent(transform);
            canvasGO.transform.localPosition = new Vector3(0f, 2.1f, 0f);
            canvasGO.transform.localScale    = Vector3.one * 0.01f; // scale down canvas units

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rectTransform = canvasGO.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(300f, 80f);

            // Background panel
            var bg = new GameObject("Background");
            bg.transform.SetParent(canvasGO.transform, false);
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.6f);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = bgRect.offsetMax = Vector2.zero;

            // Text label
            var textGO = new GameObject("Label");
            textGO.transform.SetParent(canvasGO.transform, false);
            _labelText = textGO.AddComponent<Text>();
            _labelText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _labelText.fontSize  = 28;
            _labelText.color     = Color.white;
            _labelText.alignment = TextAnchor.MiddleCenter;
            _labelText.text      = "";
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(4f, 4f);
            textRect.offsetMax = new Vector2(-4f, -4f);
        }

        // ── Update ─────────────────────────────────────────────────────────

        private void Update()
        {
            // Always face label canvas toward camera
            if (_labelText != null && Camera.main != null)
            {
                var canvasParent = _labelText.canvas.transform;
                canvasParent.LookAt(Camera.main.transform.position);
                // Flip so text faces camera, not away
                canvasParent.Rotate(0f, 180f, 0f);
            }

            // Pulse beacon for the active (next) waypoint
            if (_isActive && !_isArrived && _beaconObject != null)
            {
                float scale = 1f + 0.3f * Mathf.Sin(Time.time * PulseSpeed + _pulseSeed);
                _beaconObject.transform.localScale = Vector3.one * 0.25f * scale;
            }
        }

        // ── Public API ─────────────────────────────────────────────────────

        public void SetWaypointName(string waypointName)
        {
            if (_labelText != null) _labelText.text = waypointName;
        }

        public void SetActive(bool active)
        {
            _isActive = active;
            Refresh();
        }

        public void SetArrived(bool arrived)
        {
            _isArrived = arrived;
            Refresh();
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private void Refresh()
        {
            Color c = _isArrived ? ArrivedColor : (_isActive ? ActiveColor : InactiveColor);
            ApplyColor(_poleObject,   c);
            ApplyColor(_beaconObject, c);
        }

        private static void ApplyColor(GameObject go, Color color)
        {
            if (go == null) return;
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            // Instance material to avoid modifying the shared material
            renderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit")
                                          ?? Shader.Find("Standard"));
            renderer.material.color = color;
        }

        private static void RemoveCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }
    }
}
