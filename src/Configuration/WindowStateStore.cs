using System.IO;
using System.Text.Json;

namespace taskTru;

internal sealed class WindowStateStore
{
    private const int MaximumSavedStates = 256;

    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "taskTru",
        "state.json");

    private readonly Dictionary<string, WindowState> _states = Read();
    private bool _dirty;

    public WindowState? Get(string key) =>
        _states.GetValueOrDefault(key);

    public void Set(string key, WindowState state)
    {
        if (state.IsDefault)
        {
            _dirty |= _states.Remove(key);
            return;
        }

        if (_states.TryGetValue(key, out WindowState? current)
            && current == state)
        {
            return;
        }

        _states.Remove(key);
        _states[key] = state;
        while (_states.Count > MaximumSavedStates)
            _states.Remove(_states.Keys.First());
        _dirty = true;
    }

    public void Flush()
    {
        if (!_dirty)
            return;

        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(
                StatePath,
                JsonSerializer.Serialize(_states));
            _dirty = false;
        }
        catch
        {
        }
    }

    public void Clear()
    {
        _states.Clear();
        _dirty = false;

        try
        {
            File.Delete(StatePath);
        }
        catch
        {
        }
    }

    private static Dictionary<string, WindowState> Read()
    {
        try
        {
            if (!File.Exists(StatePath))
                return new(StringComparer.Ordinal);

            Dictionary<string, WindowState>? saved =
                JsonSerializer.Deserialize<Dictionary<string, WindowState>>(
                    File.ReadAllText(StatePath));
            return saved is null
                ? new(StringComparer.Ordinal)
                : new(saved, StringComparer.Ordinal);
        }
        catch
        {
            return new(StringComparer.Ordinal);
        }
    }
}
