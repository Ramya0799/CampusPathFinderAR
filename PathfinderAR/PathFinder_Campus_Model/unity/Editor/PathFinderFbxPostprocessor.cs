// PathFinder: copies Blender custom properties stored in the PathFinder_*.fbx files onto a
// PathFinderNodeInfo component, so room names, floor ids and navigation-anchor data are
// available at runtime. Place in an Editor folder, e.g. Assets/Scripts/PathFinder/Editor/.
// NOTE: written for the PathFinder FBX export; not compiled in the environment that generated it.
using UnityEditor;
using UnityEngine;

public class PathFinderFbxPostprocessor : AssetPostprocessor
{
    void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] names, object[] values)
    {
        if (!assetPath.Contains("PathFinder_")) return;
        var info = go.GetComponent<PathFinderNodeInfo>();
        if (info == null) info = go.AddComponent<PathFinderNodeInfo>();
        for (int i = 0; i < names.Length; i++)
            info.Set(names[i], values[i] != null ? values[i].ToString() : "");
    }
}
