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
        int size = (int)bridge.GetField("SharedMemorySize", flags).GetRawConstantValue();
        int stride = (int)bridge.GetField("PlayerStride", flags).GetRawConstantValue();
        Check(size == 512 && stride == 128, "V14 protocol size");
        IntPtr memory = Marshal.AllocHGlobal(size);
        try {
            for (int i=0; i<size; i++) Marshal.WriteByte(memory, i, 0);
            bridge.GetField("sharedMemoryView", flags).SetValue(null, memory);
            FieldInfo slot = bridge.GetField("currentPlayer", flags);
            PropertyInfo target = bridge.GetProperty("lastHeroLockTarget", flags);
            for (int p=0; p<4; p++) {
                slot.SetValue(null, p);
                Call("WriteSharedInt64", 40, (long)(p+10));
                Call("WriteSharedInt64", 8, (long)(p+1));
                Call("WriteSharedInt64", 72, (long)(p%2));
                Call("WriteSharedInt64", 80, (long)(p+30));
                Call("WriteSharedInt64", 88, (long)(p%2));
                Call("WriteSharedInt64", 96, (long)(p+40));
                Call("WriteSharedInt64", 104, (long)(p+50));
                Call("WriteSharedInt64", 112, (long)(p+60));
                Call("WriteSharedInt64", 120, 99L);
                target.SetValue(null, (long)(p+20), null);
            }
            for (int p=0; p<4; p++) {
                slot.SetValue(null, p);
                Check((long)Call("ReadSharedInt64", 40) == p+10, "lock slot routing");
                Check(Marshal.ReadInt64(memory, stride*p+8) == p+1, "command slot routing");
                Check(Marshal.ReadInt64(memory, stride*p+72) == p%2, "villager AI slot routing");
                Check(Marshal.ReadInt64(memory, stride*p+80) == p+30, "companion command isolation");
                Check(Marshal.ReadInt64(memory, stride*p+88) == p%2, "invincible player isolation");
                Check(Marshal.ReadInt64(memory, stride*p+96) == p+40, "refill player isolation");
                Check(Marshal.ReadInt64(memory, stride*p+104) == p+50, "protection status isolation");
                Check(Marshal.ReadInt64(memory, stride*p+112) == p+60, "refill result isolation");
                Check(Marshal.ReadInt64(memory, stride*p+120) == (p==0 ? 99 : 0), "global client heartbeat");
                Check((long)target.GetValue(null, null) == p+20, "runtime lock isolation");
                Call("WriteSharedInt64", 48, 123L);
            }
            Check(Marshal.ReadInt64(memory,48) == 123L, "global hero mask");
            Check(Marshal.ReadInt64(memory,stride+48) == 0L, "global mask must not spill into player 2");
            Call("ResetPlayerSequences");
            for (int p=0; p<4; p++) {
                slot.SetValue(null,p);
                Check((long)bridge.GetProperty("lastAddLifeSequence",flags).GetValue(null,null) == p+1, "reconnect consumes each player's stale commands");
                Array states = (Array)bridge.GetField("playerStates",flags).GetValue(null);
                object playerState = states.GetValue(p);
                Check((long)playerState.GetType().GetField("lastSpawnCompanionSequence").GetValue(playerState) == p+30,
                    "reconnect consumes stale companion commands for each player");
                Check((long)playerState.GetType().GetField("lastRefillAmmoSequence").GetValue(playerState) == p+40,
                    "reconnect consumes stale refill commands for each player");
            }
            Console.WriteLine("PASS: compiled bridge four-player memory routing, runtime locks, global mask and reconnect sequences");
            return 0;
        } finally { Marshal.FreeHGlobal(memory); }
    }
}
