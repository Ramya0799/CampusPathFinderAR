// GeoUtils.cs
// Geographic coordinate utilities — a direct port of the web app's geoUtils.ts.
//
// Reference: PathFinderAR-main/frontend/src/features/ar/geoUtils.ts
//
// Coordinate system used:
//   The first GPS fix (or first waypoint) is treated as the ENU origin (0,0,0).
//   All subsequent positions are converted to metres relative to that origin:
//     +X = East
//     +Y = Up  (altitude difference, usually 0 for outdoor flat terrain)
//     -Z = North   (+Z = South, following Three.js / Unity convention)
//
// Limitation:
//   GPS accuracy is typically 3–10 m outdoors and degrades indoors.
//   This class provides outdoor ENU conversion only.  Indoor localisation
//   requires QR checkpoints, BLE, Wi-Fi positioning, or UWB — NOT GPS alone.

using UnityEngine;
using System;

namespace PathFinderAR
{
    public static class GeoUtils
    {
        private const double EarthRadiusMeters = 6371000.0;

        // ── Convert degrees → radians ─────────────────────────────────────
        private static double Rad(double deg) => deg * Math.PI / 180.0;
        private static double Deg(double rad) => rad * 180.0 / Math.PI;

        /// <summary>
        /// Converts a GPS point (lat, lng) to local East-North-Up (ENU) coordinates
        /// in metres relative to an origin point.  Mirrors latLngToLocalENU() in geoUtils.ts.
        ///
        ///   Unity world-space mapping:
        ///     x = East   (positive East of origin)
        ///     y = Up     (altitude delta; pass 0 if terrain is flat)
        ///     z = North  (positive North of origin — NOTE: Three.js uses -Z for North, Unity uses +Z)
        ///
        ///  We use +Z = North throughout this project to match Unity's left-handed convention.
        /// </summary>
        public static Vector3 LatLngToENU(
            double originLat, double originLng,
            double pointLat,  double pointLng,
            double pointAltDelta = 0.0)
        {
            double dLat   = Rad(pointLat  - originLat);
            double dLng   = Rad(pointLng  - originLng);
            double meanLat = Rad((originLat + pointLat) / 2.0);

            float east  = (float)(EarthRadiusMeters * dLng * Math.Cos(meanLat));  // +X
            float north = (float)(EarthRadiusMeters * dLat);                       // +Z  (north-positive)
            float up    = (float)pointAltDelta;                                     // +Y

            return new Vector3(east, up, north);
        }

        /// <summary>
        /// Haversine great-circle distance in metres between two GPS coordinates.
        /// Mirrors distanceMeters() in geoUtils.ts.
        /// </summary>
        public static float HaversineMeters(
            double lat1, double lng1,
            double lat2, double lng2)
        {
            double dLat = Rad(lat2 - lat1);
            double dLng = Rad(lng2 - lng1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2))
                     * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
            return (float)(EarthRadiusMeters * c);
        }

        /// <summary>
        /// Compass bearing in degrees (0 = North, 90 = East, 180 = South, 270 = West)
        /// from point A to point B.  Mirrors bearingBetween() in geoUtils.ts.
        /// </summary>
        public static float BearingDegrees(
            double lat1, double lng1,
            double lat2, double lng2)
        {
            double dLng = Rad(lng2 - lng1);
            double rlat1 = Rad(lat1);
            double rlat2 = Rad(lat2);
            double y = Math.Sin(dLng) * Math.Cos(rlat2);
            double x = Math.Cos(rlat1) * Math.Sin(rlat2)
                     - Math.Sin(rlat1) * Math.Cos(rlat2) * Math.Cos(dLng);
            return (float)((Deg(Math.Atan2(y, x)) + 360.0) % 360.0);
        }

        /// <summary>
        /// Returns the relative bearing from the device heading to the target bearing,
        /// normalised to -180 … +180.
        ///   Negative = turn left.  Positive = turn right.
        /// Mirrors normalizeRelativeBearing() in shared/routing.ts.
        /// </summary>
        public static float RelativeBearing(float targetBearing, float deviceHeading)
        {
            return ((targetBearing - deviceHeading + 540f) % 360f) - 180f;
        }

        /// <summary>
        /// Gives a simple human-readable turn label for a relative bearing angle.
        /// </summary>
        public static string TurnLabel(float relativeBearing)
        {
            float abs = Mathf.Abs(relativeBearing);
            if (abs <= 20f)   return "Continue straight";
            if (abs >= 145f)  return "Turn around";
            return relativeBearing > 0f ? "Turn right" : "Turn left";
        }
    }
}
