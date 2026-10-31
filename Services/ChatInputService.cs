using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace Bolgeler.Services;

/// <summary>
/// Oyuncudan chat uzerinden metin (ornek: yeni bolge ismi) girmesini bekler.
/// Girilen mesaji normal chat'e dusurmeden yakalar ve callback'i cagirir.
/// </summary>
public class ChatInputService
{
    private readonly ISwiftlyCore _core;
    private readonly Dictionary<int, Action<string>> _pending = new();

    public ChatInputService(ISwiftlyCore core)
    {
        _core = core;
    }

    public void Load()
    {
        // Chat hook tek merkezden (Plugin.OnClientChat) yonetilir
    }

    public void Unload()
    {
        _pending.Clear();
    }

    public bool IsWaiting(int slot) => _pending.ContainsKey(slot);

    public void RequestInput(IPlayer player, string promptMessage, Action<string> onInput)
    {
        _pending[player.Slot] = onInput;
        player.SendChat(promptMessage);
    }

    public void CancelInput(int slot)
    {
        _pending.Remove(slot);
    }

    public bool TryConsumeChat(int playerId, string text)
    {
        if (!_pending.TryGetValue(playerId, out var callback))
        {
            return false;
        }

        var trimmed = text.Trim();
        // Chat tirnak icinde gelebilir veya bos olabilir
        if (trimmed.StartsWith("\"") && trimmed.EndsWith("\"") && trimmed.Length >= 2)
        {
            trimmed = trimmed[1..^1].Trim();
        }

        if (string.Equals(trimmed, "!iptal", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "/iptal", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "iptal", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            _pending.Remove(playerId);
            var p = _core.PlayerManager.GetPlayer(playerId);
            p?.SendChat("[Red]Giriş iptal edildi.[Default]");
            return true;
        }

        _pending.Remove(playerId);
        callback.Invoke(trimmed);
        return true;
    }
}

