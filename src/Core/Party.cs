using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Generated;
using UnityEngine;

namespace HangtimeOvertime.Core
{
    // One entrant of a party tournament: a player (a key set, a gamepad or a friend's device) or the AI.
    internal class Entrant
    {
        public string Name;
        public SlotInput Input;                   // null for the AI
        public string Team;                       // teams.json id
        public bool AI => Input == null;
    }

    // The party tournament: 2-8 entrants, the AI fills the bracket to 4 or 8, single elimination; every match is a 1v1
    // Versus match under the lobby rules. Two AI entrants settle their match at once (a coin weighted by their teams'
    // strength offsets); a player against the AI plays the game's own AI team. Online the host runs it: everyone's
    // entrants come from their own devices, the PCs not playing a match watch it (the normal mirror), and the bracket
    // goes to every PC between matches. A player whose PC drops out forfeits the match in progress and the AI plays
    // their later matches under their name.
    internal static class Party
    {
        public static bool Active;
        public static readonly List<Entrant> Entrants = new List<Entrant>();
        private static readonly List<List<int>> rounds = new List<List<int>>();     // entrant indexes per round, in bracket order
        private static readonly List<List<int>> winners = new List<List<int>>();    // per round, per match: 0 / 1 (side) or -1
        private static int curRound, curMatch = -1;
        private static int lastRound = -1, lastMatch = -1;
        public static int Champion = -1;
        public static bool Swapped;               // the match being played has the bracket's right entrant on the left

        public static (Entrant left, Entrant right) Current =>
            curMatch < 0 ? (null, null) : (Entrants[rounds[curRound][2 * curMatch]], Entrants[rounds[curRound][2 * curMatch + 1]]);

        public static void Begin(List<Entrant> players, int size)
        {
            Entrants.Clear(); rounds.Clear(); winners.Clear();
            Entrants.AddRange(players);
            var used = new HashSet<string>(players.Select(p => p.Team));
            var teams = Teams.All.Where(t => !used.Contains(t.Id)).OrderBy(_ => Random.value).ToList();
            while (Entrants.Count < size)
            {
                var t = teams.Count > 0 ? teams[0] : Teams.All[Random.Range(0, Teams.All.Length)];
                if (teams.Count > 0) teams.RemoveAt(0);
                Entrants.Add(new Entrant { Name = "AI · " + t.Name.ToUpperInvariant(), Team = t.Id });
            }
            // the seeding: players spread across the bracket so they don't all meet in round 1
            var humans = Enumerable.Range(0, Entrants.Count).Where(i => !Entrants[i].AI).OrderBy(_ => Random.value).ToList();
            var bots = Enumerable.Range(0, Entrants.Count).Where(i => Entrants[i].AI).OrderBy(_ => Random.value).ToList();
            var order = new int[size];
            int[] slots = size == 8 ? new[] { 0, 2, 4, 6, 1, 3, 5, 7 } : size == 4 ? new[] { 0, 2, 1, 3 } : new[] { 0, 1 };
            var seq = humans.Concat(bots).ToList();
            for (int i = 0; i < size; i++) order[slots[i]] = seq[i];
            rounds.Add(order.ToList());
            for (int n = size; n > 1; n /= 2) winners.Add(Enumerable.Repeat(-1, n / 2).ToList());
            for (int r = 1; r < winners.Count; r++) rounds.Add(Enumerable.Repeat(-1, size >> r).ToList());
            curRound = 0; curMatch = -1; lastRound = lastMatch = -1; Champion = -1;
            Active = true;
            Plugin.Log.LogInfo($"Party: {Entrants.Count} entrants ({humans.Count} players): " + string.Join(", ", order.Select(i => Entrants[i].Name)));
        }

        public static void End() { Active = false; Entrants.Clear(); rounds.Clear(); winners.Clear(); curMatch = -1; }

