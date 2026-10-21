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

        private void Build()
        {
            if (dealSeed == 0) dealSeed = Math.Abs(Environment.TickCount ^ (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF)) | 1;
            var options = Drafting.Deal(dealSeed);
            if (countedSeed != dealSeed)
            {
                countedSeed = dealSeed;
                foreach (var o in options) { foreach (var id in o.Perks) Collection.See("perk:" + id); foreach (var id in o.Cards) Collection.See("card:" + id); }
            }
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            float pw = W * 0.94f, ph = H * 0.92f, top = ph / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            Text("DRAFT CUP", new Vector2(0f, top - 1.7f), 1.1f, pw - 2f, Color.white);
            Text("PICK ONE OF THREE TEAMS. EVERY OPPONENT IN THE 8-MATCH BRACKET HAS DRAFTED TOO. THE THREE ARE DEALT TO BE ABOUT EQUALLY STRONG.",
                 new Vector2(0f, top - 3.3f), 0.42f, pw - 3f, new Color(0.85f, 0.95f, 1f));

            float cw = (pw - 3f) / 3f, cardTop = top - 4.6f, ch = ph - 11.2f;
            for (int i = 0; i < options.Count; i++)
            {
                var o = options[i];
                float cx = -pw / 2f + 1f + (cw + 0.5f) * i + cw / 2f;
                var el = ElementColor(o.Team.Element);
                Box("Card", new Vector2(cx, cardTop - ch / 2f), new Vector2(cw, ch), Color.Lerp(new Color(0.02f, 0.2f, 0.34f), el, 0.12f), 2);
                Box("Stripe", new Vector2(cx, cardTop - 0.25f), new Vector2(cw, 0.5f), el, 3);
                float y = cardTop - 1.7f;
                Text($"<noparse>{o.Team.Name.ToUpperInvariant()}</noparse>", new Vector2(cx, y), 0.75f, cw - 1f, Color.white);
                y -= 1.2f;
                Text($"{o.Team.Element.ToUpperInvariant()}  ·  {o.Team.Slot.ToUpperInvariant()} TEAM", new Vector2(cx, y), 0.42f, cw - 1f, el);
                y -= 1.5f;
                Text("ABILITIES", new Vector2(cx, y), 0.42f, cw - 1f, new Color(1f, 0.85f, 0.35f));
                y -= 1.1f;
                foreach (var id in o.Perks)
                {
                    var p = Perks.All.First(x => x.Id == id);
                    string rc = "#" + ColorUtility.ToHtmlStringRGB(StatCards.RarityColor(p.Rarity));
                    string ec = "#" + ColorUtility.ToHtmlStringRGB(ElementColor(p.Element));
                    Text($"<color={rc}><noparse>{p.Title.ToUpperInvariant()}</noparse></color>  <size=75%><color={ec}>{p.Element.ToUpperInvariant().Replace("NONE", "")}</color></size>",
                         new Vector2(cx, y), 0.5f, cw - 1f, Color.white);
                    Text($"<noparse>{p.Description}</noparse>", new Vector2(cx, y - 1.25f), 0.36f, cw - 1.4f, new Color(0.85f, 0.95f, 1f), height: 1.8f, wrap: true);
                    y -= 2.9f;
                }
                Text("STAT CARDS", new Vector2(cx, y), 0.42f, cw - 1f, new Color(1f, 0.85f, 0.35f));
                y -= 1.1f;
                foreach (var id in o.Cards)
                {
                    var c = StatCards.Get(id);
                    string deltas = string.Join("  ", c.Stats.Select((s, k) => $"{StatCards.Label(s).ToUpperInvariant()} {(c.Points[k] > 0 ? "+" : "")}{c.Points[k]:0.##}"));
                    Text($"<noparse>{c.Title.ToUpperInvariant()}</noparse>  <size=75%><color=#BFE6FF>{deltas}</color></size>", new Vector2(cx, y), 0.45f, cw - 1f, Color.white);
                    y -= 1.1f;
                }
                // strength: the package's simulated power against the deal's target
                float k = Mathf.Clamp(o.Power / Mathf.Max(0.001f, Draft.Target), 0f, 1.3f) / 1.3f;
                float bw = cw - 3f, byy = cardTop - ch + 3.1f;
                if (o.SimWin > 0f) k = Mathf.Clamp01((o.SimWin - 0.5f) / 0.45f);        // the simulator's result for the whole package
                Text(o.SimWin > 0f ? $"STRENGTH  <size=80%>(SIMULATOR: WINS {o.SimWin:P0} OF EARLY MATCHES)</size>" : "STRENGTH",
                     new Vector2(cx, byy + 0.8f), 0.38f, cw - 1f, new Color(0.85f, 0.95f, 1f));
                Box("Bar", new Vector2(cx, byy), new Vector2(bw, 0.35f), new Color(0f, 0f, 0f, 0.35f), 3);
                Box("Fill", new Vector2(cx - bw / 2f + bw * k / 2f, byy), new Vector2(bw * k, 0.35f), new Color(1f, 0.85f, 0.35f), 4);
                var opt = o;
                Button("PICK", new Vector2(cx, cardTop - ch + 1.5f), new Vector2(cw * 0.5f, 2f), () => Pick(opt), 0.6f);
            }

            float by = -ph / 2f + 1.7f;
            var saved = RunSaves.DraftRun;
            if (saved.has && !string.IsNullOrEmpty(saved.draft))
            {
                var team = Drafting.TeamOf(saved.draft);
                Button($"CONTINUE  ({(team?.Name ?? "DRAFT").ToUpperInvariant()} · {RunSaves.Where(saved).ToUpperInvariant()})", new Vector2(-pw * 0.2f, by), new Vector2(pw * 0.3f, 2.2f), () => Continue(saved), 0.5f);
                Text("PICKING A NEW TEAM DROPS THE SAVED DRAFT CUP", new Vector2(-pw * 0.2f, by + 1.6f), 0.36f, pw * 0.35f, new Color(1f, 0.85f, 0.35f));
            }
            Button("BACK", new Vector2(pw * 0.2f, by), new Vector2(pw * 0.18f, 2.2f), () => Destroy(gameObject), 0.6f);
            var r = Records.Data;
            Text($"DRAFTS {r.draftsPlayed}  ·  CUPS WON {r.draftCupsWon}  ·  BEST STREAK {Records.Best(Records.Draft)}", new Vector2(0f, by + 3.2f), 0.42f, pw - 2f, new Color(1f, 0.85f, 0.35f));
            PadHint("A: PICK   ·   B: BACK", new Vector2(pw * 0.2f, by + 1.6f), pw * 0.3f);
            navKeys = true;
            focus = 0;
            Plugin.Log.LogInfo("Draft dealt (seed " + dealSeed + "): " + string.Join(" | ", options.Select(o => $"{o.Team.Name} [{string.Join(", ", o.Perks)}; {string.Join(", ", o.Cards)}] {o.Power:F3}")));
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
