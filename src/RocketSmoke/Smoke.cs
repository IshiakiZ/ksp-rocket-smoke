using System;
using System.Collections.Generic;
using Keystone;
using UnityEngine;

#if !DEV
[assembly: KSPAssembly("RocketSmoke", 0, 2)]
[assembly: KSPAssemblyDependency("Keystone", 0, 5)]
#endif

namespace RocketSmoke
{
    /// <summary>
    /// Rocket Smoke: the smoke of a rocket's engines as real smoke.
    ///
    /// Asked for as: "volumetric smoke for the rocket (its exhaust and launch smoke)".
    ///
    /// The game draws an engine's smoke as a string of flat puffs. This hands it to the Volumetric Explosions
    /// mod instead, whose air already carries, lights and draws an explosion's smoke as a true volume: each
    /// running rocket engine says every frame where its nozzle is, how hard it is burning and what kind of
    /// smoke it makes, and that mod's air does the rest (see its Sources). So the smoke of a launch rolls out
    /// over the pad and up, is lit by the sun and shaded by itself, leans with the wind, and is blown about
    /// by the next rocket that goes through it; and a rocket in flight leaves a trail that hangs.
    ///
    /// What is this mod's own is knowing the engines: which burn what (Keystone's Engines says, for every mod
    /// that draws exhaust), how much smoke each fuel makes and of what colour, and hiding the game's own
    /// smoke while ours is drawn.
    ///
    /// It needs Volumetric Explosions 0.4 or later, and does nothing without it but say so.
    /// </summary>
    static class Smoke
    {
        public const string Version = "0.2.0";

        public static Mod Page;
        public static Toggle On, Hide;
        public static Slider Much, Hangs;

        public static void Setup()
        {
            if (Page != null && Kit.Find("Rocket Smoke") == Page) return;
            Page = Kit.Register("Rocket Smoke", Version, "The smoke of a rocket's engines as real smoke: it rolls out over the pad, takes the light, leans with the wind, and hangs where the rocket has been.");
            On = Page.Toggle("on", "Rocket smoke", true, "Off: the smoke the game draws.");
            Much = Page.Slider("much", "How much", 1f, 0.3f, 2.5f, "", 2, "How much smoke the engines make.");
            Hangs = Page.Slider("hangs", "How long it hangs", 1f, 0.3f, 3f, "", 2, "How long the smoke stays in the air. Longer costs more: there is more of it at once.");
            Hide = Page.Toggle("hide", "Hide the game's own smoke", true, "While an engine's smoke is drawn by this mod, the smoke the game draws for it is hidden.");
        }

        public static void Log(string text) => Kit.Log("Rocket Smoke", text);

