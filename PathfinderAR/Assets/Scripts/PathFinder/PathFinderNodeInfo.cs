// PathFinder: metadata copied from the FBX custom properties (roomName, roomType, floorId, rl, ...).
// Put this file anywhere under Assets/ that is NOT an Editor folder, e.g. Assets/Scripts/PathFinder/.
// NOTE: written for the PathFinder FBX export; not compiled in the environment that generated it.
using System.Collections.Generic;
using UnityEngine;

public class PathFinderNodeInfo : MonoBehaviour
{
    [System.Serializable]
    public struct Prop { public string key; public string value; }

    public List<Prop> props = new List<Prop>();

    public void Set(string key, string value)
    {
        props.RemoveAll(p => p.key == key);
        props.Add(new Prop { key = key, value = value });
    }

    public string Get(string key, string fallback = "")
    {
        foreach (var p in props) if (p.key == key) return p.value;
        return fallback;
    }

    public string PfType => Get("pfType");      // floor, walls, slab, roomZone, navAnchor, stair, ...
    public string FloorId => Get("floorId");
    public string RoomName => Get("roomName");
}
