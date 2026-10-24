using System;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Generated;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // MATCH RULES (the Versus / lobby screen's RULES button): points, rounds, upgrades per round and their choices, perks and
    // stat cards on or off, a rarity limit, starting perks, draft start, banned elements and perks; presets (sheets/
    // rules.json) and three custom slots. Online only the host edits (friends see the rules read-only); a change un-readies
    // everyone and goes to every PC with the lobby screen.
    internal class RulesPanel : OvertimePanel
    {
        private static RulesPanel open;
        public static bool IsOpen => open != null;
        private bool readOnly;
        private string note = "";

        public static void Open()
        {
            if (open != null) return;
            var go = new GameObject("Overtime Rules");
            open = go.AddComponent<RulesPanel>();
            if (!open.InitKit(290)) { Destroy(go); open = null; return; }
            open.readOnly = Online.Net.IsClient;
            open.Build();
        }

        private static RuleSet R => LobbyRules.Current;

        private void Build()
        {
            int keep = focus;
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops();
            navKeys = true;
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            float pw = W * 0.74f, ph = H * 0.92f, top = ph / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            Text("MATCH RULES", new Vector2(0f, top - 1.7f), 1.1f, pw - 2f, Color.white);
            string preset = LobbyRules.MatchingPreset();
            Text(readOnly ? "THE HOST'S RULES (ONLY THE HOST CAN CHANGE THEM)" : preset != null ? $"PRESET: {preset}" : "CUSTOM RULES (NOT SAVED)",
                 new Vector2(0f, top - 3.1f), 0.5f, pw - 2f, new Color(1f, 0.85f, 0.35f));

            // two columns: numbers on the left, switches and the perk bans on the right; the preset across the top
            float row = 2.1f, colW = pw * 0.46f;
            float y = 0f, cx = 0f;
            void Stepper(string label, string value, Action down, Action up)
            {
                Text(label, new Vector2(cx - colW * 0.25f, y), 0.5f, colW * 0.48f, Color.white, TextAlignmentOptions.Right);
                Text(value, new Vector2(cx + colW * 0.25f, y), 0.58f, colW * 0.24f, new Color(1f, 0.85f, 0.35f));
                if (!readOnly)
                {
                    Button("<", new Vector2(cx + colW * 0.08f, y), new Vector2(1.7f, 1.6f), () => { down(); Changed(); }, 0.6f);
                    Button(">", new Vector2(cx + colW * 0.42f, y), new Vector2(1.7f, 1.6f), () => { up(); Changed(); }, 0.6f);
                }
                y -= row;
            }
            void Switch(string label, bool value, Action flip)
            {
                Text(label, new Vector2(cx - colW * 0.25f, y), 0.5f, colW * 0.48f, Color.white, TextAlignmentOptions.Right);
                if (readOnly) Text(value ? "<color=#7CFF8A>ON</color>" : "<color=#FF8A7C>OFF</color>", new Vector2(cx + colW * 0.25f, y), 0.58f, colW * 0.3f, Color.white);
                else Toggle("", new Vector2(cx + colW * 0.25f, y), new Vector2(colW * 0.38f, 1.6f), () => value, _ => { flip(); Changed(); }, 0.5f);
                y -= row;
            }
            var presets = PresetList();
            if (!readOnly)
            {
                int cur = presets.FindIndex(p => p.name == preset);
                cx = 0f; y = top - 5f;
                Stepper("PRESET", preset ?? "-", () => Apply(presets[(cur - 1 + presets.Count) % presets.Count].rules), () => Apply(presets[(cur + 1) % presets.Count].rules));
            }
            float colTop = top - 7.4f;
            cx = -pw * 0.24f; y = colTop;
            Stepper("POINTS PER ROUND", R.Points.ToString(), () => R.Points = Wrap(R.Points - 1, 1, 15), () => R.Points = Wrap(R.Points + 1, 1, 15));
            Stepper("ROUNDS TO WIN", R.Rounds.ToString(), () => R.Rounds = Wrap(R.Rounds - 1, 1, 9), () => R.Rounds = Wrap(R.Rounds + 1, 1, 9));
            Stepper("UPGRADES PER ROUND", R.Picks == 0 ? "NONE" : R.Picks.ToString(), () => R.Picks = Wrap(R.Picks - 1, 0, 3), () => R.Picks = Wrap(R.Picks + 1, 0, 3));
            Stepper("CHOICES OFFERED", R.Offer.ToString(), () => R.Offer = Wrap(R.Offer - 1, 2, 4), () => R.Offer = Wrap(R.Offer + 1, 2, 4));
            string[] rar = { "common", "rare", "epic" };
            int ri = Array.IndexOf(rar, R.MaxRarity);
            Stepper("RARITY UP TO", R.MaxRarity.ToUpperInvariant(), () => R.MaxRarity = rar[(ri + 2) % 3], () => R.MaxRarity = rar[(ri + 1) % 3]);
            Stepper("STARTING PERKS", R.StartPerks.ToString(), () => R.StartPerks = Wrap(R.StartPerks - 1, 0, 5), () => R.StartPerks = Wrap(R.StartPerks + 1, 0, 5));
            float leftEnd = y;
            cx = pw * 0.24f; y = colTop;
            Switch("PERKS", R.Perks, () => R.Perks = !R.Perks);
            Switch("STAT CARDS", R.Cards, () => R.Cards = !R.Cards);
            Switch("DRAFT START", R.DraftStart, () => R.DraftStart = !R.DraftStart);
            Text($"BANNED PERKS: {R.BannedPerks.Length}", new Vector2(cx - colW * 0.25f, y), 0.5f, colW * 0.48f, Color.white, TextAlignmentOptions.Right);
            Button(readOnly ? "SEE LIST" : "EDIT LIST", new Vector2(cx + colW * 0.25f, y), new Vector2(colW * 0.38f, 1.6f), () => BanPanel.Open(readOnly, Build), 0.48f);
            y = Mathf.Min(leftEnd, y - row) - 0.4f;

            // banned elements: one switch each
            Text("BANNED ELEMENTS", new Vector2(0f, y), 0.5f, pw - 2f, Color.white);
            y -= 1.7f;
            var els = Elements.All.Where(e => e.Id != "none").ToList();
            float ew = (pw - 3f) / els.Count;
            for (int i = 0; i < els.Count; i++)
            {
                var e = els[i];
                bool banned = R.BannedElements.Contains(e.Id);
                var at = new Vector2(-pw / 2f + 1.5f + ew * (i + 0.5f), y);
                string label = banned ? $"<color=#FF8A7C><s>{e.Id.ToUpperInvariant()}</s></color>" : $"<color=#{e.Color}>{e.Id.ToUpperInvariant()}</color>";
                if (readOnly) Text(label, at, 0.42f, ew, Color.white);
                else
                {
                    var b = Button("", at, new Vector2(ew - 0.25f, 1.6f), () => { R.BannedElements = banned ? R.BannedElements.Where(x => x != e.Id).ToArray() : R.BannedElements.Concat(new[] { e.Id }).ToArray(); Changed(); }, 0.42f);
                    b.color = UiArt.Paint(banned ? new Color(0.35f, 0.1f, 0.12f, 1f) : new Color(0.02f, 0.2f, 0.34f, 1f));
                    var w = b.GetComponent<CreatorWidget>(); if (w != null) { w.Normal = b.color; w.Hover = Color.Lerp(b.color, Color.white, 0.2f); }
                    Text(label, at, 0.42f, ew, Color.white, o: 9);
                }
            }

            float by = -ph / 2f + 1.7f;
            if (!readOnly)
            {
                Text("SAVE AS:", new Vector2(-pw * 0.38f, by + 2.4f), 0.45f, 6f, new Color(0.85f, 0.95f, 1f));
                for (int i = 0; i < LobbyRules.CustomSlots; i++)
                {
                    int slot = i;
                    Button($"CUSTOM {i + 1}", new Vector2(-pw * 0.22f + i * pw * 0.15f, by + 2.4f), new Vector2(pw * 0.13f, 1.6f), () => { LobbyRules.SaveCustom(slot); note = $"SAVED AS CUSTOM {slot + 1}"; Build(); }, 0.42f);
                }
                Button("DEFAULTS", new Vector2(pw * 0.27f, by), new Vector2(pw * 0.18f, 2.1f), () => Apply(RuleSet.From(RulePresets.All[0])), 0.55f);
            }
            Button("DONE", new Vector2(readOnly ? 0f : -pw * 0.05f, by), new Vector2(pw * 0.2f, 2.1f), () => Destroy(gameObject), 0.6f);
            Text(note, new Vector2(pw * 0.33f, by + 2.4f), 0.42f, pw * 0.3f, new Color(1f, 0.85f, 0.35f));
            PadHint("A: CHANGE · B: DONE", new Vector2(-pw * 0.33f, by), pw * 0.3f);
            focus = keep >= 0 && keep < stops.Count ? keep : 0;
        }

        private static int Wrap(int v, int lo, int hi) => v < lo ? hi : v > hi ? lo : v;

        private static List<(string name, RuleSet rules)> PresetList()
        {
            var l = RulePresets.All.Select(d => (d.Name, RuleSet.From(d))).ToList();
            for (int i = 0; i < LobbyRules.CustomSlots; i++) { var c = LobbyRules.Custom(i); if (c != null) l.Add(($"CUSTOM {i + 1}", c)); }
            return l;
        }

        private void Apply(RuleSet r) { LobbyRules.Current = r.Copy(); Changed(); }

        private void Changed()
        {
            note = "";
            LobbyRules.Current.Name = LobbyRules.MatchingPreset() ?? "CUSTOM";
            LobbyRules.Changed();
            VersusMenu.RulesChanged();
            Build();
        }

        private void Update()
        {
            if (BanPanel.IsOpen) return;
            if (readOnly && Online.Net.IsClient && shownRules != LobbyRules.Current) { shownRules = LobbyRules.Current; Build(); }   // the host changed them
            if (NavUpdate()) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Destroy(gameObject);
        }

        private RuleSet shownRules;
        protected override void OnNavBack() => Destroy(gameObject);
        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }
        public static void Refresh() { if (open != null) open.Build(); }
    }

    // the perks banned from offers (and starting perks): a page of perks at a time, filtered by element
    internal class BanPanel : OvertimePanel
    {
        private static BanPanel open;
        public static bool IsOpen => open != null;
        private bool readOnly;
        private Action onClose;
        private static int page, element;
        private const int Cols = 4, Rows = 9;

        public static void Open(bool readOnly, Action onClose)
        {
            if (open != null) return;
            var go = new GameObject("Overtime Bans");
            open = go.AddComponent<BanPanel>();
            if (!open.InitKit(310)) { Destroy(go); open = null; return; }
            open.readOnly = readOnly; open.onClose = onClose;
            open.Build();
        }

        private void Build()
        {
            int keep = focus;
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops();
            navKeys = true;
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            float pw = W * 0.86f, ph = H * 0.9f, top = ph / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            var r = LobbyRules.Current;
            Text($"BANNED PERKS  ({r.BannedPerks.Length})", new Vector2(0f, top - 1.7f), 1f, pw - 2f, Color.white);
            var els = new[] { "all" }.Concat(Elements.All.Select(e => e.Id)).ToArray();
            element = Mathf.Clamp(element, 0, els.Length - 1);
            Button($"ELEMENT: {els[element].ToUpperInvariant()}", new Vector2(-pw * 0.3f, top - 3.6f), new Vector2(pw * 0.25f, 1.7f), () => { element = (element + 1) % els.Length; page = 0; Build(); }, 0.45f);
            Text(readOnly ? "THE HOST'S LIST" : "CLICK A PERK TO BAN OR ALLOW IT", new Vector2(pw * 0.15f, top - 3.6f), 0.45f, pw * 0.5f, new Color(0.85f, 0.95f, 1f));
            var list = Perks.All.Where(p => element == 0 || p.Element == els[element]).OrderBy(p => p.Title).ToList();
            int per = Cols * Rows, pages = Mathf.Max(1, (list.Count + per - 1) / per);
            page = Mathf.Clamp(page, 0, pages - 1);
            float cw = (pw - 2f) / Cols, y0 = top - 5.6f;
            var shown = list.Skip(page * per).Take(per).ToList();
            for (int i = 0; i < shown.Count; i++)
            {
                var p = shown[i];
                bool banned = r.BannedPerks.Contains(p.Id);
                var at = new Vector2(-pw / 2f + 1f + cw * (i % Cols + 0.5f), y0 - (i / Cols) * 2f);
                string label = banned ? $"<color=#FF8A7C><s>{p.Title.ToUpperInvariant()}</s></color>" : p.Title.ToUpperInvariant();
                if (readOnly) { Text(label, at, 0.42f, cw - 0.4f, Color.white); continue; }
                var b = Button("", at, new Vector2(cw - 0.3f, 1.7f), () =>
                {
                    var rr = LobbyRules.Current;
                    rr.BannedPerks = banned ? rr.BannedPerks.Where(x => x != p.Id).ToArray() : rr.BannedPerks.Concat(new[] { p.Id }).ToArray();
                    rr.Name = LobbyRules.MatchingPreset() ?? "CUSTOM";
                    LobbyRules.Changed();
                    VersusMenu.RulesChanged();
                    Build();
                }, 0.42f);
                b.color = UiArt.Paint(banned ? new Color(0.35f, 0.1f, 0.12f, 1f) : new Color(0.02f, 0.2f, 0.34f, 1f));
                var w = b.GetComponent<CreatorWidget>(); if (w != null) { w.Normal = b.color; w.Hover = Color.Lerp(b.color, Color.white, 0.2f); }
                Text(label, at, 0.42f, cw - 0.4f, Color.white, o: 9);
            }
            float by = -ph / 2f + 1.6f;
            Button("<", new Vector2(-pw * 0.12f, by), new Vector2(2f, 1.8f), () => { page = (page - 1 + pages) % pages; Build(); }, 0.6f);
            Text($"PAGE {page + 1} / {pages}", new Vector2(0f, by), 0.5f, 8f, Color.white);
            Button(">", new Vector2(pw * 0.12f, by), new Vector2(2f, 1.8f), () => { page = (page + 1) % pages; Build(); }, 0.6f);
            if (!readOnly) Button("ALLOW ALL", new Vector2(-pw * 0.33f, by), new Vector2(pw * 0.16f, 1.9f), () => { LobbyRules.Current.BannedPerks = new string[0]; LobbyRules.Changed(); VersusMenu.RulesChanged(); Build(); }, 0.5f);
            Button("DONE", new Vector2(pw * 0.33f, by), new Vector2(pw * 0.16f, 1.9f), () => Destroy(gameObject), 0.6f);
            focus = keep >= 0 && keep < stops.Count ? keep : 0;
        }

        private void Update()
        {
            if (NavUpdate()) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Destroy(gameObject);
        }

        protected override void OnNavBack() => Destroy(gameObject);
        protected override void OnNavTab(int dir) { page += dir; Build(); }
        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; var cb = onClose; onClose = null; cb?.Invoke(); }
    }
}
