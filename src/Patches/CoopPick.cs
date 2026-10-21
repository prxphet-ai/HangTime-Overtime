using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HangtimeOvertime.Patches
{
    // Co-op individual picks: player 1 takes a card on the game's own upgrade screen as before; then player 2 gets an
    // offer of their own (same kind: perks or stat cards, never player 1's card) on this panel. A perk is its picker's
    // (Ownership: its own-action triggers fire for that player only); passive / team perks, the game's techniques and
    // stat cards are TEAM-WIDE. Player 2 chooses with key set 1 (LEFT / RIGHT, JUMP or MENU OK), their gamepad, or the mouse.
    internal class CoopPick : OvertimePanel
    {
        private static CoopPick open;
        public static bool IsOpen => open != null;
        private readonly List<Logo_technique> perks = new List<Logo_technique>();
        private readonly List<UpgradePair> cards = new List<UpgradePair>();
        private readonly List<SpriteRenderer> boxes = new List<SpriteRenderer>();
        private int sel;
        private float openedAt;
        private string first;

        public static void Show(UpgradeManager um, Logo_technique tookPerk, UpgradePair tookCard)
        {
            if (open != null || um == null) return;
            var go = new GameObject("Overtime Co-op Pick");
            open = go.AddComponent<CoopPick>();
            if (!open.InitKit(300)) { Destroy(go); open = null; return; }
            var t = Traverse.Create(um);
            var owned = PerkRegistry.PlayerTechniques ?? new List<Technique>();
            if (tookPerk != null)
            {
                var pool = (t.Field("techniques").GetValue<List<Logo_technique>>() ?? new List<Logo_technique>())
                    .Where(c => c != null && c != tookPerk && !owned.Contains(PerkRegistry.TechniqueOf(c))).ToList();
                while (open.perks.Count < 2 && pool.Count > 0)
                {
                    var c = pool[Random.Range(0, pool.Count)];             // the pool repeats cards by rarity weight
                    open.perks.Add(c);
                    pool.RemoveAll(x => PerkRegistry.TechniqueOf(x) == PerkRegistry.TechniqueOf(c));
                }
                open.first = tookPerk.title;
            }
            else
            {
                var pool = (t.Field("upgrades").GetValue<List<UpgradePair>>() ?? new List<UpgradePair>()).Where(p => p != null && p != tookCard).Distinct().ToList();
                while (open.cards.Count < 3 && pool.Count > 0) { var p = pool[Random.Range(0, pool.Count)]; open.cards.Add(p); pool.Remove(p); }
                open.first = tookCard?.title;
            }
            if (open.perks.Count == 0 && open.cards.Count == 0) { Destroy(go); open = null; return; }
            open.Build();
        }

        private void Build()
        {
            openedAt = Time.unscaledTime;
            Time.timeScale = 0f;                     // the upgrade screen would load the next match 4 s after player 1's pick
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.72f), 0);
            float pw = W * 0.8f, ph = H * 0.8f, top = ph / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            Text("PLAYER 2'S PICK", new Vector2(0f, top - 2f), 1.1f, pw - 2f, Color.white);
            Text($"PLAYER 1 TOOK <noparse>{(first ?? "").ToUpperInvariant()}</noparse>. NOW PLAYER 2 CHOOSES FROM THEIR OWN OFFER: A PERK YOU PICK IS YOURS (TEAM-WIDE ONES WORK FOR BOTH).",
                 new Vector2(0f, top - 3.8f), 0.45f, pw - 3f, new Color(0.85f, 0.95f, 1f), height: 2f, wrap: true);
            int n = perks.Count > 0 ? perks.Count : cards.Count;
            float cw = Mathf.Min(pw * 0.28f, 14f), ch = ph * 0.5f, gap = 1.2f;
            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) / 2f) * (cw + gap), y = -0.6f;
                int idx = i;
                var box = Button("", new Vector2(x, y), new Vector2(cw, ch), () => Take(idx), 0.5f);
                boxes.Add(box);
                string title, kind, text;
                if (perks.Count > 0)
                {
                    var c = perks[i];
                    var tech = PerkRegistry.TechniqueOf(c);
                    title = c.title;
                    kind = (tech is DataPerk d ? $"{d.Def.Rarity.ToUpperInvariant()} {(d.Def.Element != "none" ? d.Def.Element.ToUpperInvariant() + " " : "")}PERK" : "THE GAME'S TECHNIQUE")
                           + (Ownership.Personal(tech) ? "  ·  PLAYER 2'S" : "  ·  TEAM-WIDE");
                    text = c.description;
                }
                else
                {
                    var p = cards[i];
                    var oc = StatCards.CardOf(p);
                    title = p.title;
                    kind = "STAT CARD  ·  TEAM-WIDE";
                    text = oc != null ? StatCards.LongText(oc) : (p.description ?? "").Replace("_", " ");
                }
                Text($"<noparse>{title.ToUpperInvariant()}</noparse>", new Vector2(x, y + ch / 2f - 2f), 0.75f, cw - 1f, Color.white, height: 2.6f, wrap: true);
                Text(kind, new Vector2(x, y + ch / 2f - 4f), 0.42f, cw - 1f, new Color(1f, 0.85f, 0.35f));
                Text($"<noparse>{text}</noparse>", new Vector2(x, y - 1.5f), 0.5f, cw - 1.6f, Color.white, TextAlignmentOptions.Top, height: ch - 7f, wrap: true);
            }
            Text("PLAYER 2: " + Controls.KeyName(Controls.KeyOf(0, KeyAct.Left)) + " / " + Controls.KeyName(Controls.KeyOf(0, KeyAct.Right)) + " TO CHOOSE, " +
                 Controls.KeyName(Controls.KeyOf(0, KeyAct.Jump)) + " TO TAKE IT  ·  OR THEIR GAMEPAD / THE MOUSE", new Vector2(0f, -ph / 2f + 1.6f), 0.45f, pw - 2f, new Color(0.85f, 0.95f, 1f));
            Refresh();
            Plugin.Log.LogInfo("Co-op: player 2 offered " + string.Join(", ", perks.Select(p => p.title).Concat(cards.Select(c => c.title))));
        }

        private void Refresh()
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                var c = i == sel ? new Color(0.1f, 0.42f, 0.68f, 1f) : new Color(0.02f, 0.2f, 0.34f, 1f);
                boxes[i].color = c;
                var w = boxes[i].GetComponent<CreatorWidget>();
                if (w != null) { w.Normal = c; w.Hover = Color.Lerp(c, Color.white, 0.2f); }
            }
        }

        private void Take(int i)
        {
            if (Time.unscaledTime - openedAt < 0.4f) return;
            var gm = GameManager.Instance;
            if (perks.Count > 0)
            {
                var tech = PerkRegistry.TechniqueOf(perks[i]);
                if (tech != null && !gm.playerStats.techniques.Contains(tech))
                {
                    gm.playerStats.techniques.Add(perks[i].GetTechnique());
                    if (Ownership.Personal(tech)) Ownership.Set(0, tech, 2);
                }
                Collection.Pick(Collection.KeyOf(tech));
                Plugin.Log.LogInfo($"Co-op: player 2 took {perks[i].title} ({(Ownership.Personal(tech) ? "theirs" : "team-wide")})");
            }
            else
            {
                var p = cards[i];
                var oc = StatCards.CardOf(p);
                if (oc != null) StatCards.Take(oc);
                else foreach (var u in p.upgrades) gm.playerStats.GetStat(u)?.Upgrade();
                Collection.Pick(Collection.KeyOf(p));
                Plugin.Log.LogInfo($"Co-op: player 2 took {p.title}");
            }
            Destroy(gameObject);
        }

        private void Update()
        {
            Time.timeScale = 0f;
            int n = boxes.Count;
            if (n == 0) return;
            var kb = Keyboard.current;
            int dx = 0; bool ok = false;
            if (kb != null)
            {
                var l = Controls.Control(kb, 0, KeyAct.Left); var r = Controls.Control(kb, 0, KeyAct.Right);
                var j = Controls.Control(kb, 0, KeyAct.Jump); var m = Controls.Control(kb, 0, KeyAct.Confirm);
                if (l != null && l.wasPressedThisFrame) dx = -1;
                if (r != null && r.wasPressedThisFrame) dx = 1;
                ok = (j != null && j.wasPressedThisFrame) || (m != null && m.wasPressedThisFrame);
            }
            foreach (var g in Gamepad.all)
            {
                if (g.dpad.left.wasPressedThisFrame) dx = -1;
                if (g.dpad.right.wasPressedThisFrame) dx = 1;
                var b = Controls.Button(g, Controls.SlotOf(g), PadAct.Confirm);
                ok |= b != null && b.wasPressedThisFrame;
            }
            if (dx != 0) { sel = (sel + dx + n) % n; Refresh(); }
            if (ok) Take(sel);
        }

        protected override void OnDestroy() { base.OnDestroy(); Time.timeScale = 1f; if (open == this) open = null; }

        // ------------------------------------------------------------ when

        [HarmonyPatch(typeof(TechniqueButton), "Click")]
        private static class AfterPerk
        {
            [HarmonyPostfix]
            private static void Postfix(TechniqueButton __instance)
            {
                if (!Ownership.Coop) return;
                var tech = PerkRegistry.TechniqueOf(__instance.technique);
                if (Ownership.Personal(tech)) Ownership.Set(0, tech, 1);
                var um = Object.FindFirstObjectByType<UpgradeManager>();
                if (um != null && __instance.technique != Traverse.Create(um).Field("limitBreakTechnique").GetValue<Logo_technique>())
                    Show(um, __instance.technique, null);
            }
        }

        [HarmonyPatch(typeof(UpgradeButton), "Click")]
        private static class AfterCard
        {
            [HarmonyPostfix]
            private static void Postfix(UpgradeButton __instance)
            {
                if (!Ownership.Coop) return;
                Show(Object.FindFirstObjectByType<UpgradeManager>(), null, __instance.myPair);
            }
        }

        // the co-op card's flavour says whose it will be
        [HarmonyPatch(typeof(TechniqueButton), "Start")]
        private static class Label
        {
            [HarmonyPostfix]
            private static void Postfix(TechniqueButton __instance)
            {
                if (!Ownership.Coop || __instance.flavourText == null) return;
                var tech = PerkRegistry.TechniqueOf(__instance.technique);
                __instance.flavourText.text += Ownership.Personal(tech) ? "\n<color=#FFD95A>PLAYER 1 PICKS: THEIRS</color>" : "\n<color=#FFD95A>TEAM-WIDE</color>";
            }
        }
    }
}
