// PathFinder – data classes for Resources/PathFinderNavData.json and the campus coordinate frame.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PathFinderNav
{
    [Serializable]
    public class PFFloor
    {
        public string id;
        public string name;
        public string shortName;
        public string building;
        public float rl;
    }

    [Serializable]
    public class PFDestination
    {
        public string id;
        public string name;
        public string type;
        public string floor;
        public string building;
        public string floorName;
        public float x;
        public float y;
        public float rl;
        public bool approximate;
    }

    [Serializable]
    public class PFLink
    {
        public string kind;      // stairs, lift, steps
        public string name;
        public string fromFloor;
        public string toFloor;
        public float ax, ay, arl;
        public float bx, by, brl;
        public bool approximate;
        public string note;
    }

    [Serializable]
    public class PFStartPoint
    {
        public string name;
        public string description;
        public string floor;
        public float x, y, rl;
        public float fx, fy;     // facing direction in drawing axes
        public bool approximate;
    }

    [Serializable]
    public class PFNavDataFile
    {
        public int version;
        public PFFloor[] floors;
        public PFDestination[] destinations;
        public PFLink[] links;
        public PFStartPoint[] startPoints;
        public PFFrameRef[] frameRefs;
    }

    [Serializable]
    public class PFFrameRef
    {
        public string name;
        public float x, y, z;    // model-space centre of the mesh bounding box (z = RL - 60)
    }

    /// <summary>
    /// Maps model coordinates (x = drawing right, y = drawing up, z = RL - 60) to Unity world positions.
    /// The mapping is solved from the bounding-box centres of known meshes in the imported campus, so it
    /// is correct whatever axis conversion, rotation or mirroring the FBX import or the user applied.
    /// </summary>
    public class PFCampusFrame
    {
        public const float RlDatum = 60f;
        public Matrix4x4 M = Matrix4x4.identity;      // model -> world
        public Matrix4x4 Minv = Matrix4x4.identity;   // world -> model
        public Vector3 origin, right, planUp, up, upUnit, rightUnit, planUpUnit;
        public bool valid;
        public bool mirrored;
        public int refsUsed;
        public float rms;
        public string method = "";
        public string error = "";

        public static PFCampusFrame FromMatrix(Matrix4x4 modelToWorld, string method)
        {
            var f = new PFCampusFrame();
            f.M = modelToWorld;
            f.method = method;
            f.Finish();
            return f;
        }

        public static PFCampusFrame Solve(Transform root, PFFrameRef[] refs)
        {
            var f = new PFCampusFrame();
            var src = new List<Vector3>();
            var dst = new List<Vector3>();
            if (refs != null)
            {
                foreach (var r in refs)
                {
                    Transform t = FindDeep(root, r.name);
                    if (t == null) continue;
                    Renderer rend = t.GetComponent<Renderer>();
                    if (rend == null) continue;
                    src.Add(new Vector3(r.x, r.y, r.z));
                    dst.Add(rend.bounds.center);
                }
            }
            if (src.Count >= 4 && f.FitAffine(src, dst))
            {
                f.method = "meshes";
            }
            else if (!f.FromMarkers(root))
            {
                f.valid = false;
                if (f.error.Length == 0) f.error = "Could not locate the campus model (reference meshes and markers missing).";
                return f;
            }
            f.Finish();
            return f;
        }

        bool FitAffine(List<Vector3> src, List<Vector3> dst)
        {
            // least squares: w = A m + t, solved per world axis with 4x4 normal equations
            var N = new double[4, 4];
            var R = new double[4, 3];
            for (int i = 0; i < src.Count; i++)
            {
                double[] p = { src[i].x, src[i].y, src[i].z, 1.0 };
                double[] w = { dst[i].x, dst[i].y, dst[i].z };
                for (int a = 0; a < 4; a++)
                {
                    for (int b = 0; b < 4; b++) N[a, b] += p[a] * p[b];
                    for (int k = 0; k < 3; k++) R[a, k] += p[a] * w[k];
                }
            }
            double[,] X;
            if (!SolveLinear(N, R, out X)) return false;
            var m = Matrix4x4.identity;
            m.m00 = (float)X[0, 0]; m.m01 = (float)X[1, 0]; m.m02 = (float)X[2, 0]; m.m03 = (float)X[3, 0];
            m.m10 = (float)X[0, 1]; m.m11 = (float)X[1, 1]; m.m12 = (float)X[2, 1]; m.m13 = (float)X[3, 1];
            m.m20 = (float)X[0, 2]; m.m21 = (float)X[1, 2]; m.m22 = (float)X[2, 2]; m.m23 = (float)X[3, 2];
            M = m;
            double se = 0;
            for (int i = 0; i < src.Count; i++) se += (M.MultiplyPoint3x4(src[i]) - dst[i]).sqrMagnitude;
            rms = (float)Math.Sqrt(se / src.Count);
            refsUsed = src.Count;
            return M.determinant != 0f;
        }

        static bool SolveLinear(double[,] A, double[,] B, out double[,] X)
        {
            int n = 4, m = 3;
            var a = (double[,])A.Clone();
            var b = (double[,])B.Clone();
            for (int c = 0; c < n; c++)
            {
                int piv = c;
                for (int r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[piv, c])) piv = r;
                if (Math.Abs(a[piv, c]) < 1e-9) { X = null; return false; }
                if (piv != c)
                {
                    for (int k = 0; k < n; k++) { double t = a[c, k]; a[c, k] = a[piv, k]; a[piv, k] = t; }
                    for (int k = 0; k < m; k++) { double t = b[c, k]; b[c, k] = b[piv, k]; b[piv, k] = t; }
                }
                for (int r = 0; r < n; r++)
                {
                    if (r == c) continue;
                    double f = a[r, c] / a[c, c];
                    if (f == 0) continue;
                    for (int k = c; k < n; k++) a[r, k] -= f * a[c, k];
                    for (int k = 0; k < m; k++) b[r, k] -= f * b[c, k];
                }
            }
            X = new double[n, m];
            for (int r = 0; r < n; r++) for (int k = 0; k < m; k++) X[r, k] = b[r, k] / a[r, r];
            return true;
        }

        bool FromMarkers(Transform root)
        {
            Transform o = FindDeep(root, "PF_Origin");
            Transform r = FindDeep(root, "PF_Marker_PlanRight_10m");
            Transform p = FindDeep(root, "PF_Marker_PlanUp_10m");
            Transform u = FindDeep(root, "PF_Marker_Up_10m");
            if (o == null || r == null || p == null || u == null) return false;
            Vector3 O = o.position;
            Vector3 X = (r.position - O) / 10f, Y = (p.position - O) / 10f, Z = (u.position - O) / 10f;
            var m = Matrix4x4.identity;
            m.m00 = X.x; m.m01 = Y.x; m.m02 = Z.x; m.m03 = O.x;
            m.m10 = X.y; m.m11 = Y.y; m.m12 = Z.y; m.m13 = O.y;
            m.m20 = X.z; m.m21 = Y.z; m.m22 = Z.z; m.m23 = O.z;
            M = m;
            method = "markers";
            return M.determinant != 0f;
        }

        void Finish()
        {
            Minv = M.inverse;
            origin = M.MultiplyPoint3x4(Vector3.zero);
            right = M.MultiplyVector(new Vector3(1f, 0f, 0f));
            planUp = M.MultiplyVector(new Vector3(0f, 1f, 0f));
            up = M.MultiplyVector(new Vector3(0f, 0f, 1f));
            upUnit = up.normalized;
            rightUnit = right.normalized;
            planUpUnit = planUp.normalized;
            // model axes are right-handed; Unity is left-handed, so a non-mirrored import has det < 0
            mirrored = M.determinant > 0f;
            valid = up.sqrMagnitude > 1e-6f;
            if (!valid) error = "Campus model transform is degenerate (check its scale).";
        }

        public Vector3 ToWorld(float x, float y, float rl) { return M.MultiplyPoint3x4(new Vector3(x, y, rl - RlDatum)); }
        public Vector3 FromModel(Vector3 m) { return M.MultiplyPoint3x4(m); }
        public Vector3 ToModel(Vector3 world) { return Minv.MultiplyPoint3x4(world); }
        public Vector3 ToModelDir(Vector3 dir) { return Minv.MultiplyVector(dir); }
        public float RL(Vector3 world) { return RlDatum + ToModel(world).z; }

        public static Transform FindDeep(Transform parent, string name)
        {
            if (parent == null) return null;
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform t = FindDeep(parent.GetChild(i), name);
                if (t != null) return t;
            }
            return null;
        }
    }
}
