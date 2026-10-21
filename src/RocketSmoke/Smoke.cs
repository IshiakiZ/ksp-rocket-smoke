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
        public const string Version = "0.3.0";

        public static Mod Page;
        public static Toggle On, Hide, Trails, Deluge;
        public static Slider Much, Hangs;

        static void Faster() => Page.Faster(Hangs, "0.6");

        public static void Setup()
        {
            if (Page != null && Kit.Find("Rocket Smoke") == Page) return;
            Page = Kit.Register("Rocket Smoke", Version, "The smoke of a rocket's engines as real smoke: it rolls out over the pad, takes the light, leans with the wind, and hangs where the rocket has been.");
            On = Page.Toggle("on", "Rocket smoke", true, "Off: the smoke the game draws.");
            Much = Page.Slider("much", "How much", 1f, 0.3f, 2.5f, "", 2, "How much smoke the engines make.");
            Hangs = Page.Slider("hangs", "How long it hangs", 1f, 0.3f, 3f, "", 2, "How long the smoke stays in the air. Longer costs more: there is more of it at once.");
            try { Faster(); } catch (Exception) { }             // (what "every mod set for speed" sets here; not with a Keystone older than 0.9.0)
            Hide = Page.Toggle("hide", "Hide the game's own smoke", true, "While an engine's smoke is drawn by this mod, the smoke the game draws for it is hidden.");
            Trails = Page.Toggle("trails", "Jets' trails", true, "High up, where the air is cold enough, a jet leaves a white trail of ice behind it, which lingers where the air is damp (with the Natural Weather mod, as the weather says).");
            Deluge = Page.Toggle("deluge", "The pad's water deluge", true, "As at real launch pads, water is poured under a rocket lifting off, and its flame boils it away: the white billows round a launch are mostly that steam.");
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
            public bool Jet;                   // (since 0.3.0) it breathes air: no smoke of its own, but a trail where the air is cold enough (see Contrail)
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
            // (the nozzles measured by Keystone ahead of the lift-off, not at it: see Keystone's Engines.MeasureAhead)
            if (hooked) try { AskAhead(); } catch (Exception) { }
        }

        // (in a method of its own: a Keystone from before 2026-10-18 has no such thing, and it is the method that names it that fails)
        static void AskAhead() => Engines.MeasureAhead = true;

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
                        bool jet = fuel == Fuel.None && Breathes(module);
                        if (amount <= 0f && !jet) continue;
                        engines.Add(new Engine { Module = module, Part = part, Fuel = fuel, Jet = jet });
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

        /// <summary>Whether an engine breathes the air (a jet, or a RAPIER breathing): what it burns it takes in from round it.</summary>
        static bool Breathes(ModuleEngines module)
        {
            if (module.engineType == EngineType.Turbine) return true;
            foreach (Propellant p in module.propellants) if (p.name == "IntakeAir" || p.name == "IntakeAtm") return true;
            return false;
        }

        /// <summary>
        /// A jet's trail (since 0.3.0). A jet makes no smoke worth the name; what it is seen by, high up, is its trail: the water
        /// its fuel burns to, let out into air cold enough, freezes into a white line behind it. Whether it does is Schmidt and
        /// Appleman's reckoning: hot damp exhaust mixing into the air passes through saturation on the way if the air is colder
        /// than a limit, about −43 °C at airliners' heights (a quarter of the ground's pressure), warmer in thicker air and colder
        /// in thinner (here from that reckoning for an engine turning three tenths of its fuel's heat into thrust), and a little
        /// warmer for air already damp. And whether the line stays is the air's: where it is saturated over ice (ice holds less
        /// vapour than water: at −40 °C, air at two thirds of what it could hold over water) it spreads and lingers for most of
        /// a minute; drier, it is gone within seconds, a short white tail. How damp the air is, the weather mod says, if there is
        /// one (through Volumetric Explosions: see its Sources.Damp); without one it is taken as fairly dry. Begins a plane's
        /// length or two behind the engine, where the exhaust has cooled into the air, as a real one does.
        /// </summary>
        static bool Contrail(Vessel v, Vector3 at, out float thick, out float lasts)
        {
            thick = 0f; lasts = 0f;
            CelestialBody body = v.mainBody;
            if (body == null || !body.atmosphere || !body.atmosphereContainsOxygen) return false;
            double alt = body.GetAltitude(at), pressure = FlightGlobals.getStaticPressure(alt, body);
            if (pressure < 1.0) return false;
            float kelvin = (float)FlightGlobals.getExternalTemperature(alt, body);
            float rh = damp ? DampAt(at) : -1f;
            if (rh < 0f) rh = 0.4f;
            float g = 1.45f * (float)pressure / 25f, lg = Mathf.Log(Mathf.Max(g - 0.053f, 0.02f));
            float limit = 273.15f - 46.46f + 9.43f * lg + 0.72f * lg * lg - 4f * (1f - Mathf.Clamp01(rh));
            if (kelvin > limit) return false;
            float overIce = rh * (1f + 0.012f * (273.15f - kelvin));
            lasts = overIce >= 1f ? Mathf.Lerp(25f, 55f, Mathf.Clamp01((overIce - 1f) * 5f)) : Mathf.Lerp(0.8f, 6f, Mathf.Clamp01((overIce - 0.4f) * 1.7f));
            thick = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01((limit - kelvin) / 8f));
            return true;
        }

        /// <summary>
        /// (Since 0.3.0.) How much of a rocket's trail is ice: what its exhaust is of water, frozen. Where the air is cold enough it
        /// freezes as it mixes in (the exhaust is so wet that it does so well short of the −40 °C a jet's needs: from about
        /// −25 °C); and where the air is thin it freezes whatever the air's warmth, as it spreads: let out at near the ground's
        /// pressure into a few hundredths of it, the gas cools as it expands far past freezing (what the plume of a launch seen
        /// at dusk, lit against a dark sky, is made of). So from the cold top of the weather up, a rocket's trail is a white line
        /// of ice that catches the sun, far brighter than its smoke would be: hydrogen's all of it (it burns to water and nothing
        /// else), methane's two thirds, kerosene's about half, a solid motor's and the storable fuels' a third or so. Any air
        /// will do: the oxygen it burns with it carries.
        /// </summary>
        static float Frozen(Vessel v, Vector3 at, Fuel fuel)
        {
            CelestialBody body = v.mainBody;
            if (body == null || !body.atmosphere) return 0f;
            double alt = body.GetAltitude(at);
            float kelvin = (float)FlightGlobals.getExternalTemperature(alt, body), kPa = (float)FlightGlobals.getStaticPressure(alt, body);
            float water = fuel == Fuel.Hydrogen ? 1f : fuel == Fuel.Methane ? 0.67f : fuel == Fuel.Kerosene ? 0.5f : fuel == Fuel.Solid ? 0.35f : fuel == Fuel.Storable ? 0.35f : fuel == Fuel.Mono ? 0.3f : 0f;
            float cold = Mathf.Clamp01((248f - kelvin) / 20f), spread = Mathf.InverseLerp(2f, 0.3f, kPa);
#if DEV
            iceSaid = "at " + (alt / 1000.0).ToString("F1") + " km: " + kPa.ToString("F2") + " kPa, " + (kelvin - 273.15f).ToString("F0") + " °C; cold " + cold.ToString("F2") + ", spread " + spread.ToString("F2") + ", ice " + (water * Mathf.Max(cold, spread)).ToString("F2") + " (" + fuel + ")";
#endif
            return water * Mathf.Max(cold, spread);
        }
