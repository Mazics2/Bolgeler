namespace Bolgeler.Hud;

public enum RowStyle
{
    Normal,
    On,
    Off,
    Danger,
    Accent,
    Muted,
}

public sealed class HudRow
{
    public string Label { get; init; } = "";
    public string? Value { get; init; }
    public bool Selected { get; init; }
    public bool HasArrows { get; init; }
    public RowStyle Style { get; init; }
}

public sealed class HudMenuView
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public List<HudRow> Rows { get; init; } = new();
    public int Page { get; init; }
    public int PageCount { get; init; }
    public bool CanGoBack { get; init; }
    public string Hint { get; init; } = "";
    public string? Prompt { get; init; }
    public bool CursorEnabled { get; init; }
}

public sealed class HudDrawView
{
    public string Title { get; init; } = "";
    public string Info { get; init; } = "";
    public string Keys { get; init; } = "";
    public int Step { get; init; }
}

public sealed class HudInfoView
{
    public string Title { get; init; } = "";
    public string Effects { get; init; } = "";
    public string Color { get; init; } = "#00C8FF";
}

/// <summary>Bir oyuncu icin o anki HUD durumunun tamami. Renderer'lar bunu ekrana basar.</summary>
public sealed class HudFrame
{
    public HudMenuView? Menu { get; init; }
    public HudDrawView? Draw { get; init; }
    public HudInfoView? Info { get; init; }

    public bool IsEmpty => Menu == null && Draw == null && Info == null;
}

