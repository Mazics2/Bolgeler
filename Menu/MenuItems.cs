using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;
using Bolgeler.Hud;

namespace Bolgeler.Menu;

public abstract class MenuItem
{
    public string Label { get; init; } = "";
    public virtual string? Value => null;
    public virtual bool HasArrows => false;
    public virtual RowStyle Style => RowStyle.Normal;

    /// <summary>E / sol tik.</summary>
    public virtual void Select(IPlayer player) { }

    /// <summary>A/D veya ok butonlari. dir = -1 / +1.</summary>
    public virtual void Adjust(IPlayer player, int dir) { }
}

public sealed class ActionItem : MenuItem
{
    private readonly Action<IPlayer> _action;
    private readonly Func<string?>? _value;
    private readonly RowStyle _style;

    public ActionItem(string label, Action<IPlayer> action, Func<string?>? value = null, RowStyle style = RowStyle.Normal)
    {
        Label = label;
        _action = action;
        _value = value;
        _style = style;
    }

    public override string? Value => _value?.Invoke();
    public override RowStyle Style => _style;
    public override void Select(IPlayer player) => _action(player);
}

public sealed class ToggleItem : MenuItem
{
    private readonly Func<bool> _get;
    private readonly Action<IPlayer, bool> _set;

    public ToggleItem(string label, Func<bool> get, Action<IPlayer, bool> set)
    {
        Label = label;
        _get = get;
        _set = set;
    }

    public override string? Value => _get() ? "AÇIK" : "KAPALI";
    public override RowStyle Style => _get() ? RowStyle.On : RowStyle.Off;
    public override void Select(IPlayer player) => _set(player, !_get());
    public override void Adjust(IPlayer player, int dir) => _set(player, dir > 0);
}

public sealed class NumberItem : MenuItem
{
    private readonly Func<float> _get;
    private readonly Action<IPlayer, float> _set;
    private readonly Func<float> _step;
    private readonly float _min;
    private readonly float _max;
    private readonly Func<float, string> _format;

    public NumberItem(string label, Func<float> get, Action<IPlayer, float> set, Func<float> step, float min, float max, Func<float, string>? format = null)
    {
        Label = label;
        _get = get;
        _set = set;
        _step = step;
        _min = min;
        _max = max;
        _format = format ?? (v => v.ToString("0.##"));
    }

    public NumberItem(string label, Func<float> get, Action<IPlayer, float> set, float step, float min, float max, Func<float, string>? format = null)
        : this(label, get, set, () => step, min, max, format)
    {
    }

    public override string? Value => _format(_get());
    public override bool HasArrows => true;
    public override RowStyle Style => RowStyle.Accent;
    public override void Select(IPlayer player) => Adjust(player, +1);

    public override void Adjust(IPlayer player, int dir)
    {
        var v = MathF.Round((_get() + _step() * dir) * 1000f) / 1000f;
        _set(player, Math.Clamp(v, _min, _max));
    }
}

public sealed class ChoiceItem : MenuItem
{
    private readonly IReadOnlyList<string> _options;
    private readonly Func<int> _get;
    private readonly Action<IPlayer, int> _set;

    public ChoiceItem(string label, IReadOnlyList<string> options, Func<int> get, Action<IPlayer, int> set)
    {
        Label = label;
        _options = options;
        _get = get;
        _set = set;
    }

    public override string? Value
    {
        get
        {
            var i = _get();
            return i >= 0 && i < _options.Count ? _options[i] : "?";
        }
    }

    public override bool HasArrows => true;
    public override RowStyle Style => RowStyle.Accent;
    public override void Select(IPlayer player) => Adjust(player, +1);

    public override void Adjust(IPlayer player, int dir)
    {
        var n = _options.Count;
        _set(player, ((_get() + dir) % n + n) % n);
    }
}

/// <summary>Bir menu ekraninin o anki icerigi (her render'da yeniden uretilir, degerler hep canli).</summary>
public sealed class MenuPage
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public List<MenuItem> Items { get; init; } = new();
}

public delegate MenuPage MenuFactory(IPlayer player);

public sealed class MenuSession
{
    private readonly Stack<(MenuFactory Factory, int Cursor)> _history = new();

    public MenuFactory Current { get; private set; }
    public int Cursor { get; set; }
    public bool CursorCapture { get; set; }
    public GameButtonFlags LastButtons { get; set; }
    public float LastKeyTime { get; set; }

    public MenuSession(MenuFactory root)
    {
        Current = root;
    }

    public bool CanGoBack => _history.Count > 0;

    public void Push(MenuFactory factory)
    {
        _history.Push((Current, Cursor));
        Current = factory;
        Cursor = 0;
    }

    /// <summary>Ayni seviyede ekrani degistir (gecmise eklemeden).</summary>
    public void Replace(MenuFactory factory)
    {
        Current = factory;
        Cursor = 0;
    }

    public bool Back()
    {
        if (_history.Count == 0) return false;
        (Current, Cursor) = _history.Pop();
        return true;
    }
}

