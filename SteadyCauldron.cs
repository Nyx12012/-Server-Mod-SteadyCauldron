using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Alta.Networking;
using Alta.Networking.Internal;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(SteadyCauldron.Mod), "SteadyCauldron", SteadyCauldron.Mod.Version, "Shadow")]
[assembly: MelonGame("Alta", "A Township Tale")]

namespace SteadyCauldron
{
    public class Mod : MelonMod
    {
        public const string Version = "1.0.0";
        internal static MelonLogger.Instance Log;
        static bool server;
        static float nextScan;
        static float nextConfig;
        static string lastError;
        static FieldInfo entityManagerField;
        static readonly Dictionary<int, Pot> pots = new Dictionary<int, Pot>();
        static readonly List<int> gone = new List<int>();

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            server = ApplicationManager.IsHeadless || CommandLineArguments.Contains(CommandLineArguments.StartServerArgument);
            if (!server) return;
            entityManagerField = typeof(NetworkScene).GetField("entityManager", BindingFlags.Instance | BindingFlags.NonPublic);
            Config.Load();
            Log.Msg("SteadyCauldron " + Version + " is running. Settings live in " + Config.FilePath);
        }

        public override void OnUpdate()
        {
            if (!server) return;
            try
            {
                if (!NetworkSceneManager.IsServer) return;
                float now = Time.time;
                if (now >= nextConfig)
                {
                    nextConfig = now + 2f;
                    Config.ReloadIfChanged();
                }
                if (now >= nextScan)
                {
                    nextScan = now + 2f;
                    Scan();
                }
                gone.Clear();
                foreach (KeyValuePair<int, Pot> pair in pots)
                {
                    try
                    {
                        if (!pair.Value.Tick(now)) gone.Add(pair.Key);
                    }
                    catch (Exception e)
                    {
                        gone.Add(pair.Key);
                        Warn("A cauldron check failed: " + e.Message, e);
                    }
                }
                foreach (int id in gone) pots.Remove(id);
            }
            catch (Exception e)
            {
                Warn("Update failed: " + e.Message, e);
            }
        }

        static void Warn(string message, Exception e)
        {
            if (message == lastError) return;
            lastError = message;
            Log.Warning(message + "\n" + e.StackTrace);
        }

        static void Scan()
        {
            IEnumerable<NetworkEntity> all = AllEntities();
            if (all == null) return;
            foreach (NetworkEntity e in all)
            {
                if (e == null || e.IsBeingDestroyed || e.Parent != null || e.Prefab == null) continue;
                if (!Config.Current.Items.Contains(e.Prefab.Hash)) continue;
                int id = e.GetInstanceID();
                if (pots.ContainsKey(id)) continue;
                Pickup p = e.CommonPickup;
                if (p == null || p.Rigidbody == null) continue;
                pots[id] = new Pot(e, p);
            }
        }

        static IEnumerable<NetworkEntity> AllEntities()
        {
            NetworkScene scene = NetworkSceneManager.Current as NetworkScene;
            if (scene == null || entityManagerField == null) return null;
            EntityManager manager = entityManagerField.GetValue(scene) as EntityManager;
            return manager == null ? null : manager.IterateEntities();
        }

