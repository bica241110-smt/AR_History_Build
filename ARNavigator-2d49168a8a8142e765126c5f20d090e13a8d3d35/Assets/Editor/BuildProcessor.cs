#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

public class BuildProcessor : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        // 1. Windowsの環境変数を読み込む
        string graphHopperApiKey = System.Environment.GetEnvironmentVariable("GRAPHHOPPER_API_KEY");
        string placesApiKey = System.Environment.GetEnvironmentVariable("PLACES_API_KEY");

        if (string.IsNullOrEmpty(graphHopperApiKey))
        {
            graphHopperApiKey = "DUMMY_KEY"; // エラー回避用
        }
        if (string.IsNullOrEmpty(placesApiKey))
        {
            placesApiKey = "DUMMY_KEY"; // エラー回避用
        }

        // 2. Resourcesフォルダに一時ファイルとして保存する
        // (実行時に読み込むためのファイル)
        string graphHopperPath = Path.Combine(Application.dataPath, "Resources/GraphHopperApiKey.txt");
        File.WriteAllText(graphHopperPath, graphHopperApiKey);

        string placesPath = Path.Combine(Application.dataPath, "Resources/PlacesApiKey.txt");
        File.WriteAllText(placesPath, placesApiKey);

        AssetDatabase.Refresh();
        Debug.Log("環境変数からAPIキーを埋め込みました。");
    }
}
#endif