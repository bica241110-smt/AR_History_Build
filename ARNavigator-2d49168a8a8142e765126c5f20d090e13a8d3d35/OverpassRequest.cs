using SimpleJSON;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Google.XR.ARCoreExtensions.Samples.Geospatial;

public class OverpassRequest : MonoBehaviour
{
    public static OverpassRequest instance { get; private set; }

    public OSRMRouteRequest routeRequest;
    public TargetRenderer targetRenderer;

    public static MapTileUI mapTileUI { get; private set; }

    private void Awake()
    {
        if (instance == null) {
            instance = this;
        } else {
            Destroy(gameObject); 
        }
    }

    /// <summary>
    /// オーケストレータ：現在地から最寄り道路ノードを取得し、
    /// そのノードから最寄りバス停を取得し、結果を表示・ルート取得する。
    /// </summary>
    public IEnumerator FetchBusStopInfoCoroutine(double latitude, double longitude)
    {
        double roadLat = latitude, roadLon = longitude;

        // 1) 現在地から最寄り道路ノードを取得
        yield return StartCoroutine(FetchNearestRoadNodeCoroutine(latitude, longitude, (rLat, rLon) =>
        {
            roadLat = rLat;
            roadLon = rLon;
        }));

        // 2) その道路ノードから最寄りのバス停を取得
        bool foundBus = false;
        string busName = "名前なし";
        double busLat = 0, busLon = 0, busDist = 0, busDir = 0;

        yield return StartCoroutine(FetchNearestBusStopFromNodeCoroutine(roadLat, roadLon, (bLat, bLon, name, dist, dir) =>
        {
            foundBus = true;
            busLat = bLat;
            busLon = bLon;
            busName = name;
            busDist = dist;
            busDir = dir;
        }));

        if (!foundBus)
        {
            // バス停が見つからない場合はここで終了（FetchNearestBusStop... 内で表示済み）
            yield break;
        }

        // 3) 結果を用いて表示・ルート・地図読み込みなどを行う
        yield return StartCoroutine(HandleBusStopAndRouteCoroutine(roadLat, roadLon, busLat, busLon, busName, busDist, busDir));
    }

