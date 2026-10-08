using System.Text.Json.Serialization;

namespace LaunchStage.Core.Models;

/// <summary>A rectangle in screen pixels.</summary>
public sealed class Rect
{
    public Rect()
    {
    }

    public Rect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    [JsonIgnore] public int Right => X + Width;
    [JsonIgnore] public int Bottom => Y + Height;
    [JsonIgnore] public int CenterX => X + Width / 2;
    [JsonIgnore] public int CenterY => Y + Height / 2;

    public bool Contains(int px, int py) => px >= X && px < Right && py >= Y && py < Bottom;

    public bool SameSize(Rect other) => Width == other.Width && Height == other.Height;

    public bool SamePlace(Rect other) => X == other.X && Y == other.Y && SameSize(other);

    public Rect Copy() => new(X, Y, Width, Height);

    public override string ToString() => $"{Width}x{Height} at ({X},{Y})";
}