        /// <summary>The smoke each fuel makes: how much (1 is a solid booster's), how light, how brown, and for how many seconds it hangs in thick still air.</summary>
        public static void Of(Fuel fuel, out float amount, out float shade, out float warm, out float lasts, out int tint)
        {
            switch (fuel)
            {
                // Solid propellant: thick, white with a little grey in it, and it stays.
                case Fuel.Solid: amount = 1f; shade = 0.82f; warm = 0.06f; lasts = 16f; tint = 1; break;
                // Kerosene: thin, grey-brown with soot.
                case Fuel.Kerosene: amount = 0.24f; shade = 0.42f; warm = 0.3f; lasts = 9f; tint = 0; break;
                // Hydrogen: water, and nothing else: a little white steam that is soon gone.
                case Fuel.Hydrogen: amount = 0.13f; shade = 0.85f; warm = 0f; lasts = 5f; tint = 4; break;
                case Fuel.Methane: amount = 0.12f; shade = 0.6f; warm = 0.1f; lasts = 5f; tint = 4; break;
                // Fuels that keep: a thin haze with brown in it.
                case Fuel.Storable: amount = 0.14f; shade = 0.38f; warm = 0.6f; lasts = 6f; tint = 2; break;
                case Fuel.Mono: amount = 0.06f; shade = 0.8f; warm = 0f; lasts = 3f; tint = 2; break;
                default: amount = 0f; shade = 0.5f; warm = 0f; lasts = 0f; tint = 0; break;
            }
        }
    }

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class SmokeAtStart : MonoBehaviour
    {
        void Awake() { Smoke.Setup(); Destroy(gameObject); }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class Trails : MonoBehaviour
    {
        /// <summary>One engine: its nozzles as measured, and what of the game's own smoke is hidden for it.</summary>
        sealed class Engine
        {
            public ModuleEngines Module;
            public Part Part;
            public Fuel Fuel;
            public readonly List<Transform> Nozzles = new List<Transform>();
            public readonly List<float> Rims = new List<float>(), Radii = new List<float>();
            public bool Measured;
            public float Seen;                 // when its smoke was last taken
            public bool Hid;                   // whether the game's own smoke for it is hidden just now
            public readonly List<Renderer> Hidden = new List<Renderer>();
            public readonly List<ModuleSurfaceFX> Dust = new List<ModuleSurfaceFX>();      // the game's own dust and spray where its jet meets the ground,
            public readonly List<float> DustWas = new List<float>();                       // and how strong each was set to be
        }

        readonly List<Engine> engines = new List<Engine>();
        readonly HashSet<ModuleEngines> known = new HashSet<ModuleEngines>();
        float lookAt;
        int smoking;
        bool hooked, broken;
        public static string Report = "", Why = "";

        void Awake()
        {
            Smoke.Setup();
            Smoke.Page.Panel = Panel;
        }

        void Start()
        {
            // (Everything that names the other mod is in Feed, and Feed is not touched unless that mod is there: so this one
            // loads, and can say what is missing, where it is not.)
            if (!Installed()) { Why = "Rocket Smoke needs the Volumetric Explosions mod (0.4 or later), which is not installed: the game's own smoke is left."; Smoke.Log(Why); return; }
            try { Feed.Hook(Asked); hooked = true; }
            catch (Exception ex) { Why = "Rocket Smoke needs Volumetric Explosions 0.4 or later: the one installed is older (" + ex.GetType().Name + ")."; Smoke.Log(Why); }
        }

        static bool Installed()
        {
            foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetType("VolumetricExplosions.Sources", false) != null) return true;
            return false;
        }

        void OnDestroy()
        {
            if (hooked) { try { Feed.Unhook(Asked); } catch (Exception) { } }
            foreach (Engine e in engines) Show(e);
            engines.Clear();
            if (Smoke.Page != null && Smoke.Page.Panel == Panel) Smoke.Page.Panel = null;
        }

        void Panel()
        {
            GUILayout.Label(Why.Length > 0 ? Why : Report, Host.Small);
        }

        static void Show(Engine e)
        {
            foreach (Renderer r in e.Hidden) if (r != null) r.forceRenderingOff = false;
            e.Hidden.Clear();
            for (int n = 0; n < e.Dust.Count; n++) if (e.Dust[n] != null) e.Dust[n].fxMax = e.DustWas[n];
            e.Dust.Clear(); e.DustWas.Clear();
            e.Hid = false;
        }

        /// <summary>The game's own smoke for this engine: every particle effect on its part that has "smoke" in its name, or in the name of what it hangs from.</summary>
        static void HideTheirs(Engine e)
        {
            e.Hid = true;
            // (And the haze the game draws on the ground under an engine, flat pictures of dust, which would lie in the
            // middle of ours: it is as strong as the engine's thrust times a number of its own, which is set to nothing.)
            int which = e.Part.Modules.IndexOf(e.Module);
            foreach (ModuleSurfaceFX dust in e.Part.FindModulesImplementing<ModuleSurfaceFX>())
            {
                if (dust == null || dust.thrustProviderModuleIndex != which) continue;
                e.Dust.Add(dust); e.DustWas.Add(dust.fxMax);
                dust.fxMax = 0f;
            }
            foreach (ParticleSystemRenderer r in e.Part.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (r == null || r.forceRenderingOff) continue;
                bool smoke = false;
                for (Transform t = r.transform; t != null && t != e.Part.transform; t = t.parent)
                    if (t.name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0) { smoke = true; break; }
                if (!smoke) continue;
                r.forceRenderingOff = true;
                e.Hidden.Add(r);
            }
        }

        void Find()
        {
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || !v.loaded) continue;
                foreach (Part part in v.parts)
                {
                    if (part == null || part.Modules == null) continue;
                    for (int m = 0; m < part.Modules.Count; m++)
                    {
                        ModuleEngines module = part.Modules[m] as ModuleEngines;
                        if (module == null || known.Contains(module) || module.thrustTransforms == null || module.thrustTransforms.Count == 0) continue;
                        known.Add(module);
                        Fuel fuel = Engines.Burns(module, part.partInfo != null ? part.partInfo.name : null);
                        Smoke.Of(fuel, out float amount, out _, out _, out _, out _);
                        if (amount <= 0f) continue;
                        engines.Add(new Engine { Module = module, Part = part, Fuel = fuel });
                    }
                }
            }
            engines.RemoveAll(e =>
            {
                if (e.Module != null && e.Part != null && e.Part.vessel != null && e.Part.vessel.loaded) return false;
                Show(e);
                if (e.Module != null) known.Remove(e.Module);
                return true;
            });
            known.RemoveWhere(m => m == null);
        }

        /// <summary>The air wants to know what is burning: every running rocket engine says where its nozzles are and what smoke it makes.</summary>
        void Asked()
        {
            if (broken) return;
            bool wanted = Smoke.On != null && Smoke.On && Feed.Ready();
            float now = Time.time;
            if (!wanted)
            {
                foreach (Engine e in engines) if (e.Hid) Show(e);
                smoking = 0;
                Report = Smoke.On ? "Volumetric Explosions cannot draw smoke as a volume here: the game's own smoke is left." : "";
                return;
            }
            if (Time.realtimeSinceStartup > lookAt) { Find(); lookAt = Time.realtimeSinceStartup + 1f; }
            float much = (float)Smoke.Much, hangs = (float)Smoke.Hangs;
            int burning = 0;
            foreach (Engine e in engines)
            {
                // (a part that has just been blown up is still on the list until the list is next gone through)
                if (e.Module == null || e.Part == null) continue;
                float running = Engines.Running(e.Module);
                Vessel v = e.Part.vessel;
                if (running <= 0.02f || v == null || v.packed)
                {
                    // (its smoke is given back a moment after it stops: the last of ours is still coming out of the flame)
                    if (e.Hid && now - e.Seen > 0.5f) Show(e);
                    continue;
                }
                if (!e.Measured) Measure(e);
                if (e.Nozzles.Count == 0) continue;
                // One source for the engine: the middle of its nozzles' mouths, as wide as all of them together.
                Vector3 rim = Vector3.zero, along = Vector3.zero;
                float area = 0f, each = 0f;
                bool whole = true;
                foreach (Transform one in e.Nozzles) if (one == null) whole = false;
                if (!whole) continue;
                for (int n = 0; n < e.Nozzles.Count; n++)
                {
                    Transform nozzle = e.Nozzles[n];
                    rim += nozzle.position + nozzle.forward * e.Rims[n];
                    along += nozzle.forward;
                    area += e.Radii[n] * e.Radii[n];
                    each += e.Radii[n];
                }
                rim /= e.Nozzles.Count; each /= e.Nozzles.Count;
                along = along.sqrMagnitude > 1e-6f ? along.normalized : e.Nozzles[0].forward;
                Smoke.Of(e.Fuel, out float amount, out float shade, out float warm, out float lasts, out int tint);
                // (The ship it is on: with Volumetric Explosions 0.5 or later the smoke is worked out with the ship's flames while it
                // is young, and turns out of the flame where the flame cools, and is handed on to the air behind once the ship has
                // left it. An older one lays it straight into the air and takes no notice of the ship.)
                bool taken = Feed.Burning((int)e.Part.flightID * 31 + e.Module.GetInstanceID() % 31, rim, along, v.srf_velocity, Mathf.Sqrt(area), Engines.Flame(e.Fuel, each) * (0.3f + 0.7f * Mathf.Sqrt(running)),
                                          running, amount * much, shade, warm, lasts * hangs, tint, v.transform);
                if (taken)
                {
                    burning++;
                    e.Seen = now;
                    if (Smoke.Hide && !e.Hid) HideTheirs(e);
                    else if (!Smoke.Hide && e.Hid) Show(e);
                }
                else if (e.Hid && now - e.Seen > 0.5f) Show(e);
            }
            smoking = burning;
            Report = burning > 0 ? burning + (burning == 1 ? " engine is" : " engines are") + " leaving smoke." : engines.Count > 0 ? "No engine is running where there is air to hold smoke." : "No rocket engine here.";
        }

        /// <summary>Where the mouths of an engine's nozzles are and how wide (see Keystone's Engines.Mouth): once, the first time it runs, by when whatever shrouded it has been cast off.</summary>
        static void Measure(Engine e)
        {
            e.Measured = true;
            IList<Transform> all = e.Module.thrustTransforms;
            for (int n = 0; n < all.Count; n++)
            {
                Transform nozzle = all[n];
                if (nozzle == null || !nozzle.gameObject.activeInHierarchy) continue;
                float share = e.Module.thrustTransformMultipliers != null && n < e.Module.thrustTransformMultipliers.Count ? e.Module.thrustTransformMultipliers[n] : 1f / all.Count;
                float guess = Engines.Guess(e.Module, share), radius = guess, at = 0f;
                try { if (!Engines.Mouth(e.Part, nozzle, all, guess, out radius, out at)) { radius = guess; at = 0f; } }
                catch (Exception) { radius = guess; at = 0f; }
                e.Nozzles.Add(nozzle); e.Radii.Add(radius); e.Rims.Add(at);
            }
        }

