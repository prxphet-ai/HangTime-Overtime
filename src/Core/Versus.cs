using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Engine;
using HangtimeOvertime.Generated;
using UnityEngine;
using StatCards = HangtimeOvertime.Engine.StatCards;

namespace HangtimeOvertime.Core
{
    // ------------------------------------------------------------ match setup (who plays where, on what input)
    // A match is a list of slots. 1v1 uses two human hitters, each with the game's AI setter. 1v2, 2v2 and online
    // play add or change slots (another Human on a side, or Remote) without changing the rest.

    public enum SlotControl { Human, AI, Remote }

    public enum InputKind { None, KeysWASD, KeysArrows, KeysIJKL, KeysNumpad, Gamepad, Remote }

    public class SlotInput
    {
        public InputKind Kind;
        public int DeviceId;          // gamepads: the Input System device id
        public string Label;          // "WASD", "ARROWS", "GAMEPAD 1"
        public bool Same(SlotInput o) => o != null && o.Kind == Kind && (Kind != InputKind.Gamepad || o.DeviceId == DeviceId);
    }

    public class VersusSlot
    {
        public int Side;              // 0 left, 1 right
        public int Number;            // player number shown on screen (join order), 0 for AI
        public string Role;           // "hitter" (the game's main player / the right spiker), "partner" (the game's co-op
                                      // player 2 on the left) or "setter" (the right team's setter; a full player when human)
        public SlotControl Control;
        public SlotInput Input;    // Human: a local device; Remote: a network peer (later)
    }

    public class VersusSetup
    {
        public string Format = "1v1";
        public readonly List<VersusSlot> Slots = new List<VersusSlot>();
        public readonly string[] Team = new string[2];   // team key per side: "" = your own team (side 0), a new-team id or the game's prefab name

        public static readonly string[] Formats = { "1v1", "1v2", "2v2" };

        // Which slot each joining player takes, in join order: (side, role).
        public static (int side, string role)[] Seats(string format) =>
            format == "1v2" ? new[] { (0, "hitter"), (1, "hitter"), (1, "setter") } :
            format == "2v2" ? new[] { (0, "hitter"), (0, "partner"), (1, "hitter"), (1, "setter") } :
                              new[] { (0, "hitter"), (1, "hitter") };

        // 1v1: a human hitter and an AI setter per side. 1v2: player 1 and the AI setter vs two humans (the right setter
        // becomes a full player). 2v2: two humans per side (the left pair is the game's own co-op pair).
        public static VersusSetup Create(string format, IList<SlotInput> inputs, string team1, string team2)
        {
            var s = new VersusSetup { Format = format };
            var seats = Seats(format);
            for (int i = 0; i < seats.Length; i++)
                s.Slots.Add(new VersusSlot { Side = seats[i].side, Role = seats[i].role, Number = i + 1, Control = SlotControl.Human, Input = inputs[i] });
            foreach (int side in new[] { 0, 1 })
                if (!s.Slots.Any(x => x.Side == side && x.Role != "hitter"))
                    s.Slots.Add(new VersusSlot { Side = side, Role = "setter", Control = SlotControl.AI });
            s.Team[0] = team1; s.Team[1] = team2;
            return s;
        }

        public static VersusSetup OneVsOne(SlotInput p1, SlotInput p2, string team1, string team2) => Create("1v1", new[] { p1, p2 }, team1, team2);

        public List<VersusSlot> Humans(int side) => Slots.Where(x => x.Side == side && x.Control == SlotControl.Human).OrderBy(x => x.Number).ToList();
        public IEnumerable<VersusSlot> AllHumans => Slots.Where(x => x.Control == SlotControl.Human).OrderBy(x => x.Number);
        public VersusSlot Human(int side) => Humans(side).FirstOrDefault();   // the side's first player (picks its team)
        public bool HumanSetter(int side) => Slots.Any(x => x.Side == side && x.Role == "setter" && x.Control == SlotControl.Human);

        // "P1", "P1 & P2", "P1 + AI"
        public string SideLabel(int side)
        {
            var names = Humans(side).Select(h => "P" + h.Number).ToList();
            return string.Join(" & ", names) + (names.Count == 1 && Format != "1v1" ? " + AI" : "");
        }
    }

    // ------------------------------------------------------------ an upgrade on the loser's pick screen

    public class VersusOffer
    {
        public Technique Perk;        // an Overtime perk or one of the game's own
        public StatCardDef Card;      // or a stat card
        public string Title => Perk is DataPerk d ? d.Def.Title : Perk != null ? PerkRegistry.VanillaTitle(Perk) : Card.Title;
        public string Rarity => Perk is DataPerk d ? d.Def.Rarity : Perk != null ? Scaling.VanillaRarity(Perk.GetType().Name) : Card.Rarity;
        public string Kind => Card != null ? "STAT CARD" : Perk is DataPerk d && d.Def.Element != "none" ? d.Def.Element.ToUpperInvariant() + " PERK" : "PERK";
        public string Text
        {
            get
            {
                if (Card != null) return StatCards.LongText(Card).Split('\n').Where(l => l.Length > 0 && !l.Equals(Card.Rarity.ToUpperInvariant())).Aggregate((a, b) => a + "\n" + b);
                if (Perk is DataPerk d) return d.Def.Description;
                var card = PerkRegistry.VanillaCards.FirstOrDefault(v => PerkRegistry.TechniqueOf(v) == Perk);
                if (card != null) return card.description;
                return PerkRegistry.KnownDescriptions.TryGetValue(Perk.GetType().Name, out var known) ? known : "One of the game's own abilities.";
            }
        }
    }

    // ------------------------------------------------------------ the running match

