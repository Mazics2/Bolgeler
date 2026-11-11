namespace Bolgeler.Hud;

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

/// <summary>Menü dışı HUD durumu (çizim önizleme bilgisi ve bölge içi bilgi paneli).</summary>
public sealed class HudFrame
{
    public HudDrawView? Draw { get; init; }
    public HudInfoView? Info { get; init; }

    public bool IsEmpty => Draw == null && Info == null;
}
