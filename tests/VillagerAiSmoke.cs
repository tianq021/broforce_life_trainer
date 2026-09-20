// Deterministic test doubles: exercise the production adapter without starting Unity.
using System;
using System.Collections.Generic;
using System.Reflection;
using BroforceLifeTrainer;
using UnityEngine;

namespace UnityEngine {
    public class Object { public int GetInstanceID() { return GetHashCode(); } }
    public class Component : Object {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
    }
    public class MonoBehaviour : Component { public bool enabled = true; }
    public class GameObject {
        public Transform transform = new Transform();
        public bool activeInHierarchy = true;
        private List<Component> components = new List<Component>();
        public T AddComponent<T>() where T : Component, new() {
            T value = new T(); value.gameObject = this; components.Add(value); return value;
        }
        public T GetComponent<T>() where T : class {
            foreach (Component item in components) if (item is T) return item as T;
            return null;
        }
    }
    public class Transform { public Vector3 localScale = new Vector3(1,1,1); public Vector3 position; }
    public struct Vector3 {
        public float x,y,z;
        public Vector3(float a,float b,float c) { x=a;y=b;z=c; }
        public static Vector3 operator -(Vector3 a,Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
    }
    public struct Ray { public Vector3 origin,direction; public Ray(Vector3 a,Vector3 b) { origin=a;direction=b; } }
    public static class Physics {
        public static float wall = float.NaN;
        public static bool fail;
        public static bool Raycast(Ray ray,float distance,int layer) {
            if (fail) throw new InvalidOperationException("test physics fault");
            float end = ray.origin.x + ray.direction.x;
            return wall > System.Math.Min(ray.origin.x,end) && wall < System.Math.Max(ray.origin.x,end);
        }
    }
    public static class Time { public static float time, timeScale=1; public static int frameCount; }
    public static class LayerMask { public static int NameToLayer(string name) { return 2; } }
}
public class Unit : MonoBehaviour {
    public int health=10,playerNum=5,collumn,row;
    public float X,Y,height=6;
    public bool invulnerable,stealth;
    public bool IsInStealthMode() { return stealth; }
}
public class Villager : Unit {
    public float fireRate = .333f;
    public VillagerAI enemyAI;
    public bool blind;
    public int Direction { get { return transform.localScale.x > 0 ? 1 : -1; } }
    public bool IsBlind() { return blind; }
}
public class Animal : Unit { }
public class VillagerAI : MonoBehaviour {
    protected virtual void Update() { }
    protected virtual void LateUpdate() { }
    protected virtual void OnDestroy() { }
    public virtual void GetInput(ref bool l,ref bool r,ref bool u,ref bool d,ref bool j,ref bool f,
        ref bool s1,ref bool s2,ref bool s3,ref bool s4,ref bool c) { }
}
public class FollowerMinion : MonoBehaviour {
    public Unit leader;
    private int leaderPlayerNo = -1;
    public int LeaderIndex { get { return leaderPlayerNo; } }
}
public class NavPath { }
public class PathAgent : MonoBehaviour {
    public NavPath CurrentPath;
    public bool IsClimbing, stalled;
    public int requests, goal;
    public void GetPath(int col,int row,float milliseconds) { requests++;goal=col;CurrentPath=new NavPath(); }
    public void GetMove(ref bool l,ref bool r,ref bool u,ref bool d,ref bool j) { if (!stalled) r=true; }
}
public class Player {
    public Unit character;
    public bool alive=true,pending;
    public List<Unit> minions = new List<Unit>();
    public bool IsAlive() { return alive; }
    public bool IsPendingRespawnOrResuingInProgress() { return pending; }
    public void RegisterMinion(Unit u) { if (!minions.Contains(u)) minions.Add(u); }
    public void DeRegisterMinion(Unit u) { minions.Remove(u); }
}
public static class HeroController { public static Player[] players = new Player[4]; }
public class Map {
    public static Map Instance = new Map(); public static int groundLayer=1,groundLayerStrict=1;
    public static List<Unit> units = new List<Unit>();
}
public static class Connect { public static bool IsOffline=true; }
public static class GameModeController { public static bool DoesPlayerNumDamage(int a,int b) { return b==5; } }

class VillagerAiSmoke {
    class Fixture {
        public Villager actor;
        public VillagerAI native;
        public EnhancedVillagerAI ai;
        public Player player;
        public PathAgent path;
        public FollowerMinion follower;
        public Fixture(int owner) {
            GameObject go=new GameObject();
            actor=go.AddComponent<Villager>(); actor.playerNum=owner; actor.X=100;
            native=go.AddComponent<VillagerAI>(); actor.enemyAI=native;
            follower=go.AddComponent<FollowerMinion>(); path=go.AddComponent<PathAgent>();
            player=new Player(); player.character=Enemy(116); player.character.playerNum=owner;
            player.character.collumn=7; HeroController.players[owner]=player;
            ai=EnhancedVillagerAI.Attach(actor,owner); ai.SetEnhanced(true);
        }
    }
    static Unit Enemy(float x) {
        Unit u=new GameObject().AddComponent<Unit>();u.X=x;return u;
    }
    static bool[] Tick(Fixture f) {
        bool l=false,r=false,u=false,d=false,j=false,fire=false,a=false,b=false,c=false,e=false,climb=false;
        f.ai.GetInput(ref l,ref r,ref u,ref d,ref j,ref fire,ref a,ref b,ref c,ref e,ref climb);
        return new bool[] {l,r,u,d,j,fire};
    }
    static void Step(float seconds) { Time.time+=seconds;Time.frameCount++; }
    static void Check(bool condition,string name) { if (!condition) throw new Exception(name); }
    static void Reset() {
        Map.Instance=new Map();Map.units.Clear();HeroController.players=new Player[4];
        Physics.fail=false;Physics.wall=float.NaN;Connect.IsOffline=true;Time.timeScale=1;
        Time.time=100;Time.frameCount=100;
        typeof(EnhancedVillagerAI).GetField("nextGlobalPath",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,0f);
        typeof(EnhancedVillagerAI).GetField("lastPathFrame",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,-1);
    }
    static void Main() {
        Reset(); Fixture f=new Fixture(2); Step(1);
        Unit behind=Enemy(70),far=Enemy(180),friend=Enemy(105);friend.playerNum=0;
        Unit high=Enemy(110);high.Y=40;
        Map.units.Add(far);Map.units.Add(friend);Map.units.Add(high);Map.units.Add(behind);
        bool[] input=Tick(f);
        Check(input[0]&&!input[5],"choose nearer enemy behind; turn before shooting");
        f.actor.transform.localScale=new Vector3(-1,1,1);Step(.02f);input=Tick(f);
        Check(input[5],"fire after facing target");
        Physics.wall=85;Step(.02f);Check(!Tick(f)[5],"new wall blocks cached target immediately");
        Step(.2f);input=Tick(f);Check(input[1],"blocked near enemy does not hide visible far enemy");
        Check(f.follower.LeaderIndex==2 && f.player.minions.Contains(f.actor),"bind correct local player");
        Unit replacement=Enemy(120);f.player.character=replacement;Step(.2f);Tick(f);
        Check(f.follower.leader==replacement,"rebind after hero replacement");
        f.ai.SetEnhanced(false);Check(f.actor.enemyAI==f.native&&f.native.enabled,"toggle restores native component");
        f.ai.SetEnhanced(true);Check(f.actor.enemyAI==f.ai&&!f.native.enabled,"toggle enables adapter exclusively");

        Reset();f=new Fixture(0);f.player.character.X=450;f.player.character.collumn=28;
        Map.units.Add(Enemy(130));Step(1);input=Tick(f);
        Check(input[1]&&input[5]&&f.path.goal==28,"catchup preserves path and fires at forward enemy");
        Map.units.Clear(); Map.units.Add(Enemy(70)); Step(.2f); input=Tick(f);
        Check(input[1]&&!input[0]&&!input[5],"enemy behind cannot reverse catchup movement");
        int before=f.path.requests;f.path.stalled=true;Step(2);Tick(f);
        Check(f.path.requests>before,"stuck path is recomputed even when no movement input returned");
        before=f.path.requests;Time.timeScale=0;Step(2);input=Tick(f);
        Check(!input[1]&&!input[5]&&f.path.requests==before,"pause suspends AI");
        Time.timeScale=1;f.player.alive=false;Step(1);input=Tick(f);
        Check(!input[1]&&!input[5],"dead owner does not redirect to another player");
        f.player.alive=true;Connect.IsOffline=false;Tick(f);
        Check(f.actor.enemyAI==f.native,"online transition restores native");

        Reset();f=new Fixture(0);Map.units.Add(Enemy(130));Physics.fail=true;Step(1);Tick(f);
        Check(f.actor.enemyAI==f.native&&f.native.enabled,"runtime exception restores original AI");
        Check(f.ai.ConsumeFailure()&&!f.ai.ConsumeFailure(),"report fallback once");
        f.ai.SetEnhanced(true);Check(f.actor.enemyAI==f.native,"fault does not enter automatic retry loop");

        Reset();f=new Fixture(0);Fixture other=new Fixture(1);
        f.player.character.X=450;other.player.character.X=450;Step(1);Tick(f);Tick(other);
        Check(f.path.requests+other.path.requests==1,"shared navigation budget allows one path per frame");
        Step(.04f);Tick(other);Check(other.path.requests==1,"deferred unit gets a later path slot");
        Map.Instance=new Map();Tick(f);Check(f.actor.enemyAI==f.native,"scene change restores native");

        Check(VillagerTactics.ShouldCatchUp(280,0,false),"catchup begins outside threshold");
        Check(VillagerTactics.ShouldCatchUp(150,0,true),"catchup hysteresis retains state");
        Check(!VillagerTactics.ShouldCatchUp(60,0,true),"catchup ends near owner");
        Check(!VillagerTactics.ShouldRepath(false,0,10),"intentional idle is not stuck");
        Check(!VillagerTactics.ShouldRepath(true,10,2),"moving unit is not stuck");
        Check(VillagerTactics.InFiringLane(9,0,6),"real ordinary mook height=6 must be shootable on level ground");
        Check(!VillagerTactics.InFiringLane(9,32,6),"enemy on a higher platform must not be shot horizontally");
        Reset();Fixture[] squad=new Fixture[20];
        for (int i=0;i<squad.Length;i++) squad[i]=new Fixture(0);
        for (int frame=0;frame<300;frame++) {
            Step(1f/30f);
            HeroController.players[0].character.X=400;
            HeroController.players[0].character.collumn=25+frame/3;
            foreach (Fixture member in squad) Tick(member);
        }
        foreach (Fixture member in squad)
            Check(member.path.requests>=2,"FIFO grants repeated path slots to every member of a 20-unit squad");

        Reset();f=new Fixture(0);Unit distant=Enemy(430);distant.collumn=26;Map.units.Add(distant);
        Step(1);input=Tick(f);
        Check(input[1]&&!input[5]&&f.path.goal==26,"actively approach an enemy outside gun range while leader stands still");
        Check(f.actor.fireRate==.12f,"assault fire interval is reduced");
        f.ai.SetEnhanced(false);Check(f.actor.fireRate==.333f,"turning off restores original fire interval");

        Reset();f=new Fixture(0);Unit upstairs=Enemy(150);upstairs.Y=48;upstairs.row=3;upstairs.collumn=9;
        Map.units.Add(upstairs);Step(1);input=Tick(f);
        Check(input[1]&&!input[5]&&f.path.goal==9,"different-height enemy is acquired for approach, not filtered out");
        f.actor.Y=48;Step(.2f);input=Tick(f);Check(input[5],"starts shooting once attack path reaches target height");

        Reset();f=new Fixture(0);Unit covered=Enemy(160);covered.collumn=10;Map.units.Add(covered);
        Physics.wall=130;Step(1);input=Tick(f);
        Check(input[1]&&!input[5]&&f.path.goal==10,"covered enemy triggers approach without shooting through wall");
        Physics.wall=float.NaN;Step(.2f);Check(Tick(f)[5],"fires when terrain opens");

        Reset();f=new Fixture(0);Unit tooFar=Enemy(650);tooFar.collumn=40;Map.units.Add(tooFar);
        Step(1);input=Tick(f);Check(!input[1]&&!input[5],"does not chase outside player combat radius");
        Console.WriteLine("PASS: AI adapter scenarios including 20-unit path fairness (test doubles, not in-game validation)");
    }
}