    /// <summary>
    /// 指定位置から最寄りの道路ノード(lat, lon) を取得して onComplete に返す。
    /// ノードが見つからなければ、元の座標を返す。
    /// </summary>
    public IEnumerator FetchNearestRoadNodeCoroutine(double latitude, double longitude, Action<double, double> onComplete)
    {
        string roadQuery = $@"
        [out:json];
        node(around:200,{latitude},{longitude})[""highway""];
        out body;";

        var form = new WWWForm();
        form.AddField("data", roadQuery);

        if (targetRenderer != null)
        {
            targetRenderer.AppendApiLog($"Overpass request payload:\n{roadQuery}");
        }

        using (UnityWebRequest www = UnityWebRequest.Post("https://overpass-api.de/api/interpreter", form))
        {
            yield return www.SendWebRequest();

            long responseCode = www.responseCode;
            string responseBody = www.downloadHandler != null ? www.downloadHandler.text : string.Empty;

            if (www.result != UnityWebRequest.Result.Success)
            {
                string err = "APIエラー: " + www.error;
                Debug.LogError(err);

                if (targetRenderer != null)
                {
                    string full = $"Overpass ERROR\nURL: https://overpass-api.de/api/interpreter\nResponseCode: {responseCode}\nError: {www.error}\nBody:\n{responseBody}";
                    targetRenderer.AppendApiLog(full);
                    targetRenderer.Display(err);
                }

                // ノードが取れなければ元座標を返す
                onComplete?.Invoke(latitude, longitude);
                yield break;
            }

            if (targetRenderer != null)
            {
                string full = $"Overpass RESPONSE\nURL: https://overpass-api.de/api/interpreter\nResponseCode: {responseCode}\nBody:\n{responseBody}";
                targetRenderer.AppendApiLog(full);
            }

            var parsed = JSON.Parse(responseBody);
            var roads = parsed["elements"];

            if (roads == null || roads.Count == 0)
            {
                // 見つからない場合は元の座標を返す
                onComplete?.Invoke(latitude, longitude);
                yield break;
            }

            double bestDistSq = double.MaxValue;
            double bestLat = latitude;
            double bestLon = longitude;

            for (int i = 0; i < roads.Count; i++)
            {
                double rlat = roads[i]["lat"];
                double rlon = roads[i]["lon"];

                double distSq = GeoCalculator.GetDistanceSquared(latitude, longitude, rlat, rlon);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestLat = rlat;
                    bestLon = rlon;
                }
            }

            onComplete?.Invoke(bestLat, bestLon);
        }
    }

    /// <summary>
    /// 指定ノード位置から最寄りのバス停(lat, lon, name, distanceMeters, direction) を取得して onComplete に返す。
    /// 見つからない場合は targetRenderer にメッセージを表示して終了する。
    /// </summary>
    public IEnumerator FetchNearestBusStopFromNodeCoroutine(double nodeLat, double nodeLon, Action<double, double, string, double, double> onComplete)
    {
        // クエリ組み立て（ノード周辺からバス停を探す）
        string query = $@"
        [out:json];
        node(around:2000,{nodeLat},{nodeLon})[highway=bus_stop];
        out body;";

        var form = new WWWForm();
        form.AddField("data", query);

        if (targetRenderer != null)
        {
            targetRenderer.AppendApiLog($"Overpass request payload:\n{query}");
        }

        using (UnityWebRequest www = UnityWebRequest.Post("https://overpass-api.de/api/interpreter", form))
        {
            yield return www.SendWebRequest();

            long responseCode = www.responseCode;
            string responseBody = www.downloadHandler != null ? www.downloadHandler.text : string.Empty;

            if (www.result != UnityWebRequest.Result.Success)
            {
                string err = "APIエラー: " + www.error;
                Debug.LogError(err);

                if (targetRenderer != null)
                {
                    string full = $"Overpass ERROR\nURL: https://overpass-api.de/api/interpreter\nResponseCode: {responseCode}\nError: {www.error}\nBody:\n{responseBody}";
                    targetRenderer.AppendApiLog(full);
                    targetRenderer.Display(err);
                }
                yield break;
            }

            if (targetRenderer != null)
            {
                string full = $"Overpass RESPONSE\nURL: https://overpass-api.de/api/interpreter\nResponseCode: {responseCode}\nBody:\n{responseBody}";
                targetRenderer.AppendApiLog(full);
            }

            var parsed = JSON.Parse(responseBody);
            var elements = parsed["elements"];

            if (elements == null || elements.Count == 0)
            {
                if (targetRenderer != null)
                    targetRenderer.Display("近くにバス停が見つかりません。");
                yield break;
            }

            double nearestDistSq = double.MaxValue;
            string nearestName = "名前なし";
            double nearestLat = 0, nearestLon = 0;

            for (int i = 0; i < elements.Count; i++)
            {
                var node = elements[i];
                double lat = node["lat"];
                double lon = node["lon"];

                string name = "名前なし";
                if (node["tags"] != null && node["tags"]["name"] != null)
                    name = node["tags"]["name"];

                double distSq = GeoCalculator.GetDistanceSquared(nodeLat, nodeLon, lat, lon);
                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearestName = name;
                    nearestLat = lat;
                    nearestLon = lon;
                }
            }

            double nearestDist = GeoCalculator.GetDistance(nodeLat, nodeLon, nearestLat, nearestLon);
            double nearestDir = GeoCalculator.GetDirection(nodeLat, nodeLon, nearestLat, nearestLon);

            // callback に結果を返す
            onComplete?.Invoke(nearestLat, nearestLon, nearestName, nearestDist, nearestDir);
        }
    }

    /// <summary>
    /// 取得済みの道路ノード位置とバス停位置および情報を使って表示・ルート取得・地図読み込みを行う。
    /// roadLat/roadLon は道路ノード（経度・緯度の順で内部使用する箇所に注意）
    /// </summary>
    public IEnumerator HandleBusStopAndRouteCoroutine(double roadLat, double roadLon, double busLat, double busLon, string name, double distanceMeters, double direction)
    {
        // ルート取得（start: road, end: bus）
        if (routeRequest != null)
        {
            // OSRM/Graph 呼び出しは lon, lat の順が期待されるため注意
            yield return StartCoroutine(routeRequest.RequestRouteAndPlace(roadLon, roadLat, busLon, busLat));
        }

        // GeospatialController に目的地を配置（経度, 緯度 の順）
        var geoCtrl = UnityEngine.Object.FindFirstObjectByType<Google.XR.ARCoreExtensions.Samples.Geospatial.GeospatialController>();
        if (geoCtrl != null)
        {
            geoCtrl.PlaceDestinationFromLatLon(roadLon, roadLat, double.NaN, direction);
        }

        if (targetRenderer != null)
        {
            targetRenderer.DisplayBusStop(
                roadLat,
                roadLon,
                name,
                distanceMeters,
                direction,
                busLat,
                busLon);

            if (routeRequest != null)
            {
                if (mapTileUI != null)
                {
                    yield return StartCoroutine(mapTileUI.LoadMapTile(roadLat, roadLon));
                }
                yield return StartCoroutine(routeRequest.RequestRouteAndPlace(roadLon, roadLat, busLon, busLat));
            }
        }
    }
}