using System.Reflection;
using System.Text;
using System.Text.Json;

var trainee = new QuestRelayTransport();
var expert = new QuestRelayTransport();
bool expertStopped = false;
var update = typeof(QuestRelayTransport).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)!;
var destroy = typeof(QuestRelayTransport).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance)!;
var settings = new QuestRelaySettings
{
    enabled = true, serverUrl = Environment.GetEnvironmentVariable("HEALLINK_TEST_URL")!,
    accessToken = Environment.GetEnvironmentVariable("HEALLINK_TEST_TOKEN")!, room = "client-room"
};
UnityEngine.Application.persistentDataPath = Path.GetFullPath("Temp/RelayClientSettings");
Directory.CreateDirectory(UnityEngine.Application.persistentDataPath);
string configPath = Path.Combine(UnityEngine.Application.persistentDataPath, "relay.json");
try
{
    UnityEngine.Application.isEditor = false;
    File.WriteAllText(configPath, JsonSerializer.Serialize(new
    {
        enabled = true, serverUrl = "ws://127.0.0.1/relay", room = "test-room",
        accessToken = settings.accessToken
    }));
    try { QuestRelaySettings.Load(); throw new Exception("Plaintext production URL accepted"); }
    catch (FormatException) { }
    File.WriteAllText(configPath, JsonSerializer.Serialize(new
    {
        enabled = true, serverUrl = "wss://relay.example.edu/relay", room = "test-room",
        accessToken = settings.accessToken
    }));
    if (!QuestRelaySettings.Load().enabled) throw new Exception("Valid WSS configuration rejected");
}
finally
{
    UnityEngine.Application.isEditor = true;
    File.Delete(configPath);
}
void Tick() { update.Invoke(trainee, null); update.Invoke(expert, null); }
async Task Until(Func<bool> complete)
{
    var deadline = DateTime.UtcNow.AddSeconds(8);
    while (DateTime.UtcNow < deadline)
    {
        Tick();
        if (complete()) return;
        await Task.Delay(10);
    }
    throw new Exception("Relay client timed out");
}
try
{
    trainee.Configure(QuestRole.Trainee, settings);
    expert.Configure(QuestRole.Expert, settings);
    await Until(() => trainee.IsPaired && expert.IsPaired);
    if (trainee.SessionId != expert.SessionId) throw new Exception("Session IDs differ");
    byte[] video = { 255, 216, 1, 255, 217 };
    byte[] hands = Encoding.UTF8.GetBytes("{\"action\":\"hands\"}");
    byte[] audio = { 72, 81, 65, 49, 0, 0, 0, 0, 1, 0, 0, 0 };
    byte[] receivedVideo = null, receivedHands = null, traineeAudio = null, expertAudio = null;
    expert.VideoReceived += frame => receivedVideo = frame;
    trainee.HandsReceived += frame => receivedHands = frame;
    trainee.AudioReceived += frame => traineeAudio = frame;
    expert.AudioReceived += frame => expertAudio = frame;
    trainee.SendVideo(video);
    expert.SendHands(hands);
    trainee.SendAudio(audio);
    expert.SendAudio(audio);
    await Until(() => receivedVideo != null && receivedHands != null &&
        traineeAudio != null && expertAudio != null);
    if (!video.SequenceEqual(receivedVideo) || !hands.SequenceEqual(receivedHands) ||
        !audio.SequenceEqual(traineeAudio) || !audio.SequenceEqual(expertAudio))
        throw new Exception("Media changed in transit");
    string previousSession = trainee.SessionId;
    destroy.Invoke(expert, null);
    expertStopped = true;
    await Until(() => !trainee.IsPaired);
    expert = new QuestRelayTransport();
    expertStopped = false;
    expert.Configure(QuestRole.Expert, settings);
    await Until(() => trainee.IsPaired && expert.IsPaired);
    if (trainee.SessionId == previousSession) throw new Exception("Session was reused");
    Console.WriteLine("PASS: Unity relay client configuration, pairing, media, disconnect and replacement.");
}
finally
{
    destroy.Invoke(trainee, null);
    if (!expertStopped) destroy.Invoke(expert, null);
}

public enum QuestRole { None, Trainee, Expert }
namespace UnityEngine
{
    public class MonoBehaviour { }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DefaultExecutionOrder(int order) : Attribute { }
    public static class Application
    {
        public static bool isEditor = true;
        public static string persistentDataPath = Path.Combine(Path.GetTempPath(), "unused-relay-config");
    }
    public class TextAsset { public string text; }
    public static class Resources { public static T Load<T>(string name) where T : class => null; }
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
        public static string ToJson(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);
        public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    }
}
