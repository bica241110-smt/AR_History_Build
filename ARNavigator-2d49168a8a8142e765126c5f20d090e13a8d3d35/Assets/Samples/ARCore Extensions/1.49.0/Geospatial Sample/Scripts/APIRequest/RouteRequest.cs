using Google.XR.ARCoreExtensions;
using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;


public class RouteRequest : MonoBehaviour
{
    public DestinationMeshPlacer destinationPlacer;
    //public MapTileUI mapTileUI;

    private string apiKey;

    const double EarthRadius = 6378137.0;
    const double Deg2Rad = Math.PI / 180.0;

    string baseUrl = "https://graphhopper.com/api/1/route";

    void Start()
    {
        TextAsset keyFile = Resources.Load<TextAsset>("GraphHopperApiKey");
        apiKey = keyFile != null ? keyFile.text.Trim() : ""; // Trim()で余計な改行を除去
    }

    /// <summary>
    /// GraphHopper API を使ってルートを取得するコルーチン
    /// </summary>
    public IEnumerator GetRoute(
        double startLat, double startLon,
        double endLat, double endLon,
        Action<List<Vector2d>, List<RouteInstruction>> onComplete = null)
    {
        // vehicle=car に設定済み
        string url =
            $"{baseUrl}?point={startLat},{startLon}&point={endLat},{endLon}" +
            $"&vehicle=car&locale=ja&instructions=true&points_encoded=false&key={apiKey}";

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                DebugTextManager.Set("Route", $"GraphHopper Error: {req.error}");
                yield break;
            }

            var json = JSON.Parse(req.downloadHandler.text);

            // パスの存在チェック
            if (json["paths"] == null || json["paths"].Count == 0)
            {
                DebugTextManager.Set("Route", "No paths found.");
                yield break;
            }

            var path = json["paths"][0];
            var coords = path["points"]["coordinates"];
            var instructionsJson = path["instructions"]; // ★追加

            List<Vector2d> routePoints = new List<Vector2d>();
            List<RouteInstruction> instructions = new List<RouteInstruction>();

            foreach (var p in coords.Children)
            {
                // ★ float を絶対に使わない（doubleのまま保持）
                // JSONNode.AsDouble は double を返します
                double lon = p[0].AsDouble;
                double lat = p[1].AsDouble;
                routePoints.Add(new Vector2d(lon, lat));
            }

            // ★指示データのパースを追加
            foreach (var i in instructionsJson.Children)
            {
                instructions.Add(new RouteInstruction
                {
                    text = i["text"],
                    sign = i["sign"].AsInt,
                    distance = i["distance"].AsDouble,
                    lastNodeIndex = i["interval"][1].AsInt // interval[1] が終了地点のノード番号
                });
            }

            onComplete?.Invoke(routePoints, instructions);
        }
    }

    // ---------------------------------------------------------
    // ★ 出発点（正確な始点）+ double の route を渡す
    // ---------------------------------------------------------
    public IEnumerator RequestRouteAndPlace(
        double startLat, double startLon,
        double endLat, double endLon,
        string destinationName)
    {
        List<Vector2d> route = null;
        List<RouteInstruction> instructions = null;

        yield return StartCoroutine(GetRoute(startLat, startLon, endLat, endLon, (res, inst) =>
        {
            route = res;
            instructions = inst;
        }));

        if (route == null || route.Count == 0)
        {
            DebugTextManager.Set("Route", "ルート取得失敗");
            yield break;
        }

        if (destinationPlacer != null)
        {
            // 始点の正確な GPS を保持
            destinationPlacer.SetRouteStartCoordinate(startLat, startLon);

            // double → メートル → float 変換は DestinationMeshPlacer のみで行う
            // StartDynamicRoute メソッドを Vector2d 対応に変更する必要があります
            // (DestinationMeshPlacer側の修正に合わせて呼び出しを変更)
            destinationPlacer.StartDynamicRoute(route, instructions, destinationName);
        }
    }
}