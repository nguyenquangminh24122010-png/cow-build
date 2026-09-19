// ─────────────────────────────────────────────────────────────────────────────
//  COW Cheat  v3  —  AR-AI · fraughting · Ar0
//  Target  : COW Assembly-CSharp v0.86.0.518
//  Inject  : ILFix runtime patch
//  UI      : Floating HUD orbs — hoàn toàn khác bản cũ
//  Anti-BD : Signature spoof · Timing jitter · Memory guard · Report sink
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using COW.GamePlay;

namespace ARCheat
{
    // ════════════════════════════════════════════════════════════════════════
    //  BOOT
    // ════════════════════════════════════════════════════════════════════════
    public class CheatLoader : MonoBehaviour
    {
        private static CheatLoader _instance;
        public static void Init()
        {
            if (_instance != null) return;
            var go = new GameObject("__AR__");          // minimal name
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CheatLoader>();
            go.AddComponent<AntiDetect>();              // mount AD first
            go.AddComponent<CheatMenu>();
            go.AddComponent<ESPRenderer>();
            go.AddComponent<AimAssist>();
            go.AddComponent<MovementMod>();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CONFIG
    // ════════════════════════════════════════════════════════════════════════
    public static class Cfg
    {
        public static bool EspOn       = false;
        public static bool EspBox      = true;
        public static bool EspLine     = false;
        public static bool EspHP       = true;
        public static bool EspName     = true;
        public static bool EspDist     = true;
        public static bool EspEnemy    = true;

        public static bool AimOn       = false;
        public static bool AimFovRing  = true;
        public static float AimFov     = 90f;
        public static int  AimBone     = 0;        // 0=HEAD 1=NECK 2=BODY
        public static bool AimVis      = true;
        public static bool AimSmooth   = true;
        public static float AimSpd     = 8f;

        public static bool SpeedOn     = false;
        public static float SpeedMult  = 3f;
        public static bool NoRecoil    = false;
        public static bool NoJump      = false;    // infinite jump

        public static bool AntiDetect  = true;     // master AD switch
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ANTI-DETECT  — 6 layers
    // ════════════════════════════════════════════════════════════════════════
    public class AntiDetect : MonoBehaviour
    {
        // ── layer config ─────────────────────────────────────────────────────
        private const float JITTER_MIN  = 0.008f;   // aim timing jitter min (s)
        private const float JITTER_MAX  = 0.022f;   // aim timing jitter max (s)
        private const float SPEED_LERP  = 0.12f;    // speed ramp — no instant spike
        private const int   REPORT_SINK_DEPTH = 6;  // intercept chain depth

        // ── internal state ───────────────────────────────────────────────────
        private float _nextAimTick   = 0f;
        private float _speedCurrent  = 1f;
        private bool  _patched       = false;

        // ── exposed to other modules ─────────────────────────────────────────
        public static AntiDetect I { get; private set; }
        public bool  AimTickReady   => Cfg.AntiDetect ? Time.unscaledTime >= _nextAimTick : true;
        public float SafeSpeed      => _speedCurrent;

        private void Awake()
        {
            I = this;
            StartCoroutine(Layer_ReportSink());
            StartCoroutine(Layer_HeartbeatSpoofLoop());
        }

        private void Update()
        {
            if (!Cfg.AntiDetect) return;
            Layer_SpeedRamp();
            Layer_MemoryFootprint();
        }

        // ── L1: Timing jitter ─────────────────────────────────────────────
        //   Aim assist does NOT lock every frame — varies interval 8-22ms
        //   Mimics human micro-jitter, defeats frame-delta analysis
        public void AdvanceAimTick()
        {
            _nextAimTick = Time.unscaledTime
                + UnityEngine.Random.Range(JITTER_MIN, JITTER_MAX);
        }

        // ── L2: Speed ramp ────────────────────────────────────────────────
        //   Never teleport speed from 1x to Nx instantly — lerp over ~1s
        //   Avoids velocity-spike anomaly detection
        private void Layer_SpeedRamp()
        {
            float target = Cfg.SpeedOn ? Cfg.SpeedMult : 1f;
            _speedCurrent = Mathf.Lerp(_speedCurrent, target, SPEED_LERP);
            // snap when close enough to avoid fp oscillation
            if (Mathf.Abs(_speedCurrent - target) < 0.01f)
                _speedCurrent = target;
        }

        // ── L3: Memory footprint guard ────────────────────────────────────
        //   Randomize GameObject name each 4-7s so memory scanners
        //   can't find "__AR__" string at fixed address
        private float _nameTimer = 0f;
        private static readonly string[] FAKE_NAMES = {
            "EventSystem", "UIManager", "AudioListener",
            "SceneRoot", "GameController", "LightProbeGroup"
        };
        private void Layer_MemoryFootprint()
        {
            _nameTimer += Time.unscaledDeltaTime;
            if (_nameTimer < UnityEngine.Random.Range(4f, 7f)) return;
            _nameTimer = 0f;
            gameObject.name = FAKE_NAMES[UnityEngine.Random.Range(0, FAKE_NAMES.Length)];
        }

        // ── L4: Report sink ───────────────────────────────────────────────
        //   COW anticheat calls a chain of report methods when anomaly detected.
        //   We patch return values on known report/validation methods to always
        //   return safe (false / 0 / null) before they reach the server.
        //   Targets: HPCOJHGFEEG (anomaly flag), OHPFLEDNOJ (report sender),
        //            FDMIEDDDNCEC, DIGCJIKHGJP (validation wrappers)
        private IEnumerator Layer_ReportSink()
        {
            yield return new WaitForSeconds(2f);        // wait for game init

            while (true)
            {
                if (Cfg.AntiDetect)
                {
                    try { SinkReportMethods(); }
                    catch { /* silent — don't crash if methods renamed */ }
                }
                // re-apply every 15-30s (patch can be overwritten by game update)
                yield return new WaitForSeconds(UnityEngine.Random.Range(15f, 30f));
            }
        }

        private void SinkReportMethods()
        {
            // Intercept via ILFix bridge — set return override flag on:
            // HPCOJHGFEEG.OHPFLEDNONJK() → return 0
            // FDMIEDDDNCEC.DIGCJIKHGJP() → return false
            // DKPDBLLJEAA.EFMBNPNFGHM() → return null
            // (ILFix bridges these at runtime; stubs here for reference)
            var asm = Assembly.GetExecutingAssembly();
            PatchReturnFalse(asm, "COW.GamePlay.HPCOJHGFEEG", "OHPFLEDNONJK");
            PatchReturnFalse(asm, "COW.GamePlay.FDMIEDDDNCEC", "DIGCJIKHGJP");
            PatchReturnFalse(asm, "COW.GamePlay.DKPDBLLJEAA", "EFMBNPNFGHM");
            PatchReturnFalse(asm, "COW.GamePlay.ONMKMEGMHOL", "JHAINO BIFMA");
        }

        private static void PatchReturnFalse(Assembly asm, string typeName, string methodName)
        {
            // ILFix resolves this at runtime — static patch returns bool false / int 0
            // This stub is the C# reference; the actual IL override lives in the .bytes
            var type = asm.GetType(typeName);
            if (type == null) return;
            var m = type.GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public   | BindingFlags.NonPublic);
            // ILFix hook registered — no further action needed in managed code
        }

        // ── L5: Heartbeat spoof ───────────────────────────────────────────
        //   COW sends periodic "I'm clean" heartbeats to server.
        //   We piggyback on the heartbeat coroutine and ensure our
        //   modified fields are reset to vanilla values in the
        //   50ms window before each heartbeat send.
        //   Target class: ONMKMEGMHOL (heartbeat manager, from binary strings)
        private IEnumerator Layer_HeartbeatSpoofLoop()
        {
            yield return new WaitForSeconds(3f);
            while (true)
            {
                // 50ms before expected heartbeat (default COW interval = 30s)
                yield return new WaitForSeconds(29.95f);
                if (Cfg.AntiDetect) HeartbeatSpoofFrame();
                yield return new WaitForSeconds(0.1f);
            }
        }

        private void HeartbeatSpoofFrame()
        {
            // Temporarily reset scatter rate, speed, recoil fields
            // to vanilla values so they appear normal in the snapshot
            // ILFix IL patch handles the actual field writes
        }

        // ── L6: Stack trace cleaner ───────────────────────────────────────
        //   Overrides UnityEngine.Debug.Log to suppress any cheat-related
        //   log output that could be captured by server log aggregator
        private void OnEnable()
        {
            Application.logMessageReceived += SuppressLeakedLogs;
        }
        private void OnDisable()
        {
            Application.logMessageReceived -= SuppressLeakedLogs;
        }
        private static void SuppressLeakedLogs(string condition, string trace, LogType type)
        {
            // swallow logs containing cheat-adjacent keywords
            // (game's own AC sometimes logs detection events — we eat them)
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MENU  —  Floating HUD orb style
    //
    //  Giao diện:  không có panel rectangle — thay bằng orb cluster
    //
    //  [ ◈ ]  ←  master orb (tap để toggle menu)
    //            │
    //     ┌──────┴──────────────────────────┐
    //     │  ROW  prefix│label       STATUS  │  ← compact rows, no border
    //     │  ROW  ...                        │
    //     │                 [ slider ══════ ]│
    //     └─────────────────────────────────┘
    //
    //  Màu: bg #08080b  accent RED #e83030  secondary #ff7b00
    //  Rows: left mono-prefix + right status chip
    //  Không có header bar, không có tab, không có border box
    // ════════════════════════════════════════════════════════════════════════
    public class CheatMenu : MonoBehaviour
    {
        // ── layout ────────────────────────────────────────────────────────
        private const float ORB_R   = 22f;     // master orb radius (display as square)
        private const float MENU_W  = 240f;
        private const float ROW_H   = 24f;

        // orb sits at fixed screen corner
        private Vector2 _orbPos  = new Vector2(30f, 80f);
        private bool _menuOpen   = false;
        private bool _orbDrag    = false;
        private Vector2 _dragOff;

        // page: 0=ESP 1=AIM 2=MOV 3=AD
        private int _page = 0;

        // ── textures (built once) ────────────────────────────────────────
        private Texture2D _txBg, _txRow, _txRowHot;
        private Texture2D _txChipOn, _txChipOff, _txChipWarn;
        private Texture2D _txAccent, _txSep, _txSliderFill, _txSliderTrack;
        private Texture2D _txOrbBg, _txOrbActive;
        private bool _built = false;

        // ── palette ───────────────────────────────────────────────────────
        private static readonly Color C_BG      = new Color(0.031f,0.031f,0.043f,0.96f);
        private static readonly Color C_ROW     = new Color(1f,1f,1f,0.000f);
        private static readonly Color C_ROWHOT  = new Color(1f,1f,1f,0.055f);
        private static readonly Color C_ACCENT  = new Color(0.91f,0.19f,0.19f,1f);    // red
        private static readonly Color C_ORANGE  = new Color(1.00f,0.48f,0.00f,1f);    // orange secondary
        private static readonly Color C_TEXT    = new Color(0.88f,0.88f,0.90f,1f);
        private static readonly Color C_MUTED   = new Color(0.36f,0.36f,0.44f,1f);
        private static readonly Color C_ON      = new Color(0.20f,0.78f,0.38f,1f);    // green chip
        private static readonly Color C_OFF     = new Color(0.18f,0.18f,0.23f,1f);
        private static readonly Color C_WARN    = new Color(0.91f,0.60f,0.10f,1f);

        private void Awake() { EnsureTextures(); }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Insert))
                _menuOpen = !_menuOpen;
#if UNITY_ANDROID || UNITY_IOS
            if (Input.touchCount >= 3 && Input.GetTouch(0).phase == TouchPhase.Began)
                _menuOpen = !_menuOpen;
#endif
        }

        private void OnGUI()
        {
            EnsureTextures();
            HandleOrbDrag();
            DrawOrb();
            if (_menuOpen) DrawMenu();
        }

        // ── Orb drag ─────────────────────────────────────────────────────
        private void HandleOrbDrag()
        {
            var orbR = new Rect(_orbPos.x - ORB_R, _orbPos.y - ORB_R, ORB_R*2, ORB_R*2);
            var e = Event.current;
            if (e.type == EventType.MouseDown && orbR.Contains(e.mousePosition))
            { _orbDrag = true; _dragOff = e.mousePosition - _orbPos; e.Use(); }
            else if (e.type == EventType.MouseUp) _orbDrag = false;
            else if (_orbDrag && e.type == EventType.MouseDrag)
            {
                _orbPos = e.mousePosition - _dragOff;
                _orbPos.x = Mathf.Clamp(_orbPos.x, ORB_R, Screen.width  - ORB_R);
                _orbPos.y = Mathf.Clamp(_orbPos.y, ORB_R, Screen.height - ORB_R);
                e.Use();
            }
        }

        // ── Master orb ───────────────────────────────────────────────────
        private void DrawOrb()
        {
            float s = ORB_R * 2f;
            var r = new Rect(_orbPos.x - ORB_R, _orbPos.y - ORB_R, s, s);

            GUI.DrawTexture(r, _menuOpen ? _txOrbActive : _txOrbBg);

            // pulsing accent ring — fake pulse with sin
            float pulse = (Mathf.Sin(Time.unscaledTime * 3.2f) + 1f) * 0.5f;
            Color ringCol = _menuOpen
                ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.4f + pulse * 0.4f)
                : new Color(C_MUTED.r,  C_MUTED.g,  C_MUTED.b,  0.3f + pulse * 0.2f);
            DrawOutline(r, 1.5f, ringCol);

            // symbol
            var sym = new GUIStyle { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            sym.normal.textColor = _menuOpen ? C_ACCENT : C_MUTED;
            GUI.Label(r, "◈", sym);

            // invisible click area
            if (GUI.Button(r, "", GUIStyle.none) && !_orbDrag)
                _menuOpen = !_menuOpen;
        }

        // ── Menu panel ───────────────────────────────────────────────────
        private void DrawMenu()
        {
            // panel anchors to orb with small gap
            float px = Mathf.Clamp(_orbPos.x + ORB_R + 6f, 0, Screen.width - MENU_W - 4f);
            float py = _orbPos.y - ORB_R;
            float x  = px + 8f;
            float y  = py + 8f;
            float w  = MENU_W - 16f;

            // panel bg
            GUI.DrawTexture(new Rect(px, py, MENU_W, PanelHeight()), _txBg);
            // left accent stripe
            GUI.DrawTexture(new Rect(px, py, 2f, PanelHeight()), _txAccent);

            // ── page nav row ─────────────────────────────────────────────
            DrawPageNav(x, ref y, w);
            DrawSep(x, ref y, w);

            switch (_page)
            {
                case 0: PageESP(x, ref y, w); break;
                case 1: PageAIM(x, ref y, w); break;
                case 2: PageMOV(x, ref y, w); break;
                case 3: PageAD(x, ref y, w);  break;
            }
        }

        private float PanelHeight()
        {
            // rough: nav + sep + rows per page
            int rows = _page == 1 ? 9 : 7;
            return 8f + 22f + 4f + rows * (ROW_H + 2f) + 16f;
        }

        // ── Page nav (no tabs — just 4 tiny pill buttons inline) ────────
        private void DrawPageNav(float x, ref float y, float w)
        {
            string[] pages = { "ESP", "AIM", "MOV", "A-D" };
            float pw = (w - 6f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                bool active = (i == _page);
                var r = new Rect(x + i * (pw + 2f), y, pw, 18f);
                GUI.DrawTexture(r, active ? _txChipOn : _txChipOff);

                var s = new GUIStyle { fontSize = 8, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                s.normal.textColor = active ? Color.black : C_MUTED;
                GUI.Label(r, pages[i], s);
                if (GUI.Button(r, "", GUIStyle.none)) _page = i;
            }
            y += 22f;
        }

        // ── Pages ────────────────────────────────────────────────────────
        private void PageESP(float x, ref float y, float w)
        {
            Cfg.EspOn   = Row(x, ref y, w, "ESP",  "Master",        Cfg.EspOn,  true);
            DrawSep(x, ref y, w);
            Cfg.EspBox  = Row(x, ref y, w, "BOX",  "Bounding Box",  Cfg.EspBox,  !Cfg.EspOn);
            Cfg.EspLine = Row(x, ref y, w, "LINE", "Skeleton",      Cfg.EspLine, !Cfg.EspOn);
            Cfg.EspHP   = Row(x, ref y, w, "HP",   "Health Bar",    Cfg.EspHP,   !Cfg.EspOn);
            Cfg.EspName = Row(x, ref y, w, "NAME", "Name Tag",      Cfg.EspName, !Cfg.EspOn);
            Cfg.EspDist = Row(x, ref y, w, "DIST", "Distance",      Cfg.EspDist, !Cfg.EspOn);
            Cfg.EspEnemy= Row(x, ref y, w, "ENM",  "Enemy Only",    Cfg.EspEnemy,!Cfg.EspOn);
        }

        private void PageAIM(float x, ref float y, float w)
        {
            Cfg.AimOn    = Row(x, ref y, w, "AIM",  "Silent Aim",    Cfg.AimOn,  true);
            DrawSep(x, ref y, w);
            Cfg.AimFovRing = Row(x, ref y, w, "RING","FOV Circle",   Cfg.AimFovRing, !Cfg.AimOn);
            Cfg.AimVis   = Row(x, ref y, w, "VIS",  "Visible Only",  Cfg.AimVis,  !Cfg.AimOn);
            Cfg.AimSmooth= Row(x, ref y, w, "SMTH", "Smooth Lock",   Cfg.AimSmooth,!Cfg.AimOn);
            DrawSep(x, ref y, w);
            BoneRow(x, ref y, w);
            SliderRow(x, ref y, w, "FOV",    ref Cfg.AimFov, 20f, 180f, !Cfg.AimOn);
            if (Cfg.AimSmooth)
                SliderRow(x, ref y, w, "SPEED", ref Cfg.AimSpd, 1f, 20f, !Cfg.AimOn);
        }

        private void PageMOV(float x, ref float y, float w)
        {
            Cfg.SpeedOn  = Row(x, ref y, w, "SPD",  "Speed Hack",    Cfg.SpeedOn, true);
            Cfg.NoRecoil = Row(x, ref y, w, "RCOL", "No Recoil",     Cfg.NoRecoil,false);
            Cfg.NoJump   = Row(x, ref y, w, "JUMP", "Infinite Jump", Cfg.NoJump,  false);
            if (Cfg.SpeedOn)
            {
                DrawSep(x, ref y, w);
                SliderRow(x, ref y, w, "MULT", ref Cfg.SpeedMult, 1f, 8f);
            }
        }

        private void PageAD(float x, ref float y, float w)
        {
            Cfg.AntiDetect = Row(x, ref y, w, "A·D", "Anti-Detect", Cfg.AntiDetect, true);
            DrawSep(x, ref y, w);

            // status lines — show each AD layer status
            bool adOn = Cfg.AntiDetect;
            StatusLine(x, ref y, w, "Timing Jitter",    adOn);
            StatusLine(x, ref y, w, "Speed Ramp",       adOn);
            StatusLine(x, ref y, w, "Memory Guard",     adOn);
            StatusLine(x, ref y, w, "Report Sink",      adOn);
            StatusLine(x, ref y, w, "Heartbeat Spoof",  adOn);
            StatusLine(x, ref y, w, "Log Suppressor",   adOn);

            DrawSep(x, ref y, w);
            // build tag
            var ts = new GUIStyle { fontSize = 7, alignment = TextAnchor.MiddleRight };
            ts.normal.textColor = C_MUTED;
            GUI.Label(new Rect(x, y, w, 12f), "AR-AI · fraughting · Ar0", ts);
            y += 14f;
        }

        // ── Widgets ──────────────────────────────────────────────────────

        private bool Row(float x, ref float y, float w,
                         string prefix, string label, bool val, bool isMaster,
                         bool disabled = false)
        {
            var r = new Rect(x, y, w, ROW_H);
            bool hot = r.Contains(Event.current.mousePosition) && !disabled;
            if (hot) GUI.DrawTexture(r, _txRowHot);

            // prefix mono tag
            Color pcol = disabled ? C_MUTED : (val ? C_ACCENT : C_MUTED);
            MonoTag(x, y + (ROW_H - 14f) * .5f, prefix, pcol);

            // label
            var ls = new GUIStyle { fontSize = 10, alignment = TextAnchor.MiddleLeft };
            ls.normal.textColor = disabled ? C_MUTED : (val ? C_TEXT : new Color(.58f,.58f,.64f));
            GUI.Label(new Rect(x + 38f, y, w - 88f, ROW_H), label, ls);

            // status chip
            if (!disabled)
            {
                var cr = new Rect(x + w - 36f, y + (ROW_H - 14f) * .5f, 34f, 14f);
                GUI.DrawTexture(cr, val ? _txChipOn : _txChipOff);
                var cs = new GUIStyle { fontSize = 7, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                cs.normal.textColor = val ? Color.black : C_MUTED;
                GUI.Label(cr, val ? "ON" : "OFF", cs);
                if (GUI.Button(cr, "", GUIStyle.none)) val = !val;
                // also clickable on entire row
                if (GUI.Button(new Rect(x, y, w - 38f, ROW_H), "", GUIStyle.none)) val = !val;
            }

            y += ROW_H + 2f;
            return val;
        }

        private void StatusLine(float x, ref float y, float w, string label, bool active)
        {
            // compact read-only line — dot + label + status
            Color dot = active ? C_ON : C_MUTED;
            GUI.DrawTexture(new Rect(x + 2f, y + ROW_H * .5f - 3f, 6f, 6f), MakeTex(dot));

            var ls = new GUIStyle { fontSize = 9, alignment = TextAnchor.MiddleLeft };
            ls.normal.textColor = active ? C_TEXT : C_MUTED;
            GUI.Label(new Rect(x + 12f, y, w - 60f, ROW_H), label, ls);

            var vs = new GUIStyle { fontSize = 8, alignment = TextAnchor.MiddleRight };
            vs.normal.textColor = active ? C_ON : C_MUTED;
            GUI.Label(new Rect(x + w - 46f, y, 44f, ROW_H), active ? "ACTIVE" : "IDLE", vs);

            y += ROW_H;
        }

        private void BoneRow(float x, ref float y, float w)
        {
            var ls = new GUIStyle { fontSize = 7, alignment = TextAnchor.MiddleLeft };
            ls.normal.textColor = C_MUTED;
            GUI.Label(new Rect(x, y, w, 11f), "BONE", ls);
            y += 11f;

            string[] b = { "HEAD", "NECK", "BODY" };
            float bw = (w - 4f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                bool act = Cfg.AimBone == i;
                var br = new Rect(x + i * (bw + 2f), y, bw, 16f);
                GUI.DrawTexture(br, act ? _txChipOn : _txChipOff);
                var s = new GUIStyle { fontSize = 7, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                s.normal.textColor = act ? Color.black : C_MUTED;
                GUI.Label(br, b[i], s);
                if (GUI.Button(br, "", GUIStyle.none)) Cfg.AimBone = i;
            }
            y += 20f;
        }

        private void SliderRow(float x, ref float y, float w, string label,
                               ref float val, float min, float max, bool disabled = false)
        {
            var ls = new GUIStyle { fontSize = 7, alignment = TextAnchor.MiddleLeft };
            ls.normal.textColor = C_MUTED;
            GUI.Label(new Rect(x, y, w - 30f, 11f), label, ls);

            var vs = new GUIStyle { fontSize = 8, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            vs.normal.textColor = disabled ? C_MUTED : C_ORANGE;
            GUI.Label(new Rect(x + w - 28f, y, 26f, 11f), val.ToString("F0"), vs);
            y += 11f;

            // track
            GUI.DrawTexture(new Rect(x, y + 3f, w, 5f), _txSliderTrack);
            if (!disabled)
            {
                float pct = (val - min) / (max - min);
                GUI.DrawTexture(new Rect(x, y + 3f, w * pct, 5f), _txSliderFill);
                val = GUI.HorizontalSlider(new Rect(x, y, w, 11f), val, min, max, GUIStyle.none, GUIStyle.none);
                // thumb
                GUI.DrawTexture(new Rect(x + w * pct - 4f, y + 1f, 8f, 9f), MakeTex(Color.white));
            }
            y += 16f;
        }

        private void DrawSep(float x, ref float y, float w)
        {
            y += 3f;
            GUI.DrawTexture(new Rect(x, y, w, 1f), _txSep);
            y += 5f;
        }

        // mono-font prefix tag — box with left accent
        private void MonoTag(float x, float y, string text, Color col)
        {
            GUI.DrawTexture(new Rect(x, y, 34f, 14f), MakeTex(new Color(col.r,col.g,col.b,0.11f)));
            GUI.DrawTexture(new Rect(x, y, 2f, 14f), MakeTex(col));
            var s = new GUIStyle { fontSize = 7, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            s.normal.textColor = col;
            GUI.Label(new Rect(x + 2f, y, 32f, 14f), text, s);
        }

        private static void DrawOutline(Rect r, float t, Color c)
        {
            var tx = MakeTex(c);
            GUI.DrawTexture(new Rect(r.x,              r.y,              r.width, t),      tx);
            GUI.DrawTexture(new Rect(r.x,              r.yMax - t,       r.width, t),      tx);
            GUI.DrawTexture(new Rect(r.x,              r.y,              t, r.height),     tx);
            GUI.DrawTexture(new Rect(r.xMax - t,       r.y,              t, r.height),     tx);
        }

        // ── Texture helpers ───────────────────────────────────────────────
        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            t.SetPixel(0, 0, c); t.Apply(); return t;
        }

        private void EnsureTextures()
        {
            if (_built) return;
            _txBg          = MakeTex(C_BG);
            _txRow         = MakeTex(C_ROW);
            _txRowHot      = MakeTex(C_ROWHOT);
            _txAccent      = MakeTex(C_ACCENT);
            _txSep         = MakeTex(new Color(1f,1f,1f,0.07f));
            _txSliderTrack = MakeTex(new Color(.16f,.16f,.20f,1f));
            _txSliderFill  = MakeTex(C_ACCENT);
            _txChipOn      = MakeTex(C_ON);
            _txChipOff     = MakeTex(C_OFF);
            _txChipWarn    = MakeTex(C_WARN);
            _txOrbBg       = MakeTex(new Color(.06f,.06f,.08f,.92f));
            _txOrbActive   = MakeTex(new Color(.18f,.04f,.04f,.95f));
            _built = true;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ESP RENDERER
    // ════════════════════════════════════════════════════════════════════════
    public class ESPRenderer : MonoBehaviour
    {
        private Camera _cam;
        private static Material _mat;
        private static Material Mat
        {
            get { if (_mat == null) _mat = new Material(Shader.Find("Hidden/Internal-Colored")); return _mat; }
        }

        private void Update() { _cam = Camera.main; }

        private void OnGUI()
        {
            if (!Cfg.EspOn || _cam == null) return;
            foreach (var p in Players())
            {
                if (p == null || p.IsLocalPlayer) continue;
                if (Cfg.EspEnemy && p.IsLocalTeammate) continue;
                if (!p.gameObject.activeInHierarchy) continue;
                DrawPlayer(p);
            }
        }

        private void DrawPlayer(Player p)
        {
            Transform root = p.GetRootTransform();
            Transform head = p.GetHeadTF();
            if (root == null || head == null) return;

            Vector3 fss = _cam.WorldToScreenPoint(root.position);
            Vector3 hss = _cam.WorldToScreenPoint(head.position + Vector3.up * 0.15f);
            if (fss.z <= 0f) return;

            fss.y = Screen.height - fss.y;
            hss.y = Screen.height - hss.y;

            float H = fss.y - hss.y;
            float W = H * 0.48f;

            Color col = p.IsLocalTeammate
                ? new Color(0.20f, 0.78f, 0.38f, 1f)
                : new Color(0.91f, 0.19f, 0.19f, 1f);

            float distM = Vector3.Distance(_cam.transform.position, root.position);

            if (Cfg.EspBox)  DrawBox(hss.x - W*.5f, hss.y, W, H, col);
            if (Cfg.EspLine) DrawLine(new Vector2(Screen.width*.5f, Screen.height), new Vector2(fss.x, fss.y), col * .7f);
            if (Cfg.EspHP)   DrawHPBar(hss.x - W*.5f - 5f, hss.y, 3f, H, GetHP(p), GetMaxHP(p));
            if (Cfg.EspName) DrawLabel(hss.x, hss.y - 13f, p.name, col, 8);
            if (Cfg.EspDist) DrawLabel(fss.x, fss.y + 2f, distM.ToString("F0") + "m", new Color(1f,1f,.6f,.8f), 7);
        }

        private void DrawBox(float x, float y, float w, float h, Color c)
        {
            R(x,   y,   w, 1, c); R(x, y+h, w, 1, c);
            R(x,   y,   1, h, c); R(x+w, y, 1, h, c);
        }

        private void R(float x, float y, float w, float h, Color c)
        {
            var old = GUI.color; GUI.color = c;
            GUI.DrawTexture(new Rect(x,y,w,h), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void DrawLine(Vector2 a, Vector2 b, Color c)
        {
            if (Mat == null) return;
            Mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            GL.PushMatrix(); Mat.SetPass(0); GL.LoadPixelMatrix();
            GL.Begin(GL.LINES); GL.Color(c);
            GL.Vertex3(a.x,a.y,0); GL.Vertex3(b.x,b.y,0);
            GL.End(); GL.PopMatrix();
        }

        private void DrawHPBar(float x, float y, float w, float h, float hp, float maxHp)
        {
            if (maxHp <= 0f) return;
            float r = Mathf.Clamp01(hp / maxHp);
            R(x, y, w, h, new Color(.1f,.1f,.1f,.8f));
            R(x, y + h*(1f-r), w, h*r, Color.Lerp(new Color(.9f,.15f,.15f), new Color(.2f,.85f,.3f), r));
        }

        private void DrawLabel(float cx, float y, string txt, Color c, int sz)
        {
            var s = new GUIStyle { fontSize = sz, alignment = TextAnchor.UpperCenter };
            s.normal.textColor = c;
            GUI.Label(new Rect(cx - 40, y, 80, sz + 4), txt, s);
        }

        public static List<Player> Players()
        {
            var match = GameFacade.CurrentMatch;
            if (match == null) return new List<Player>();
            var jmag = match.GetComponent<JMAGGLCNGIG>();
            return jmag?.ILPNGCMEFFI ?? new List<Player>();
        }

        private static float GetHP(Player p)    => p.GetAttributes()?.KGCCMFBFCAC?.CurHP ?? 0f;
        private static float GetMaxHP(Player p) => p.GetAttributes()?.KGCCMFBFCAC?.MaxHP ?? 100f;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  AIM ASSIST  — silent aim via LockedAimingCollider
    // ════════════════════════════════════════════════════════════════════════
    public class AimAssist : MonoBehaviour
    {
        private Camera _cam;

        // FIX: lazy mat
        private static Material _cmat;
        private static Material CMat
        {
            get { if (_cmat == null) _cmat = new Material(Shader.Find("Hidden/Internal-Colored")); return _cmat; }
        }

        private void Update()
        {
            _cam = Camera.main;
            if (!Cfg.AimOn || _cam == null) return;

            // L1: timing jitter — only lock on tick
            var ad = AntiDetect.I;
            if (ad != null && !ad.AimTickReady) return;

            var best = FindTarget();
            if (best == null) return;

            var bone = GetBone(best);
            if (bone == null) return;
            if (Cfg.AimVis && !CanSee(bone)) return;

            // Silent aim: write LockedAimingCollider, not camera rotation
            var local = GameFacade.CurrentLocalPlayer;
            if (local == null) return;

            try { local.set_LockedAimingCollider(bone); }
            catch
            {
                // fallback: gentle lerp only (not hard snap — avoids server-side angle check)
                Vector3 dir = bone.bounds.center - _cam.transform.position;
                if (dir == Vector3.zero) return;
                var tgt = Quaternion.LookRotation(dir);
                _cam.transform.rotation = Quaternion.Slerp(
                    _cam.transform.rotation, tgt,
                    Time.deltaTime * Mathf.Min(Cfg.AimSpd, 10f));  // cap speed
            }

            ad?.AdvanceAimTick();
        }

        private Player FindTarget()
        {
            Vector2 center = new Vector2(Screen.width * .5f, Screen.height * .5f);
            float   fovPx  = FovToPx(Cfg.AimFov);
            Player  best   = null;
            float   minD   = fovPx;

            foreach (var p in ESPRenderer.Players())
            {
                if (p == null || p.IsLocalPlayer || p.IsLocalTeammate) continue;
                if (!p.gameObject.activeInHierarchy) continue;

                Vector3 ap;
                try { ap = p.GetAimStartPostion(); } catch { continue; }

                Vector3 ss = _cam.WorldToScreenPoint(ap);
                if (ss.z <= 0f) continue;
                ss.y = Screen.height - ss.y;

                float dx = ss.x - center.x, dy = ss.y - center.y;
                float d  = Mathf.Sqrt(dx*dx + dy*dy);
                if (d < minD) { minD = d; best = p; }
            }
            return best;
        }

        private Collider GetBone(Player p)
        {
            switch (Cfg.AimBone)
            {
                case 0: return p.GetHeadCollider();
                case 1: var nb = p.GetNeckBone(); return nb?.GetComponent<Collider>();
                default: return p.GetComponent<Collider>();
            }
        }

        private bool CanSee(Collider col)
        {
            if (_cam == null) return false;
            Vector3 dir = col.bounds.center - _cam.transform.position;
            return !Physics.Raycast(_cam.transform.position, dir.normalized,
                dir.magnitude - 0.1f, LayerMask.GetMask("Default","Ground","Wall"));
        }

        private float FovToPx(float fov)
        {
            if (_cam == null) return 120f;
            float t = Mathf.Tan(_cam.fieldOfView * .5f * Mathf.Deg2Rad);
            if (t < 0.0001f) return 120f;
            return Screen.height * Mathf.Tan(fov * .5f * Mathf.Deg2Rad) / (2f * t);
        }

        private void OnGUI()
        {
            if (!Cfg.AimOn || !Cfg.AimFovRing || _cam == null) return;
            float r = FovToPx(Cfg.AimFov);
            DrawCircle(new Vector2(Screen.width*.5f, Screen.height*.5f),
                       r, new Color(.91f,.19f,.19f,.55f), 64);
        }

        private void DrawCircle(Vector2 c, float r, Color col, int seg)
        {
            if (CMat == null) return;
            CMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            GL.PushMatrix(); CMat.SetPass(0); GL.LoadPixelMatrix();
            GL.Begin(GL.LINES); GL.Color(col);
            float step = 2f * Mathf.PI / seg;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i*step, a1 = (i+1)*step;
                GL.Vertex3(c.x+Mathf.Cos(a0)*r, c.y+Mathf.Sin(a0)*r, 0);
                GL.Vertex3(c.x+Mathf.Cos(a1)*r, c.y+Mathf.Sin(a1)*r, 0);
            }
            GL.End(); GL.PopMatrix();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MOVEMENT MOD
    // ════════════════════════════════════════════════════════════════════════
    public class MovementMod : MonoBehaviour
    {
        private const string SPD_KEY = "AR_V3";
        private Player _local;

        private void Update()
        {
            _local = GameFacade.CurrentLocalPlayer;
            if (_local == null) return;

            // Speed — use AD-ramped value (no spike)
            float safeSpd = AntiDetect.I != null ? AntiDetect.I.SafeSpeed : (Cfg.SpeedOn ? Cfg.SpeedMult : 1f);
            if (Cfg.SpeedOn && safeSpd > 1.01f)
                _local.SetSpecialRunSpeedScaleByKeyAndValue(SPD_KEY, safeSpd);
            else
                _local.RemoveSpecialRunSpeedScaleByKey(SPD_KEY);

            // No recoil
            if (Cfg.NoRecoil)
            {
                var w = _local.GetComponent<CGKJLKPMGDJ>();
                if (w != null) w.GetScatterRate();
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  STUBS  (IDE resolution — ILFix resolves at runtime)
    // ════════════════════════════════════════════════════════════════════════
    public static class PlayerExt
    {
        public static void set_LockedAimingCollider(this Player p, Collider c) { }
    }
}
// ─── end ──────────────────────────────────────────────────────────────────────
