using System;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class QuestRelaySettings
{
    public bool enabled;
    public string serverUrl = "";
    public string room = "";
    public string accessToken = "";

    public static QuestRelaySettings Load()
    {
        string path = Path.Combine(Application.persistentDataPath, "relay.json");
        string json;
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length > 8192) throw new FormatException("Relay configuration is too large");
            json = File.ReadAllText(path);
        }
        else
        {
            var defaults = Resources.Load<TextAsset>("HealLinkRelay");
            json = defaults != null ? defaults.text : "{}";
        }
        var settings = JsonUtility.FromJson<QuestRelaySettings>(json);
        if (settings == null) throw new FormatException("Invalid relay configuration");
        if (!settings.enabled) return settings;
        if (!Uri.TryCreate(settings.serverUrl, UriKind.Absolute, out Uri uri) ||
            (uri.Scheme != "wss" && !(Application.isEditor && uri.Scheme == "ws" && uri.IsLoopback)) ||
            uri.AbsolutePath != "/relay" || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new FormatException("Use a wss URL ending in /relay");
        if (string.IsNullOrEmpty(settings.accessToken) || settings.accessToken.Length < 32 ||
            settings.accessToken.Length > 512 || settings.accessToken.StartsWith("replace-", StringComparison.Ordinal))
            throw new FormatException("Relay access token is missing");
        if (settings.room == null || !System.Text.RegularExpressions.Regex.IsMatch(settings.room,
            @"\A[A-Za-z0-9_-]{6,64}\z"))
            throw new FormatException("Use a room code with 6 to 64 letters, digits, hyphens or underscores");
        return settings;
    }
}
