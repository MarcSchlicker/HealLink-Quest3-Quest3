using System;
using System.Collections.Generic;
enum QuestRole { None, Trainee, Expert }
class QuestPeer { public string id; }
namespace UnityEngine {
static class Time { public static float unscaledTime; }
static class Random {
public static System.Random rng = new System.Random(42);
public static int Range(int a,int b)=>rng.Next(a,b);
public static float Range(float a,float b)=>a+(float)rng.NextDouble()*(b-a);
}}
class Node {
string instanceId; QuestRole role; float nextBroadcastTime;
    private sealed class Candidate
    {
        public QuestPeer peer;
        public string target;
        public float seen;
    }
    private readonly Dictionary<string, Candidate> candidates = new Dictionary<string, Candidate>();
    private string targetId;
    private float targetSince;
    private float nextSelection;
    public QuestPeer Peer { get; private set; }

    private void MatchPeer()
    {
        float now = UnityEngine.Time.unscaledTime;
        if (targetId != null)
        {
            Candidate c;
            if (!candidates.TryGetValue(targetId, out c) || now - c.seen > 6f ||
                (!string.IsNullOrEmpty(c.target) && c.target != instanceId) ||
                (Peer == null && now - targetSince > 6f) || (Peer != null && c.target != instanceId))
            {
                Peer = null;
                targetId = null;
                nextSelection = now + UnityEngine.Random.Range(0.5f, 1.5f);
                nextBroadcastTime = 0f;
            }
            else if (c.target == instanceId) Peer = c.peer;
        }
        if (targetId != null || now < nextSelection) return;
        nextSelection = now + 2f;
        List<string> available = new List<string>();
        List<string> expired = new List<string>();
        foreach (var entry in candidates)
        {
            if (now - entry.Value.seen > 6f) { expired.Add(entry.Key); continue; }
            if (role == QuestRole.Expert ? string.IsNullOrEmpty(entry.Value.target) : entry.Value.target == instanceId)
                available.Add(entry.Key);
        }
        foreach (string id in expired) candidates.Remove(id);
        if (available.Count == 0) return;
        targetId = available[UnityEngine.Random.Range(0, available.Count)];
        targetSince = now;
        nextBroadcastTime = 0f;
    }

public Node(string id, QuestRole r) { instanceId=id;role=r;nextSelection=2; }
public void Receive(Node other) {
if (other.role==role) return;
candidates[other.instanceId]=new Candidate {peer=new QuestPeer{id=other.instanceId},target=other.targetId,seen=UnityEngine.Time.unscaledTime};
}
public void Tick()=>MatchPeer();
public string Id=>instanceId;
}
class Program {
static void Step(List<Node> nodes,float t) {
UnityEngine.Time.unscaledTime=t;
foreach(var n in nodes) foreach(var o in nodes) if(n!=o)n.Receive(o);
foreach(var n in nodes)n.Tick();
foreach(var n in nodes) if(n.Peer!=null) {
var partner=nodes.Find(x=>x.Id==n.Peer.id);
foreach(var other in nodes) if(other!=n && other.Peer?.id==n.Peer.id) throw new Exception("Duplicate partner");
}
}
static void Main() {
for(int seed=0;seed<100;seed++) {
UnityEngine.Random.rng=new System.Random(seed);
var nodes=new List<Node>{new Node("e1",QuestRole.Expert),new Node("e2",QuestRole.Expert),new Node("t1",QuestRole.Trainee),new Node("t2",QuestRole.Trainee)};
for(int t=0;t<120;t++)Step(nodes,t);
foreach(var n in nodes) if(n.Peer==null) throw new Exception("Did not converge");
string removedId=nodes[0].Peer.id; nodes.RemoveAll(n=>n.Id==removedId);
for(int t=120;t<128;t++)Step(nodes,t);
if(nodes[0].Peer!=null)throw new Exception("Stale peer");
nodes.Add(new Node("replacement",QuestRole.Trainee));
for(int t=128;t<200;t++)Step(nodes,t);
foreach(var n in nodes) if(n.Peer==null) throw new Exception("Reconnect failed");
}
var same=new List<Node>{new Node("a",QuestRole.Expert),new Node("b",QuestRole.Expert)};
for(int t=0;t<30;t++)Step(same,t);
foreach(var n in same)if(n.Peer!=null)throw new Exception("Same role paired");
Console.WriteLine("PASS: 100 randomized four-device runs, exclusivity, timeout, replacement and same-role rejection.");
}
}
