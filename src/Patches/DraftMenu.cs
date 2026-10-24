using System;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HangtimeOvertime.Generated;
using StatCards = HangtimeOvertime.Engine.StatCards;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // DRAFT (title bar): three teams, each with three abilities and two stat cards of about the same strength (sheets/
    // draft.json); PICK one to play an 8-match draft cup in which every opponent has drafted too. The deal stays the same
    // until a team is picked (closing the screen doesn't redraw it). CONTINUE resumes a saved draft cup.
    internal class DraftMenu : OvertimePanel
    {
        private static DraftMenu open;
        public static bool IsOpen => open != null;
        private static int dealSeed;                       // this session's deal, until a pick
        private static int countedSeed;                    // the deal already counted in the collection

        public static void Open()
        {
            if (open != null) return;
            var go = new GameObject("Overtime Draft");
            open = go.AddComponent<DraftMenu>();
            if (!open.InitKit(200)) { Destroy(go); open = null; return; }
            open.HoldTitle();
            open.Build();
        }

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.white;
        private static Color ElementColor(string id) { var e = Elements.All.FirstOrDefault(x => x.Id == id); return e.Id != null && id != "none" ? Hex(e.Color) : new Color(0.8f, 0.85f, 0.9f); }

        // three team cards like the tournaments screen: the team's emblem on its banner colour, badges, the three abilities
        // (rarity / element colours) and two stat cards, a flame meter for the simulator's strength, a gold PICK
        private void Build()
        {
            if (dealSeed == 0) dealSeed = Math.Abs(Environment.TickCount ^ (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF)) | 1;
            var options = Drafting.Deal(dealSeed);
            if (countedSeed != dealSeed)
            {
                countedSeed = dealSeed;
                foreach (var o in options) { foreach (var id in o.Perks) Collection.See("perk:" + id); foreach (var id in o.Cards) Collection.See("card:" + id); }
            }
            Frame("DRAFT CUP", () => Destroy(gameObject));
            TC("PICK ONE OF THREE TEAMS. EVERY OPPONENT IN THE 8-MATCH BRACKET HAS DRAFTED TOO. THE THREE ARE DEALT TO BE ABOUT EQUALLY STRONG.", 800, 170, 21, new Color(0.78f, 0.88f, 1f), 1460);

            for (int i = 0; i < options.Count; i++)
            {
                var o = options[i];
                float x0 = 70f + i * 493f, x1 = x0 + 473f, cx = (x0 + x1) / 2f;
                var el = ElementColor(o.Team.Element);
                Rect("Card", x0, 192, x1, 800, new Color(0.094f, 0.275f, 0.46f, 1f), 2, true);
                // the emblem on its banner
                Rect("Banner", x0 + 20, 208, x1 - 20, 300, Color.Lerp(Hex(o.Team.Banner), Color.black, 0.42f), 3);
                var tex = EmblemTexture(o.Team);
                if (tex != null) Picture(tex, P(cx, 254), 80f / 25f, 5);
                TC($"<noparse>{o.Team.Name.ToUpperInvariant()}</noparse>", cx, 328, 34, UiArt.Ink, 440);
                float bw = (o.Team.Element.Length + o.Team.Slot.Length + 7) * 9.6f + 66f;
                Badges(cx - bw / 2f, 362, (o.Team.Element.ToUpperInvariant(), el), (o.Team.Slot.ToUpperInvariant() + " TEAM", UiArt.Gold));
                TL("ABILITIES", x0 + 28, 394, 20, UiArt.Gold, 200);
                float y = 420;
                foreach (var id in o.Perks)
                {
                    var p = Perks.All.First(x => x.Id == id);
                    string rc = TournamentMenu.Hex(StatCards.RarityColor(p.Rarity));
                    string ec = TournamentMenu.Hex(ElementColor(p.Element));
                    TL($"<color={rc}><noparse>{p.Title.ToUpperInvariant()}</noparse></color>  <size=70%><color={ec}>{p.Element.ToUpperInvariant().Replace("NONE", "")}</color></size>", x0 + 28, y, 25, Color.white, 420);
                    TW($"<noparse>{p.Description}</noparse>", cx, y + 27, 15, new Color(0.78f, 0.88f, 1f), 420, 36);
                    y += 60;
                }
                TL("STAT CARDS", x0 + 28, 604, 20, UiArt.Gold, 200);
                y = 630;
                foreach (var id in o.Cards)
                {
                    var c = StatCards.Get(id);
                    string deltas = string.Join("  ", c.Stats.Select((st, k) => $"{StatCards.Label(st).ToUpperInvariant()} {(c.Points[k] > 0 ? "+" : "")}{c.Points[k]:0.##}"));
                    TL($"<noparse>{c.Title.ToUpperInvariant()}</noparse>  <size=70%><color=#C8E1FF>{deltas}</color></size>", x0 + 28, y, 22, UiArt.Ink, 420);
                    y += 26;
                }
                // strength: flames from the simulator's early-match win rate (or the package's power)
                float k = Mathf.Clamp(o.Power / Mathf.Max(0.001f, Draft.Target), 0f, 1.3f) / 1.3f;
                if (o.SimWin > 0f) k = Mathf.Clamp01((o.SimWin - 0.5f) / 0.45f);
                int lit = Mathf.Clamp(Mathf.RoundToInt(k * 10f), 1, 10);
                TL(o.SimWin > 0f ? $"STRENGTH  ·  WINS {Mathf.RoundToInt(o.SimWin * 100f)}% OF EARLY MATCHES (SIMULATOR)" : "STRENGTH", x0 + 28, 690, 16, new Color(0.78f, 0.88f, 1f), 420);
                for (int f = 0; f < 10; f++) Icon("flame", P(x0 + 40 + f * 22f, 711), 0.8f, f < lit ? UiArt.Orange : new Color(1f, 1f, 1f, 0.22f), 8);
                var opt = o;
                GoldSign("PICK", x0 + 90, 736, x1 - 90, 786, () => Pick(opt), 32, "check");
            }

            var saved = RunSaves.DraftRun;
            if (saved.has && !string.IsNullOrEmpty(saved.draft))
            {
                var team = Drafting.TeamOf(saved.draft);
                IconBtn("play", $"CONTINUE  {(team?.Name ?? "DRAFT").ToUpperInvariant()} · {RunSaves.Where(saved).ToUpperInvariant()}", 70, 846, 640, 892, () => Continue(saved), 22);
                TL("PICKING A NEW TEAM DROPS THE SAVED DRAFT CUP", 660, 869, 17, UiArt.Gold, 380);
            }
            var r = Records.Data;
            TC($"DRAFTS {r.draftsPlayed}  ·  CUPS WON {r.draftCupsWon}  ·  BEST STREAK {Records.Best(Records.Draft)}", 1100, 869, 20, UiArt.Gold, 520);
            PadHint("A: PICK   B: BACK", P(1100, 830), 300 / 25f * KX);
            navKeys = true;
            focus = 0;
            Plugin.Log.LogInfo("Draft dealt (seed " + dealSeed + "): " + string.Join(" | ", options.Select(o => $"{o.Team.Name} [{string.Join(", ", o.Perks)}; {string.Join(", ", o.Cards)}] {o.Power:F3}")));
        }

        // the team's shipped emblem (assets/emblems/<id>.png)
        private static readonly Dictionary<string, Texture2D> emblems = new Dictionary<string, Texture2D>();
        public static Texture2D EmblemTexture(TeamDef t)
        {
            if (t == null) return null;
            if (emblems.TryGetValue(t.Id, out var have) && have != null) return have;
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "emblems", t.Id + ".png");
            if (!System.IO.File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontUnloadUnusedAsset, wrapMode = TextureWrapMode.Clamp };
            tex.LoadImage(System.IO.File.ReadAllBytes(path));
            emblems[t.Id] = tex;
            return tex;
        }

        private static TitleButton ClassicButton() =>
            FindObjectsByType<TitleButton>(FindObjectsSortMode.None).FirstOrDefault(b => !ContinueMenu.IsInfinite(b) && ContinueMenu.Players(b) == 1);

        private void Pick(DraftOption o)
        {
            var tb = ClassicButton();
            if (tb == null) return;
            if (RunSaves.DraftRun.has) RunSaves.ClearDraft();
            foreach (var id in o.Perks) Collection.Pick("perk:" + id);
            foreach (var id in o.Cards) Collection.Pick("card:" + id);
            Drafting.Pending = o;
            Drafting.PendingSeed = dealSeed;
            dealSeed = 0;                                   // the next visit deals a new draft
            ContinueMenu.Bypass = true;
            Destroy(gameObject);
            tb.Click();
        }

        private void Continue(RunSnapshot snap)
        {
            var tb = ClassicButton();
            if (tb == null) return;
            Destroy(gameObject);
            ContinueMenu.Resume(tb, snap);
        }

        private void Update()
        {
            if (NavUpdate()) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Destroy(gameObject);
        }

        protected override void OnNavBack() => Destroy(gameObject);
        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }
    }
}
