// DestinationSelectorUI.cs
// In-game destination picker that reads live data from GET /api/destinations.
// Falls back to a hardcoded accessible subset when the server is unreachable.
//
// Uses only UnityEngine.UI (Text, Button, Dropdown) — no TextMeshPro dependency.
//
// Setup in Unity:
//   1. Create a Canvas (Screen Space - Camera).
//   2. Inside it add: a Dropdown, a "Navigate" Button, a "Close" Button,
//      an "Open" Button (floating), a Text label, and a Panel to group them.
//   3. Assign all references in the Inspector on this component.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PathFinderAR
{
    public class DestinationSelectorUI : MonoBehaviour
    {
        [Header("Dependencies")]
        public ARNavigator         Navigator;
        public PathFinderAPIClient APIClient;

        [Header("UI References (UnityEngine.UI)")]
        public Dropdown    DestinationDropdown;
        public Button      NavigateButton;
        public Button      CloseButton;
        public Button      OpenButton;     // Floating button to re-open the panel
        public GameObject  PanelRoot;     // Parent panel to show/hide
        public Text        StatusLabel;   // UnityEngine.UI.Text

        [Header("Start Node")]
        [Tooltip("Node ID used as route origin. In production, snap to nearest GPS node.")]
        public string DefaultStartNodeId = "gate";

        // ── Internal state ─────────────────────────────────────────────────
        private DestinationItem[] _destinations;
        private int               _selectedIndex;

        // ── Unity lifecycle ────────────────────────────────────────────────

        private void Start()
        {
            if (PanelRoot    != null) PanelRoot.SetActive(true);
            if (NavigateButton != null) NavigateButton.onClick.AddListener(OnNavigateClicked);
            if (CloseButton    != null) CloseButton.onClick.AddListener(() => SetPanelVisible(false));
            if (OpenButton     != null) OpenButton.onClick.AddListener(() => SetPanelVisible(true));
            if (DestinationDropdown != null)
                DestinationDropdown.onValueChanged.AddListener(OnDropdownChanged);

            if (APIClient != null)
            {
                APIClient.OnDestinationsSuccess += OnDestinationsLoaded;
                APIClient.OnDestinationsError   += OnDestinationsError;
                SetStatus("Loading destinations…");
                APIClient.FetchDestinations();
            }
            else
            {
                SetStatus("API client not assigned. Using offline list.");
                LoadHardcodedDestinations();
            }
        }

        private void OnDestroy()
        {
            if (APIClient != null)
            {
                APIClient.OnDestinationsSuccess -= OnDestinationsLoaded;
                APIClient.OnDestinationsError   -= OnDestinationsError;
            }
        }

        // ── API callbacks ──────────────────────────────────────────────────

        private void OnDestinationsLoaded(DestinationListResponse response)
        {
            if (response?.destinations == null || response.destinations.Length == 0)
            {
                SetStatus("No destinations from server. Using offline list.");
                LoadHardcodedDestinations();
                return;
            }
            _destinations = response.destinations;
            PopulateDropdown(_destinations);
            SetStatus($"{response.total} destinations loaded. Select one and tap Navigate.");
        }

        private void OnDestinationsError(string errorMessage)
        {
            Debug.LogWarning("[DestinationSelectorUI] " + errorMessage);
            SetStatus("Could not reach server. Using offline list.");
            LoadHardcodedDestinations();
        }

        // ── UI actions ─────────────────────────────────────────────────────

        private void OnDropdownChanged(int index) => _selectedIndex = index;

        private void OnNavigateClicked()
        {
            if (_destinations == null || _destinations.Length == 0)
            {
                SetStatus("No destination selected.");
                return;
            }
            if (_selectedIndex < 0 || _selectedIndex >= _destinations.Length)
            {
                SetStatus("Invalid selection.");
                return;
            }

            var dest = _destinations[_selectedIndex];
            if (Navigator == null)
            {
                Debug.LogError("[DestinationSelectorUI] ARNavigator not assigned.");
                return;
            }

            Navigator.StartNodeId       = DefaultStartNodeId;
            Navigator.DestinationNodeId = dest.nodeId;   // e.g. "library", "cse-lab"

            Debug.Log($"[DestinationSelectorUI] Navigate {DefaultStartNodeId} → {dest.nodeId} ({dest.name})");
            SetPanelVisible(false);
            Navigator.BeginNavigation();
        }

        private void SetPanelVisible(bool visible)
        {
            if (PanelRoot != null) PanelRoot.SetActive(visible);
            if (OpenButton != null) OpenButton.gameObject.SetActive(!visible);
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private void PopulateDropdown(DestinationItem[] items)
        {
            if (DestinationDropdown == null) return;
            DestinationDropdown.ClearOptions();
            var options = new List<Dropdown.OptionData>();
            foreach (var item in items)
                options.Add(new Dropdown.OptionData($"{item.name} — {item.buildingName}"));
            DestinationDropdown.AddOptions(options);
            _selectedIndex = 0;
        }

        /// <summary>
        /// Offline fallback — mirrors the accessible outdoor nodes in pathfinder.ts.
        /// </summary>
        private void LoadHardcodedDestinations()
        {
            _destinations = new[]
            {
                new DestinationItem { id = "d1",  nodeId = "gate",         name = "Main Gate",                buildingName = "Campus Grounds" },
                new DestinationItem { id = "d2",  nodeId = "admin-office", name = "Administration Office",    buildingName = "Administration Block" },
                new DestinationItem { id = "d4",  nodeId = "exam",         name = "Examination Cell",         buildingName = "Administration Block" },
                new DestinationItem { id = "d5",  nodeId = "cs-dept",      name = "Computer Science Dept.",   buildingName = "Computing Block" },
                new DestinationItem { id = "d6",  nodeId = "cse-lab",      name = "CSE Laboratory",           buildingName = "Computing Block" },
                new DestinationItem { id = "d10", nodeId = "library",      name = "Central Library",          buildingName = "Learning Block" },
                new DestinationItem { id = "d12", nodeId = "auditorium",   name = "Auditorium",               buildingName = "Learning Block" },
                new DestinationItem { id = "d13", nodeId = "cafe",         name = "Cafeteria",                buildingName = "Learning Block" },
                new DestinationItem { id = "d14", nodeId = "hostel",       name = "Student Hostel",           buildingName = "Campus Grounds" },
                new DestinationItem { id = "d16", nodeId = "sports",       name = "Sports Complex",           buildingName = "Campus Grounds" },
            };
            PopulateDropdown(_destinations);
        }

        private void SetStatus(string msg)
        {
            if (StatusLabel != null) StatusLabel.text = msg;
            Debug.Log("[DestinationSelectorUI] " + msg);
        }
    }
}
