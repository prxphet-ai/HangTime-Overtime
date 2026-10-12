using System.IO;
using System.Text;
using UnityEngine;

namespace HangtimeOvertime.Online
{
    // Message ids. Reliable unless noted.
    internal enum Msg : byte
    {
        Hello = 1,        // client -> host: protocol, mod version, name
        Welcome = 2,      // host -> client: name
        Reject = 3,       // host -> client: reason
        Bye = 4,          // either way: leaving (reason)
        Menu = 10,        // host -> client: the Versus join screen
        Load = 12,        // host -> client: load the next round (setup, scores, builds, look)
        Ready = 13,       // client -> host: the round is loaded
        Events = 20,      // host -> client: timed match events (sounds, camera, hits, points)
        Screen = 30,      // host -> client: between-rounds / match-end screen
        ScreenSel = 31,   // host -> client: highlighted choice on that screen
        ScreenClose = 32,
        Notice = 33,      // host -> client: a pause notice (or none)
        ToTitle = 34,     // host -> client: the match is over, back to the title
        Snap = 40,        // host -> client, unreliable: players and ball
        Pads = 50,        // client -> host, unreliable: the client's devices
        Ping = 60,        // unreliable, both ways (keeps the connection alive, measures the delay)
        Pong = 61,
        Roster = 70,      // host -> all: who is in the session, with pings
        Chat = 71,        // client -> host: a line; host -> all: name + line
        ToLobby = 72,     // host -> all: the match is over, back to the lobby screen
    }

    internal class NetWriter
    {
        private readonly MemoryStream ms = new MemoryStream(256);
        private readonly BinaryWriter w;
        public NetWriter(Msg id) { w = new BinaryWriter(ms, Encoding.UTF8); w.Write((byte)id); }
        public NetWriter B(bool v) { w.Write(v); return this; }
        public NetWriter U8(int v) { w.Write((byte)v); return this; }
        public NetWriter I(int v) { w.Write(v); return this; }
        public NetWriter F(float v) { w.Write(v); return this; }
        public NetWriter S(string v) { w.Write(v ?? ""); return this; }
        public NetWriter V(Vector2 v) { w.Write(v.x); w.Write(v.y); return this; }
        public NetWriter V3(Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); return this; }
        public byte[] Bytes() { w.Flush(); return ms.ToArray(); }
    }

    internal class NetReader
    {
        private readonly BinaryReader r;
        public readonly Msg Id;
        public NetReader(byte[] d) { r = new BinaryReader(new MemoryStream(d), Encoding.UTF8); Id = (Msg)r.ReadByte(); }
        public bool B() => r.ReadBoolean();
        public int U8() => r.ReadByte();
        public int I() => r.ReadInt32();
        public float F() => r.ReadSingle();
        public string S() => r.ReadString();
        public Vector2 V() => new Vector2(r.ReadSingle(), r.ReadSingle());
        public Vector3 V3() => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        public bool More => r.BaseStream.Position < r.BaseStream.Length;
    }
}
