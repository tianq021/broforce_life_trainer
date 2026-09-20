using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
class BridgeSlotsSmoke {
    static Type bridge;
    static BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
    static object Call(string name, params object[] args) { return bridge.GetMethod(name, flags).Invoke(null, args); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static int Main(string[] args) {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) {
            string path = Path.Combine(args[1], new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        bridge = Assembly.LoadFrom(args[0]).GetType("BroforceLifeTrainer.Bootstrap");
        if (bridge == null) throw new Exception("Bridge type missing");
        IntPtr memory = Marshal.AllocHGlobal(352);
        try {
            for (int i=0; i<352; i++) Marshal.WriteByte(memory, i, 0);
            bridge.GetField("sharedMemoryView", flags).SetValue(null, memory);
            FieldInfo slot = bridge.GetField("currentPlayer", flags);
            PropertyInfo target = bridge.GetProperty("lastHeroLockTarget", flags);
            for (int p=0; p<4; p++) {
                slot.SetValue(null, p);
                Call("WriteSharedInt64", 40, (long)(p+10));
                Call("WriteSharedInt64", 8, (long)(p+1));
                Call("WriteSharedInt64", 72, (long)(p%2));
                Call("WriteSharedInt64", 80, (long)(p+30));
                target.SetValue(null, (long)(p+20), null);
            }
            for (int p=0; p<4; p++) {
                slot.SetValue(null, p);
                Check((long)Call("ReadSharedInt64", 40) == p+10, "lock slot routing");
                Check(Marshal.ReadInt64(memory, 88*p+8) == p+1, "command slot routing");
                Check(Marshal.ReadInt64(memory, 88*p+72) == p%2, "villager AI slot routing");
                Check(Marshal.ReadInt64(memory, 88*p+80) == p+30, "companion command isolation");
                Check((long)target.GetValue(null, null) == p+20, "runtime lock isolation");
                Call("WriteSharedInt64", 48, 123L);
            }
            Check(Marshal.ReadInt64(memory,48) == 123L, "global hero mask");
            Check(Marshal.ReadInt64(memory,136) == 0L, "global mask must not spill into player 2");
            Call("ResetPlayerSequences");
            for (int p=0; p<4; p++) {
                slot.SetValue(null,p);
                Check((long)bridge.GetProperty("lastAddLifeSequence",flags).GetValue(null,null) == p+1, "reconnect consumes each player's stale commands");
                Array states = (Array)bridge.GetField("playerStates",flags).GetValue(null);
                object playerState = states.GetValue(p);
                Check((long)playerState.GetType().GetField("lastSpawnCompanionSequence").GetValue(playerState) == p+30,
                    "reconnect consumes stale companion commands for each player");
            }
            Console.WriteLine("PASS: compiled bridge four-player memory routing, runtime locks, global mask and reconnect sequences");
            return 0;
        } finally { Marshal.FreeHGlobal(memory); }
    }
}
