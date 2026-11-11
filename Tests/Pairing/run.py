from pathlib import Path
import subprocess
root = Path(__file__).resolve().parents[2]
source = (root / "Assets/Scripts/QuestQuestApp.cs").read_text()
start = source.index("    private sealed class Candidate")
end = source.index("    public void Configure(QuestRole", start)
harness = Path(__file__).with_name("Program.cs")
text = harness.read_text()
a = text.index("    private sealed class Candidate")
b = text.index("public Node(", a)
harness.write_text(text[:a] + source[start:end].replace("Time.unscaledTime", "UnityEngine.Time.unscaledTime") + text[b:])
subprocess.run(["dotnet", "run", "--project", str(harness.with_name("Pairing.csproj")), "--no-restore", "-v", "quiet"], check=True)