    internal static class VersusState
    {
        public static VersusSetup Setup;
        public static readonly int[] RoundWins = new int[2];
        public static readonly int[] Picks = new int[2];             // picks each side has made (two humans on a side take turns)
        public static int Target = VersusRules.RoundsToWin;          // round wins needed (CONTINUE raises it)
        public static int Round = 1;                                 // the round being played
        public static int LastWinner = -1;
        public static readonly List<Technique> RightPerks = new List<Technique>();   // the left side's are the game's playerStats.techniques
        public static bool Active => RunState.Mode == RunMode.Versus && Setup != null;

        public static List<Technique> PerksOf(int side) => side == 0 ? GameManager.Instance.playerStats.techniques : RightPerks;

        // a fresh match: no perks, no cards, score 0-0
        public static void NewMatch(VersusSetup setup)
        {
            Setup = setup;
            RunState.Mode = RunMode.Versus;
            RoundWins[0] = RoundWins[1] = 0;
            Picks[0] = Picks[1] = 0;
            Target = VersusRules.RoundsToWin;
            Round = 1;
            LastWinner = -1;
            ResetBuilds();
            TutorialManager.onTutorial = false;
            Plugin.Log.LogInfo($"Versus {setup.Format}: {Describe(setup)}");
        }

        public static void Rematch() => NewMatch(Setup);

        public static void Continue()
        {
            Target += VersusRules.ContinueRounds;
            Round++;
            Plugin.Log.LogInfo($"Versus continues: first to {Target} rounds");
        }

        // leaving Versus (menu, quit, starting another mode): nothing of it may stay behind
        public static void Leave()
        {
            if (RunState.Mode == RunMode.Versus) RunState.Mode = RunMode.Normal;
            Setup = null;
            ResetBuilds();
            TeamRoster.ResumeKey = null;
        }

        private static void ResetBuilds()
        {
            var gm = GameManager.Instance;
            if (gm != null)
            {
                foreach (var u in gm.playerStats.statUpgrades) u.currentLevel = 0;
                gm.playerStats.techniques.Clear();
            }
            RightPerks.Clear();
            StatCards.ResetRun();
            LimitBreakEffects.ResetLimits();
            GameManager.statOverload = false;
        }

        public static string Describe(VersusSetup s) =>
            string.Join(", ", s.Slots.Select(x => $"side {x.Side} {x.Role} {x.Control}{(x.Input != null ? " (" + x.Input.Label + ")" : "")}")) +
            $"; teams {TeamName(0, s.Team[0])} vs {TeamName(1, s.Team[1])}";

        public static string TeamName(int side, string key)
        {
            if (string.IsNullOrEmpty(key)) return side == 0 ? "Hoshiyumi" : "?";
            var def = Teams.All.FirstOrDefault(t => t.Id == key);
            return def != null ? def.Name : key;
        }

        // ------------------------------------------------------------ the loser's offer

        public static List<VersusOffer> Offer(int side)
        {
            var owned = new HashSet<Technique>(PerksOf(side));
            var perks = PerkRegistry.All.Cast<Technique>()
                .Concat(Scaling.OpponentTechniques.Select(PerkRegistry.FindVanilla))
                .Where(t => t != null && !owned.Contains(t)).Distinct().ToList();
            var cards = StatCards.All.ToList();
            int Weight(string rarity) => rarity == "epic" ? 1 : rarity == "rare" ? 2 : 3;
            var offer = new List<VersusOffer>();
            for (int guard = 0; offer.Count < VersusRules.OfferSize && guard < 200; guard++)
            {
                bool card = Random.value < VersusRules.StatCardShare || perks.Count == 0;
                if (card)
                {
                    var c = Pick(cards, x => Weight(x.Rarity));
                    if (c != null && offer.All(o => o.Card != c)) offer.Add(new VersusOffer { Card = c });
                }
                else
                {
                    var t = Pick(perks, x => Weight(x is DataPerk d ? d.Def.Rarity : Scaling.VanillaRarity(x.GetType().Name)));
                    if (t != null && offer.All(o => o.Perk != t)) offer.Add(new VersusOffer { Perk = t });
                }
            }
            return offer;
        }

        private static T Pick<T>(List<T> list, System.Func<T, int> weight) where T : class
        {
            int total = list.Sum(weight);
            if (total <= 0) return null;
            int x = Random.Range(0, total);
            foreach (var item in list) { x -= weight(item); if (x < 0) return item; }
            return list.LastOrDefault();
        }

        // who chooses for a losing side: its players take turns
        public static VersusSlot Picker(int side)
        {
            var humans = Setup.Humans(side);
            return humans.Count == 0 ? null : humans[Picks[side] % humans.Count];
        }

        public static void Take(int side, VersusOffer o)
        {
            Picks[side]++;
            if (o.Card != null) StatCards.Take(o.Card, side);
            else if (o.Perk != null && !PerksOf(side).Contains(o.Perk)) PerksOf(side).Add(o.Perk);
            Plugin.Log.LogInfo($"Versus: {Setup.SideLabel(side)} took {o.Title} ({o.Kind})");
        }

        // a player's build for the screens between rounds: perks and stat totals
        public static List<string> Build(int side)
        {
            var lines = PerksOf(side).Select(t => t is DataPerk d ? d.Def.Title : PerkRegistry.VanillaTitle(t)).ToList();
            foreach (var kv in StatCards.PointsFor(side).Where(kv => Mathf.Abs(kv.Value) > 0.01f))
                lines.Add($"{StatCards.Label(kv.Key)} {(kv.Value > 0 ? "+" : "")}{kv.Value:0.##}");
            return lines;
        }
    }
}
