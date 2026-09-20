// Deterministic test doubles: exercise the production adapter without starting Unity.
using System;
using System.Collections.Generic;
using System.Reflection;
using BroforceLifeTrainer;
using UnityEngine;

namespace UnityEngine {
    public class Object { public static void Destroy(object o) { } public int GetInstanceID() { return GetHashCode(); } }
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
public class Villager : TestVanDammeAnim { }
public class TestVanDammeAnim : Unit {
    public int heroType;
    public float fireRate = .333f;
    public PolymorphicAI enemyAI, enemyAIOnChildOrParent;
    public bool blind;
    public int Direction { get { return transform.localScale.x > 0 ? 1 : -1; } }
    public bool IsBlind() { return blind; }
}
public class Animal : Unit { }
public class PolymorphicAI : MonoBehaviour {
    protected virtual void Awake() { }
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
public static class GameModeController { public static bool finished; public static bool IsLevelFinished() { return finished; } public static bool DoesPlayerNumDamage(int a,int b) { return b==5; } }

class CompanionAiSmoke {
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static Unit Enemy(float x, float y) { Unit u = new GameObject().AddComponent<Unit>(); u.X=x;u.Y=y;u.collumn=(int)(x/16);return u; }
    static bool[] Tick(CompanionAI ai) {
        Time.time += .2f; Time.frameCount++;
        bool l=false,r=false,u=false,d=false,j=false,f=false,s1=false,s2=false,s3=false,s4=false,c=false;
        ai.GetInput(ref l,ref r,ref u,ref d,ref j,ref f,ref s1,ref s2,ref s3,ref s4,ref c);
        Check(!s1 && !s2 && !s3 && !s4,"never spends player special ammo");
        return new bool[] { l,r,u,d,j,f,c };
    }
    static void Main() {
        GameObject go=new GameObject();
        TestVanDammeAnim actor=go.AddComponent<TestVanDammeAnim>();actor.X=100;actor.playerNum=-1;
        PathAgent path=go.AddComponent<PathAgent>();
        Player player=new Player();player.character=Enemy(116,0);player.character.playerNum=2;
        HeroController.players[2]=player;
        Unit original=player.character;
        CompanionAI ai=CompanionAI.Attach(actor,2,path);
        Unit enemy=Enemy(160,0);Map.units.Add(enemy);
        Check(Tick(ai)[5],"stationary player: fires at visible enemy");
        actor.heroType=4;
        Check(!Tick(ai)[5],"melee hero approaches instead of attacking at gun range");
        enemy.X=115;
        Check(Tick(ai)[5],"melee hero attacks at close range");
        actor.heroType=0;enemy.X=160;
        Check(actor.playerNum==2,"first native input assigns friendly projectile ownership");
        Check(player.character==original,"does not replace player character");
        Check(actor.fireRate==.333f,"preserves native weapon rate");
        enemy.X=70;
        bool[] input=Tick(ai);Check(input[0] && !input[5],"turn before firing left");
        actor.transform.localScale=new Vector3(-1,1,1);
        Check(Tick(ai)[5],"fires after facing left");
        enemy.X=180;actor.transform.localScale=new Vector3(1,1,1);Physics.wall=140;
        input=Tick(ai);Check(!input[5] && path.requests>0,"blocked enemy triggers approach without shooting through wall");
        Physics.wall=float.NaN;Check(Tick(ai)[5],"fires after cover removed");
        enemy.Y=64;input=Tick(ai);Check(!input[5],"no shooting at wrong height");
        enemy.Y=0;enemy.playerNum=1;input=Tick(ai);Check(!input[5],"other local player never targeted");
        enemy.playerNum=5;enemy.invulnerable=true;Check(!Tick(ai)[5],"invulnerable target not fired on");enemy.invulnerable=false;
        player.alive=false;input=Tick(ai);foreach(bool value in input) Check(!value,"leader death stops input");
        player.alive=true;player.character=Enemy(450,0);player.character.playerNum=2;
        input=Tick(ai);Check(input[1],"respawned far leader is followed");
        Check(path.goal==player.character.collumn,"path targets new player body");
        Time.timeScale=0;input=Tick(ai);foreach(bool value in input) Check(!value,"pause clears input");Time.timeScale=1;
        GameModeController.finished=true;Check(!Tick(ai)[5],"level end clears attack");GameModeController.finished=false;
        Connect.IsOffline=false;input=Tick(ai);foreach(bool value in input) Check(!value,"online stops input");Connect.IsOffline=true;
        player.character=original;Physics.fail=true;Tick(ai);Check(ai.Failed,"AI faults are contained");Physics.fail=false;
        input=Tick(ai);foreach(bool value in input) Check(!value,"failed AI cannot resume stale input");
        Console.WriteLine("PASS: companion combat, cover, facing, teams, follow/respawn, pause, online and fault isolation");
    }
}

