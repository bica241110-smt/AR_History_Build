using System;

// Unityのインスペクターで表示したい場合にSerializableをつけます
// (ただし、double型はデフォルトのインスペクターでは編集できませんが、シリアライズは可能です)
[Serializable]
public struct Vector2d
{
    public double x; // Longitude (経度)
    public double y; // Latitude (緯度)

    // コンストラクタ
    public Vector2d(double x, double y)
    {
        this.x = x;
        this.y = y;
    }

    // デバッグログで中身を見やすくするためのToStringオーバーライド
    public override string ToString()
    {
        return $"({x}, {y})";
    }

    // 足し算や引き算ができるように演算子をオーバーロード（あると便利です）
    public static Vector2d operator +(Vector2d a, Vector2d b)
    {
        return new Vector2d(a.x + b.x, a.y + b.y);
    }

    public static Vector2d operator -(Vector2d a, Vector2d b)
    {
        return new Vector2d(a.x - b.x, a.y - b.y);
    }
}