#if DEV
        static string iceSaid = "none yet";
        /// <summary>(For the development build: the last rocket trail's ice, and the air it was reckoned from.)</summary>
        public static string IceNow() => iceSaid;
#endif

        static bool damp = true;
        static float DampAt(Vector3 at)
        {
            try { return Feed.Damp(at, Planetarium.GetUniversalTime()); }
            catch (Exception) { damp = false; return -1f; }       // (an explosions mod older than 0.8.0: no word on the weather's dampness)
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
            int burning = 0, trailing = 0;
            deluged = 0;
            // (the pads looked for once a scene and when another craft is taken over: a search of the whole scene is a hitch, not to be
            // made on a timer, least of all round a lift-off; but again every few seconds early in a scene while none is found, as
            // the space centre may still be coming in)
            if (padsFor != FlightGlobals.ActiveVessel || (pads.Count == 0 && Time.timeSinceLevelLoad < 30f && Time.realtimeSinceStartup > padsAt))
            { LookForPads(); padsFor = FlightGlobals.ActiveVessel; padsAt = Time.realtimeSinceStartup + 3f; }
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
                if (e.Jet)
                {
                    // (a jet's trail, white ice, where the air is cold enough; and nothing of the game's own hidden for it)
                    if (Smoke.Trails != null && Smoke.Trails && Contrail(v, rim, out float thick, out float stays))
                    {
                        // (narrow and thin where it begins, as each engine's own line is: it spreads with the minutes, not the metres)
                        bool laid = Feed.Burning((int)e.Part.flightID * 31 + e.Module.GetInstanceID() % 31 + 7, rim, along, v.srf_velocity, 0.5f * Mathf.Sqrt(area), Mathf.Max(30f, 40f * each),
                                                 running, 0.13f * thick * running * much, 0.97f, 0f, stays * hangs, 4, v.transform);
                        if (laid) trailing++;
                    }
                    continue;
                }
                Smoke.Of(e.Fuel, out float amount, out float shade, out float warm, out float lasts, out int tint);
                // (and high up, in air cold enough, the water in it freezes: the trail turns white ice, thicker to see and slower to go)
                float ice = Frozen(v, rim, e.Fuel);
                if (ice > 0f) { amount *= 1f + 2.5f * ice; shade = Mathf.Lerp(shade, 0.96f, ice); warm *= 1f - ice; lasts *= 1f + ice; }
                // (and in damp air the water in the exhaust stays as cloud: see Wetted)
                float wet = Wetted(rim, e.Fuel) * (1f - ice);
                if (wet > 0.01f) { amount *= 1f + 1.6f * wet; shade = Mathf.Lerp(shade, 0.93f, wet); warm *= 1f - 0.6f * wet; lasts *= 1f + 1.2f * wet; }
                // (and on a launch pad, the water poured under it boils away into steam: see Deluge)
                float steam = Smoke.Deluge != null && Smoke.Deluge ? Deluge(rim, along) * running : 0f;
                if (steam > 0.01f)
                {
                    float rh = damp ? DampAt(rim) : -1f;
                    if (rh < 0f) rh = 0.7f;
                    float added = 0.85f * steam;
                    shade = Mathf.Lerp(shade, 0.95f, added / (amount + added));
                    warm *= amount / (amount + added);
                    // (steam lingers as a cloud in damp air and is gone in seconds in dry: what is seen is the water it condenses to)
                    lasts = Mathf.Lerp(lasts, Mathf.Lerp(7f, 22f, Mathf.Clamp01((rh - 0.4f) / 0.55f)), added / (amount + added));
                    amount += added;
                    deluged++;
                }
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
            Report = (burning > 0 ? burning + (burning == 1 ? " engine is" : " engines are") + " leaving smoke." : engines.Count > 0 ? "No engine is running where there is air to hold smoke." : "No rocket engine here.")
                     + (deluged > 0 ? " The pad's water is boiling away under " + (deluged == 1 ? "it." : deluged + " of them.") : "")
                     + (trailing > 0 ? " " + trailing + (trailing == 1 ? " jet is" : " jets are") + " leaving a trail." : "");
        }

        /// <summary>
        /// (Since 0.3.0.) How much more of a cloud an engine's exhaust is for the water in it, where the air is damp. Every fuel
        /// burns partly to water (hydrogen to nothing else), let out as vapour hotter than boiling. Mixing into dry air it stays
        /// vapour and is not seen; mixing into air near saturation, the mix passes through saturation on the way down to the air's
        /// warmth, and the water condenses into a white cloud of droplets that lasts as long as the air keeps it from drying (the
        /// white trail a hydrogen engine leaves on a humid morning, and none on a dry one). From about four fifths humidity
        /// (the weather mod's, through Volumetric Explosions; without one, nothing), by the share of the exhaust that is water.
        /// Below the ice of the cold air higher up (see Frozen), which takes over there.
        /// </summary>
        float Wetted(Vector3 at, Fuel fuel)
        {
            if (!damp) return 0f;
            float rh = DampAt(at);
            if (rh < 0f) return 0f;
            float water = fuel == Fuel.Hydrogen ? 1f : fuel == Fuel.Methane ? 0.67f : fuel == Fuel.Kerosene ? 0.5f : fuel == Fuel.Solid ? 0.35f : fuel == Fuel.Storable ? 0.35f : fuel == Fuel.Mono ? 0.3f : 0f;
            return water * Mathf.SmoothStep(0f, 1f, (rh - 0.78f) / 0.2f);
        }

        int deluged;
        Vessel padsFor;
        float padsAt;
        readonly List<BoxCollider> pads = new List<BoxCollider>();

        /// <summary>The launch pads of the scene: the box over each one's grate that the game drives its own launch smoke from (LaunchPadFX).</summary>
        void LookForPads()
        {
            pads.Clear();
            foreach (LaunchPadFX fx in FindObjectsOfType<LaunchPadFX>())
            {
                BoxCollider box = fx != null ? fx.GetComponent<BoxCollider>() : null;
                if (box != null) pads.Add(box);
            }
        }

        /// <summary>
        /// (Since 0.3.0.) The pad's water deluge. Real launch pads pour water onto the deck and into the flame trench while the
        /// engines light and the rocket climbs away, to take up the sound and the heat (Kennedy's pad 39B some 1,700 cubic metres
        /// in half a minute for the SLS, more than the rocket's own exhaust by weight); the flame boils a good part of it away, and
        /// most of the great white cloud that rolls out round a launch is that steam, not the exhaust, whatever burns: a kerosene
        /// rocket that leaves a thin brown haze in the open lifts off a pad in white billows. How much of an engine's jet falls on
        /// the deck and in the trench: where its line meets the grate's top, as wide as the jet has spread by then, and less of it
        /// as the rocket climbs, until some seventy metres up it no longer reaches the water.
        /// </summary>
        float Deluge(Vector3 rim, Vector3 along)
        {
            float best = 0f;
            foreach (BoxCollider box in pads)
            {
                if (box == null) continue;
                Transform t = box.transform;
                Vector3 middle = t.TransformPoint(box.center);
                if ((middle - rim).sqrMagnitude > 160f * 160f) continue;
                Vector3 up = (Vector3)FlightGlobals.getUpAxis(FlightGlobals.currentMainBody, (Vector3d)middle);
                float down = -Vector3.Dot(along, up);
                if (down < 0.35f) continue;
                float t0 = Vector3.Dot(rim - middle, up) / down;           // (how far along the jet the deck is)
                if (t0 < -2f || t0 > 90f) continue;
                Vector3 hit = rim + along * t0 - middle;
                float across = (hit - up * Vector3.Dot(hit, up)).magnitude;
                Vector3 size = Vector3.Scale(box.size, t.lossyScale);
                float reach = 0.5f * Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z)) + 12f;     // (the deck's water reaches past the grate)
                float onDeck = 1f - Mathf.SmoothStep(0f, 1f, (across - reach) / 10f);
                float near = 1f - Mathf.SmoothStep(0f, 1f, (t0 - 25f) / 45f);
                best = Mathf.Max(best, onDeck * near);
            }
            return best;
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

        /// <summary>(Volumetric Explosions 0.8 or later: how damp the weather mod says the air is there, 0 to 1; less than nothing where none says.)</summary>
        public static float Damp(Vector3 at, double ut)
        {
            var asked = VolumetricExplosions.Sources.Damp;
            return asked != null ? Mathf.Clamp01(asked((Vector3d)at, ut)) : -1f;
        }

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
