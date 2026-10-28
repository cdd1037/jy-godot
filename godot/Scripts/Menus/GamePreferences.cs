using Godot;

namespace JinYong.Host;

public sealed record GamePreferences(bool SmoothMovement = false, bool MusicEnabled = true)
{
    public static GamePreferences Load()
    {
        using var file = new ConfigFile();
        if (file.Load("user://settings.cfg") != Error.Ok) return new();
        return new(file.GetValue("game", "smooth_movement", false).AsBool(),
            file.GetValue("audio", "music_enabled", true).AsBool());
    }

    public void Save()
    {
        using var file = new ConfigFile();
        file.SetValue("game", "smooth_movement", SmoothMovement);
        file.SetValue("audio", "music_enabled", MusicEnabled);
        var result = file.Save("user://settings.cfg");
        if (result != Error.Ok) throw new IOException($"无法保存设置：{result}");
    }
}
