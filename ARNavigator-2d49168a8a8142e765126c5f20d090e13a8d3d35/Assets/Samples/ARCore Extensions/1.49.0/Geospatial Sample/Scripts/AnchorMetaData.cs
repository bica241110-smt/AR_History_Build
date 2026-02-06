using UnityEngine;

public class AnchorMetadata : MonoBehaviour
{
    public int PathIndex; // このアンカーが _fullRoutePath の何番目か
    public Quaternion EunRotation; // ★追加: アンカー作成時のEUN回転
}