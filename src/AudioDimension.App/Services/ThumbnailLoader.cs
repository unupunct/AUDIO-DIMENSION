using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AudioDimension.Presets;
using Serilog;

namespace AudioDimension.App.Services;

/// <summary>Loads preset thumbnails: built-ins from embedded resources, custom presets from the user's preset folder.</summary>
public static class ThumbnailLoader
{
    private static readonly Dictionary<string, ImageSource?> Cache = new();

    public static byte[]? LoadBuiltInBytes(string id)
    {
        try
        {
            var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Thumbnails/{id}.png"));
            if (info is null) return null;
            using var ms = new MemoryStream(); info.Stream.CopyTo(ms); return ms.ToArray();
        }
        catch (Exception ex) { Log.Debug(ex, "No embedded thumbnail for {Id}", id); return null; }
    }

    public static ImageSource? Load(PresetDefinition p, bool forceReload = false)
    {
        string key = p.Id;
        if (!p.IsCustom && !forceReload && Cache.TryGetValue(key, out var hit)) return hit;
        ImageSource? img = null;
        try
        {
            byte[]? bytes = p.IsCustom ? (File.Exists(App.Presets.ThumbnailPath(p.Id)) ? File.ReadAllBytes(App.Presets.ThumbnailPath(p.Id)) : null) : LoadBuiltInBytes(p.Id);
            if (bytes is not null)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = new MemoryStream(bytes); bmp.EndInit(); bmp.Freeze();
                img = bmp;
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Could not load thumbnail for {Id}", p.Id); }
        if (!p.IsCustom) Cache[key] = img;
        return img;
    }
}