        internal static void Say(string message)
        {
            if (Config.Current.Log) Log.Msg(message);
        }
    }

    class Pot
    {
        const int Mask = (1 << 0) | (1 << 8) | (1 << 10) | (1 << 11) | (1 << 13) | (1 << 19) | (1 << 22) | (1 << 23);
        const float HoldSeconds = 4f;
        const float Lift = 0.03f;
        readonly NetworkEntity entity;
        readonly Pickup pickup;
        readonly Rigidbody body;
        bool lifted;
        bool warned;
        float restSince = -1f;
        float retryAt;
        float holdUntil;

        internal Pot(NetworkEntity entity, Pickup pickup)
        {
            this.entity = entity;
            this.pickup = pickup;
            body = pickup.Rigidbody;
        }

        internal bool Tick(float now)
        {
            if (entity == null || entity.IsBeingDestroyed || pickup == null || body == null) return false;
            if (entity.Prefab == null || !Config.Current.Items.Contains(entity.Prefab.Hash)) return false;
            if (holdUntil > now && body.isKinematic) body.WakeUp();
            if (pickup.Interactors.Count >= pickup.InteractorsToRelease)
            {
                lifted = true;
                warned = false;
                restSince = -1f;
                return true;
            }
            if (pickup.IsDocked || entity.Parent != null || body.isKinematic)
            {
                lifted = false;
                restSince = -1f;
                return true;
            }
            if (lifted)
            {
                lifted = false;
                float speed = body.velocity.magnitude;
                if (speed <= Config.Current.Throw)
                {
                    Snap(now, "set down", speed);
                    return true;
                }
                Mod.Say(Name() + " was let go at " + F(speed) + " m/s, waiting for it to stop");
                return true;
            }
            if (now < retryAt) return true;
            bool still = body.IsSleeping() || (body.velocity.sqrMagnitude < 0.04f && body.angularVelocity.sqrMagnitude < 0.25f);
            if (!still)
            {
                restSince = -1f;
                return true;
            }
            if (restSince < 0f)
            {
                restSince = now;
                return true;
            }
            if (now - restSince >= 0.5f) Snap(now, "came to rest", body.velocity.magnitude);
            return true;
        }

        void Snap(float now, string why, float speed)
        {
            Transform t = entity.transform;
            Vector3 startPosition = t.position;
            Quaternion startRotation = t.rotation;
            float tilt = Vector3.Angle(t.up, Vector3.up);
            Bounds bounds;
            if (!OwnBounds(out bounds))
            {
                Fail(now, why, "it has no solid colliders");
                return;
            }
            RaycastHit hit;
            if (!Ground(bounds, out hit))
            {
                Fail(now, why, "no surface within " + F(Config.Current.Reach) + " m below it");
                return;
            }
            float groundY = hit.point.y;
            if (Config.Current.Upright && tilt > 0.5f)
            {
                t.rotation = Quaternion.FromToRotation(t.up, Vector3.up) * t.rotation;
                Physics.SyncTransforms();
                if (!OwnBounds(out bounds))
                {
                    t.SetPositionAndRotation(startPosition, startRotation);
                    Physics.SyncTransforms();
                    Fail(now, why, "it has no solid colliders once stood upright");
                    return;
                }
            }
            t.position = t.position + Vector3.up * (groundY + 0.005f - bounds.min.y);
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            Physics.SyncTransforms();
            holdUntil = now + HoldSeconds;
            restSince = -1f;
            warned = false;
            Mod.Say(Name() + " " + why + " at " + F(speed) + " m/s, tilt " + F(tilt) + " deg, moved "
                + F(Vector3.Distance(startPosition, t.position)) + " m onto " + Describe(hit.collider) + ". Frozen.");
        }

        void Fail(float now, string why, string reason)
        {
            retryAt = now + 2f;
            if (warned) return;
            warned = true;
            Mod.Say(Name() + " " + why + " but stays loose: " + reason);
        }

        bool OwnBounds(out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            foreach (Collider c in pickup.Colliders)
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                if (!any)
                {
                    bounds = c.bounds;
                    any = true;
                }
                else bounds.Encapsulate(c.bounds);
            }
            return any;
        }

        bool Ground(Bounds bounds, out RaycastHit best)
        {
            best = new RaycastHit();
            Vector3 origin = new Vector3(bounds.center.x, bounds.min.y + Lift, bounds.center.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, Lift + Config.Current.Reach, Mask, QueryTriggerInteraction.Ignore);
            bool found = false;
            Transform root = entity.transform;
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null || h.collider.transform.IsChildOf(root)) continue;
                if (found && h.distance >= best.distance) continue;
                Rigidbody other = h.collider.attachedRigidbody;
                if (other != null)
                {
                    Pickup held = other.GetComponentInParent<Pickup>();
                    if (held != null && held.Interactors.Count > 0) continue;
                }
                best = h;
                found = true;
            }
            return found;
        }

        string Name()
        {
            Vector3 p = entity.transform.position;
            string name = entity.Prefab.name.Replace("(Clone)", "").Trim();
            int cut = name.IndexOf(" - ");
            if (cut >= 0) name = name.Substring(cut + 3);
            return name + " " + entity.Identifier + " at (" + F(p.x) + ", " + F(p.y) + ", " + F(p.z) + ")";
        }

        static string Describe(Collider c)
        {
            NetworkEntity e = c.GetComponentInParent<NetworkEntity>();
            string owner = e != null && e.PrefabRoot != null && e.PrefabRoot.Prefab != null ? e.PrefabRoot.Prefab.name : c.gameObject.name;
            return owner + " (layer " + LayerMask.LayerToName(c.gameObject.layer) + ")";
        }

        static string F(float v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    public class Config
    {
        internal const string FileName = "SteadyCauldron.txt";
        internal static string FilePath;
        internal static Config Current = new Config();
        static DateTime stamp;
        static string lastWarning;

        public readonly HashSet<uint> Items = new HashSet<uint> { 45082 };
        public float Reach = 1f;
        public float Throw = 4f;
        public bool Upright = true;
        public bool Log = true;
        public readonly List<string> Problems = new List<string>();

        internal static readonly string[] Template =
        {
            "items = 45082",
            "reach = 1",
            "throw = 4",
            "upright = on",
            "log = on",
        };

        public static Config Parse(string[] lines)
        {
            Config c = new Config();
            float number;
            bool flag;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string where = "line " + (i + 1) + ": ";
                int eq = line.IndexOf('=');
                if (eq < 0)
                {
                    c.Problems.Add(where + "expected 'name = value', got '" + line + "'");
                    continue;
                }
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "items":
                        HashSet<uint> items = new HashSet<uint>();
                        bool ok = true;
                        foreach (string part in value.Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            uint hash;
                            if (uint.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out hash)) items.Add(hash);
                            else ok = false;
                        }
                        if (!ok || items.Count == 0)
                        {
                            c.Problems.Add(where + "'" + value + "' is not a list of item hashes");
                            continue;
                        }
                        c.Items.Clear();
                        c.Items.UnionWith(items);
                        break;
                    case "reach":
                        if (Number(value, 0f, 5f, out number)) c.Reach = number;
                        else c.Problems.Add(where + "reach must be a number from 0 to 5");
                        break;
                    case "throw":
                        if (Number(value, 0f, 50f, out number)) c.Throw = number;
                        else c.Problems.Add(where + "throw must be a number from 0 to 50");
                        break;
                    case "upright":
                        if (Switch(value, out flag)) c.Upright = flag;
                        else c.Problems.Add(where + "upright must be on or off");
                        break;
                    case "log":
                        if (Switch(value, out flag)) c.Log = flag;
                        else c.Problems.Add(where + "log must be on or off");
                        break;
                    default:
                        c.Problems.Add(where + "unknown setting '" + key + "'");
                        break;
                }
            }
            return c;
        }

        static bool Number(string text, float min, float max, out float result)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result)) return false;
            return !float.IsNaN(result) && result >= min && result <= max;
        }

        static bool Switch(string text, out bool result)
        {
            string t = text.ToLowerInvariant();
            result = t == "on" || t == "yes" || t == "true";
            return result || t == "off" || t == "no" || t == "false";
        }

        public string Describe()
        {
            StringBuilder b = new StringBuilder("items ");
            bool first = true;
            foreach (uint h in Items)
            {
                if (!first) b.Append(',');
                b.Append(h.ToString(CultureInfo.InvariantCulture));
                first = false;
            }
            b.Append(", reach ").Append(Reach.ToString(CultureInfo.InvariantCulture)).Append(" m");
            b.Append(", throw ").Append(Throw.ToString(CultureInfo.InvariantCulture)).Append(" m/s");
            b.Append(", upright ").Append(Upright ? "on" : "off");
            b.Append(", log ").Append(Log ? "on" : "off");
            return b.ToString();
        }

        internal static void Load()
        {
            string mods = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string userData = Path.Combine(Directory.GetParent(mods).FullName, "UserData");
            FilePath = Path.Combine(userData, FileName);
            try
            {
                if (!File.Exists(FilePath))
                {
                    Directory.CreateDirectory(userData);
                    File.WriteAllLines(FilePath, Template);
                    Mod.Log.Msg("Wrote " + FilePath + " with the default settings.");
                }
                Read();
            }
            catch (Exception e)
            {
                Mod.Log.Error("Could not read " + FilePath + ", using the default settings. " + e.Message);
            }
        }

        static void Read()
        {
            DateTime seen = File.GetLastWriteTimeUtc(FilePath);
            string[] lines = File.ReadAllLines(FilePath);
            stamp = seen;
            lastWarning = null;
            Config c = Parse(lines);
            foreach (string problem in c.Problems) Mod.Log.Warning(FileName + " " + problem);
            Current = c;
            Mod.Log.Msg("Settings: " + c.Describe());
        }

        internal static void ReloadIfChanged()
        {
            if (FilePath == null) return;
            try
            {
                if (File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != stamp) Read();
            }
            catch (Exception e)
            {
                string warning = "Could not reload " + FileName + ", keeping the last settings. " + e.Message;
                if (warning != lastWarning) Mod.Log.Warning(warning);
                lastWarning = warning;
            }
        }
    }
}