        // the next match players have to play (AI-only matches on the way are settled); false when the cup is decided
        public static bool Advance()
        {
            for (int r = 0; r < winners.Count; r++)
                for (int m = 0; m < winners[r].Count; m++)
                {
                    if (winners[r][m] >= 0) continue;
                    int a = rounds[r][2 * m], b = rounds[r][2 * m + 1];
                    if (a < 0 || b < 0) return false;                         // not reachable: the round before isn't done
                    if (Entrants[a].AI && Entrants[b].AI) { Settle(r, m, AiWinner(a, b)); continue; }
                    curRound = r; curMatch = m;
                    return true;
                }
            curMatch = -1;
            return false;
        }

        private static int AiWinner(int a, int b)
        {
            float Offset(int i) => Teams.All.FirstOrDefault(t => t.Id == Entrants[i].Team)?.PointsOffset ?? 0f;
            float pa = Mathf.Clamp(0.5f + (Offset(a) - Offset(b)) * 0.03f, 0.2f, 0.8f);
            return Random.value < pa ? 0 : 1;
        }

        private static void Settle(int r, int m, int side)
        {
            winners[r][m] = side;
            int w = rounds[r][2 * m + side];
            if (r + 1 < rounds.Count) rounds[r + 1][m] = w;
            else Champion = w;
            lastRound = r; lastMatch = m;
            Plugin.Log.LogInfo($"Party: {RoundName(r)} match {m + 1}: {Entrants[w].Name} beat {Entrants[rounds[r][2 * m + 1 - side]].Name}");
        }

        // the match being played ended (side 0 = its left entrant)
        public static void MatchOver(int side) { if (curMatch >= 0) Settle(curRound, curMatch, side); curMatch = -1; }

        // a friend's PC dropped out: its entrants become the AI; the match in progress is theirs to forfeit (returns the
        // side that wins it, or -1)
        public static int PeerLeft(int pc)
        {
            int forfeit = -1;
            var (l, r) = Current;
            foreach (var e in Entrants.Where(e => e.Input != null && e.Input.Kind == InputKind.Remote && Online.Net.PcOf(e.Input.DeviceId) == pc))
            {
                if (e == l) forfeit = 1;
                if (e == r) forfeit = 0;
                e.Input = null;
                e.Name += " (AI)";
            }
            return forfeit;
        }

        private static string RoundName(int r)
        {
            int left = winners.Count - r;
            return left == 1 ? "FINAL" : left == 2 ? "SEMI-FINALS" : left == 3 ? "QUARTER-FINALS" : $"ROUND {r + 1}";
        }

        // the bracket for the bracket screen: the next match highlighted, or the champion
        public static BracketModel Model()
        {
            var m = new BracketModel { Title = Champion >= 0 ? "CHAMPION!" : "PARTY CUP", You = "", Marker = "NEXT" };
            for (int r = 0; r < winners.Count; r++)
            {
                var round = new BracketRound { Name = RoundName(r) };
                for (int i = 0; i < winners[r].Count; i++)
                {
                    int a = rounds[r][2 * i], b = rounds[r][2 * i + 1];
                    round.Matches.Add(new BracketMatch { A = a >= 0 ? Entrants[a].Name : "?", B = b >= 0 ? Entrants[b].Name : "?", Winner = winners[r][i] });
                }
                m.Rounds.Add(round);
            }
            if (Champion >= 0) { m.Champion = true; m.Sub = $"{Entrants[Champion].Name} WINS THE PARTY CUP"; return m; }
            var (l, rr) = Current;
            if (l != null)
            {
                m.You = l.Name;
                m.ToRound = curRound; m.FromRound = -1;
                m.Sub = $"NEXT: {RoundName(curRound)}  ·  {l.Name}  VS  {rr.Name}";
            }
            if (lastMatch >= 0) m.Sub = $"{Entrants[rounds[lastRound][2 * lastMatch + winners[lastRound][lastMatch]]].Name} WON  ·  " + m.Sub;
            return m;
        }
    }
}
