namespace JinYong.Host;

public enum EndingVisual { SceneFadeOut, Title, Words, Movie, Credits, Black }

/// <summary>Display-only projection; Index is the zero-based native scroll/movie frame.</summary>
public sealed record EndingFrameModel(EndingVisual Visual, int Index = 0, float Opacity = 1, bool WaitForKey = false);
