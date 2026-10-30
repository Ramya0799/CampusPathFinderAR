// PathFinder – turns a NavMesh path into spoken-style directions ("Turn left, then go 12 m").
using System.Collections.Generic;
using UnityEngine;

namespace PathFinderNav
{
    public struct PFManeuver
    {
        public float dist;     // distance along the route where this instruction applies (m)
        public string text;
        public bool vertical;  // stairs / lift / steps
    }

    public class PFLinkRecord
    {
        public string kind;
        public string name;
        public Vector3 a;
        public Vector3 b;
    }

    public static class PFRouteGuide
    {
        public static float[] Cumulative(Vector3[] c)
        {
            var cum = new float[c.Length];
            for (int i = 1; i < c.Length; i++) cum[i] = cum[i - 1] + Vector3.Distance(c[i - 1], c[i]);
            return cum;
        }

        public static bool IsVertical(Vector3 a, Vector3 b, Vector3 up)
        {
            Vector3 d = b - a;
            float dv = Vector3.Dot(d, up);
            float dh = Vector3.ProjectOnPlane(d, up).magnitude;
            return Mathf.Abs(dv) > 0.45f && Mathf.Abs(dv) > 0.2f * dh;
        }

        public static string FloorName(float rl, PFFloor[] floors, string preferBuilding)
        {
            PFFloor best = null;
            float bestD = float.MaxValue;
            for (int pass = 0; pass < 2 && best == null; pass++)
            {
                foreach (var f in floors)
                {
                    if (pass == 0 && preferBuilding != null && f.building != preferBuilding) continue;
                    float d = Mathf.Abs(f.rl - rl);
                    if (d < bestD) { bestD = d; best = f; }
                }
                if (pass == 0 && bestD > 1.2f) { best = null; bestD = float.MaxValue; }
            }
            return best != null ? best.name : "next floor";
        }

        static PFLinkRecord MatchLink(Vector3 s, Vector3 e, List<PFLinkRecord> links)
        {
            PFLinkRecord best = null;
            float bestD = 7f;
            foreach (var L in links)
            {
                float d1 = Vector3.Distance(s, L.a) + Vector3.Distance(e, L.b);
                float d2 = Vector3.Distance(s, L.b) + Vector3.Distance(e, L.a);
                float d = Mathf.Min(d1, d2);
                if (d < bestD) { bestD = d; best = L; }
            }
            return best;
        }

        public static List<PFManeuver> Build(Vector3[] c, PFCampusFrame frame, PFFloor[] floors,
                                             List<PFLinkRecord> links, PFDestination dest)
        {
            var list = new List<PFManeuver>();
            if (c == null || c.Length < 2) return list;
            Vector3 up = frame.upUnit;
            float[] cum = Cumulative(c);
            int n = c.Length;
            list.Add(new PFManeuver { dist = 0f, text = "Follow the arrows", vertical = false });
            bool havePrev = false;
            Vector3 prevDir = Vector3.zero;
            int i = 0;
            while (i < n - 1)
            {
                if (IsVertical(c[i], c[i + 1], up))
                {
                    int j = i;
                    while (j < n - 1 && IsVertical(c[j], c[j + 1], up)) j++;
                    float rl0 = frame.RL(c[i]);
                    float rl1 = frame.RL(c[j]);
                    PFLinkRecord L = MatchLink(c[i], c[j], links);
                    string building = dest != null ? dest.building : null;
                    if (L != null && L.name.StartsWith("Teaching")) building = "Teaching Block";
                    if (L != null && L.name.StartsWith("Admin")) building = "Admin Block";
                    string floorName = FloorName(rl1, floors, building);
                    string what = L != null ? L.name : "stairs";
                    string text;
                    if (Mathf.Abs(rl1 - rl0) < 2.0f)
                        text = (rl1 > rl0 ? "Go up the " : "Go down the ") + (L != null ? L.name : "steps");
                    else if (L != null && L.kind == "lift")
                        text = "Take the " + what + " to the " + floorName;
                    else
                        text = "Take the " + what + (rl1 > rl0 ? " up" : " down") + " to the " + floorName;
                    list.Add(new PFManeuver { dist = cum[i], text = text, vertical = true });
                    havePrev = false;
                    i = j;
                    continue;
                }
                Vector3 dir = Vector3.ProjectOnPlane(c[i + 1] - c[i], up);
                if (dir.sqrMagnitude > 1e-4f)
                {
                    dir.Normalize();
                    if (havePrev)
                    {
                        float ang = Vector3.SignedAngle(prevDir, dir, up);   // + = right turn
                        float a = Mathf.Abs(ang);
                        if (a >= 35f)
                        {
                            string side = ang > 0 ? "right" : "left";
                            string t = a > 140f ? "Turn around" : (a < 60f ? "Bear " + side : "Turn " + side);
                            list.Add(new PFManeuver { dist = cum[i], text = t, vertical = false });
                        }
                    }
                    prevDir = dir;
                    havePrev = true;
                }
                i++;
            }
            string arrive = dest != null ? "You have arrived at " + dest.name : "You have arrived";
            list.Add(new PFManeuver { dist = cum[n - 1], text = arrive, vertical = false });

            // add "then go N m" to horizontal instructions
            for (int k = 0; k < list.Count - 1; k++)
            {
                var m = list[k];
                float seg = list[k + 1].dist - m.dist;
                if (!m.vertical && seg >= 2f)
                {
                    m.text += ", then go " + Mathf.RoundToInt(seg) + " m";
                    list[k] = m;
                }
            }
            return list;
        }
    }

    public static class PFMeshes
    {
        /// <summary>Flat chevron arrow lying in local XZ, pointing +Z, double sided.</summary>
        public static Mesh Chevron(float length, float width)
        {
            float hl = length * 0.5f, hw = width * 0.5f;
            var v = new Vector3[]
            {
                new Vector3(0f, 0f, hl),               // 0 tip
                new Vector3(hw, 0f, -hl * 0.1f),       // 1
                new Vector3(hw, 0f, -hl),              // 2
                new Vector3(0f, 0f, hl * 0.25f),       // 3 inner notch
                new Vector3(-hw, 0f, -hl),             // 4
                new Vector3(-hw, 0f, -hl * 0.1f),      // 5
            };
            int[] top = { 0, 1, 3, 1, 2, 3, 0, 3, 5, 3, 4, 5 };
            var tris = new int[top.Length * 2];
            for (int i = 0; i < top.Length; i += 3)
            {
                tris[i] = top[i]; tris[i + 1] = top[i + 1]; tris[i + 2] = top[i + 2];
                tris[top.Length + i] = top[i]; tris[top.Length + i + 1] = top[i + 2]; tris[top.Length + i + 2] = top[i + 1];
            }
            var m = new Mesh();
            m.name = "PF_Chevron";
            m.vertices = v;
            m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }
    }
}