#if DEV
        /// <summary>(For the development build: the launch pads' own smoke effects, their colliders and their emitters, about the active vessel: east, north, up.)</summary>
        public static string Pads()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "no vessel";
            Vector3 o = v.transform.position, up = (Vector3)v.upAxis, north = Vector3.ProjectOnPlane((Vector3)v.mainBody.transform.up, up).normalized, east = Vector3.Cross(up, north);
            string At(Vector3 p) { Vector3 d = p - o; return "(" + Vector3.Dot(d, east).ToString("F1") + ", " + Vector3.Dot(d, north).ToString("F1") + ", " + Vector3.Dot(d, up).ToString("F1") + ")"; }
            string Dir(Vector3 d) => "(" + Vector3.Dot(d, east).ToString("F2") + ", " + Vector3.Dot(d, north).ToString("F2") + ", " + Vector3.Dot(d, up).ToString("F2") + ")";
            var say = new System.Text.StringBuilder("east, north, up from " + v.vesselName + "'s root:");
            var field = typeof(LaunchPadFX).GetField("ps", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            foreach (LaunchPadFX pad in FindObjectsOfType<LaunchPadFX>())
            {
                string path = pad.name;
                for (Transform t = pad.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                say.Append("\nLaunchPadFX ").Append(path).Append(" at ").Append(At(pad.transform.position)).Append(" tag ").Append(pad.tag).Append(" layer ").Append(pad.gameObject.layer);
                foreach (Collider c in pad.GetComponents<Collider>())
                    say.Append("\n  collider ").Append(c.GetType().Name).Append(" centre ").Append(At(c.bounds.center)).Append(" size ").Append(c.bounds.size.ToString("F1")).Append(c.isTrigger ? " trigger" : "");
                ParticleSystem[] ps = field != null ? field.GetValue(pad) as ParticleSystem[] : null;
                if (ps == null) { say.Append("\n  no ps"); continue; }
                foreach (ParticleSystem s in ps)
                {
                    if (s == null) continue;
                    var shape = s.shape; var main = s.main;
                    say.Append("\n  ps ").Append(s.name).Append(" at ").Append(At(s.transform.position)).Append(" forward ").Append(Dir(s.transform.forward)).Append(" up ").Append(Dir(s.transform.up))
                       .Append(" shape ").Append(shape.shapeType).Append(" angle ").Append(shape.angle.ToString("F0")).Append(" radius ").Append(shape.radius.ToString("F1")).Append(" box ").Append(shape.scale.ToString("F1"))
                       .Append(" rot ").Append(shape.rotation.ToString("F0")).Append(" pos ").Append(shape.position.ToString("F1")).Append(" speed ").Append(main.startSpeed.constantMax.ToString("F1"))
                       .Append(" size ").Append(main.startSize.constantMax.ToString("F1")).Append(" life ").Append(main.startLifetime.constantMax.ToString("F1")).Append(" scale ").Append(s.transform.lossyScale.ToString("F2"));
                }
            }
            // (and every collider tagged as the pad's, wherever it is)
            foreach (Collider c in FindObjectsOfType<Collider>())
                if (c != null && c.CompareTag("LaunchpadFX") && c.GetComponent<LaunchPadFX>() == null)
                    say.Append("\ntagged collider ").Append(c.name).Append(" centre ").Append(At(c.bounds.center)).Append(" size ").Append(c.bounds.size.ToString("F1"));
            return say.ToString();
        }

        /// <summary>(For the development build: what is feeding smoke, in words.)</summary>
        public static string Stats()
        {
            Trails it = FindObjectOfType<Trails>();
            if (it == null) return "not running";
            var say = new System.Text.StringBuilder(Report + " " + Why);
            foreach (Engine e in it.engines)
                say.Append("\n").Append(e.Part.partInfo != null ? e.Part.partInfo.name : e.Part.name).Append(": ").Append(e.Fuel).Append(", ").Append(e.Nozzles.Count).Append(" nozzles measured, running ")
                   .Append(Engines.Running(e.Module).ToString("F2")).Append(", hides ").Append(e.Hidden.Count);
            return say.ToString();
        }
#endif
    }

    /// <summary>
    /// Everything that names the Volumetric Explosions mod. Kept apart, and in the bodies of methods only, so that
    /// this mod loads where that one is missing: nothing here is looked at until it is called.
    /// </summary>
    static class Feed
    {
        public static void Hook(Action asked) { VolumetricExplosions.Sources.Asked += asked; }
        public static void Unhook(Action asked) { VolumetricExplosions.Sources.Asked -= asked; }
        public static bool Ready() => VolumetricExplosions.Sources.Ready;

        public static bool Burning(int id, Vector3 rim, Vector3 along, Vector3d going, float nozzle, float flame, float running, float amount, float shade, float warm, float lasts, int tint, Transform ship)
        {
            var exhaust = new VolumetricExplosions.Exhaust
            {
                id = id, rim = (Vector3d)rim, along = (Vector3d)along, going = going, nozzle = nozzle, flame = flame, running = running,
                amount = amount, shade = shade, warm = warm, lasts = lasts, tint = tint, rides = ship,
            };
            return VolumetricExplosions.Sources.Burning(exhaust);
        }
    }
}
