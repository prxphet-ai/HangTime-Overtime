using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace HangtimeOvertime.Core
{
    // A tournament bracket for the screen between matches: rounds (columns) of matches; match i of a round feeds match
    // i / 2 of the next when the next round has half as many (a knockout tree), otherwise rounds are a ladder. Plain
    // strings and ints, so a tournament can send it to other PCs as it is.
    internal class BracketMatch
    {
        public string A = "", B = "";
        public int Winner = -1;               // 0 = A, 1 = B, -1 not played
        public string Note = "";              // a small tag under the match (BOSS, COMBO ...)
    }

    internal class BracketRound
    {
        public string Name = "";
        public readonly List<BracketMatch> Matches = new List<BracketMatch>();
    }

    internal class BracketModel
    {
        public string Title = "", Sub = "";
        public readonly List<BracketRound> Rounds = new List<BracketRound>();
        public string You = "YOU";            // the entry the marker follows
        public string Marker = "YOU";         // the marker's text
        public int FromRound = -1, ToRound = -1;   // the marker's move this time (round indexes), -1: no move
        public bool Champion;
    }

    internal static class BracketWire
    {
        public static void Write(Online.NetWriter w, BracketModel m)
        {
            w.S(m.Title).S(m.Sub).S(m.You).S(m.Marker).I(m.FromRound).I(m.ToRound).B(m.Champion).U8(m.Rounds.Count);
            foreach (var r in m.Rounds)
            {
                w.S(r.Name).U8(r.Matches.Count);
                foreach (var x in r.Matches) w.S(x.A).S(x.B).I(x.Winner).S(x.Note);
            }
        }

        public static BracketModel Read(Online.NetReader r)
        {
            var m = new BracketModel { Title = r.S(), Sub = r.S(), You = r.S(), Marker = r.S(), FromRound = r.I(), ToRound = r.I(), Champion = r.B() };
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                var round = new BracketRound { Name = r.S() };
                int k = r.U8();
                for (int j = 0; j < k; j++) round.Matches.Add(new BracketMatch { A = r.S(), B = r.S(), Winner = r.I(), Note = r.S() });
                m.Rounds.Add(round);
            }
            return m;
        }
    }

    internal static class Bracket
    {
        // Classic / Co-op / Loop: who was played in each slot of this run (1-8), and the game's match titles
        private static readonly string[] played = new string[9];
        private static readonly List<string> titles = new List<string>();
        public static int LastSlot;           // the slot of the latest match
        private static string cupDay, draftTeam;         // the daily cup being played (kept until its win screen, where the run has already ended)

        public static void RememberTitles(List<string> t) { if (t != null && t.Count > 0 && titles.Count == 0) titles.AddRange(t); }

        // kick-off: record the opponent; a match 1 starts a new bracket
        public static void MatchStarted(int slot, string opponent)
        {
            if (slot < 1 || slot > 8) return;
            for (int g = slot; g <= 8; g++) played[g] = null;
            if (slot == 1) for (int g = 1; g <= 8; g++) played[g] = null;
            played[slot] = opponent;
            LastSlot = slot;
            cupDay = Daily.Cup ? RunState.Daily : null;
            draftTeam = RunState.Draft != null ? Drafting.TeamOf(RunState.Draft)?.Name : null;
        }

        private static string Title(int g) => g < titles.Count && !string.IsNullOrEmpty(titles[g]) ? titles[g].ToUpperInvariant() : $"MATCH {g}";

        private static string SlotClass(int g) => g == 3 || g == 6 || g == 8 ? "BOSS" : g == 7 ? "COMBO" : "";

        // who the game will put in a slot: Classic follows the game's line-up (the combo team is drawn at kick-off);
        // Loop draws every slot at kick-off
        private static string Upcoming(int g)
        {
            if (cupDay != null) return Engine.TeamRoster.CupName(g, cupDay);
            var gm = GameManager.Instance;
            if (gm == null || RunState.Mode != RunMode.Normal || g == 7) return "???";
            GameObject prefab = null;
            if (g == 3) prefab = gm.midBoss1;
            else if (g == 6) prefab = gm.midBoss2;
            else if (g == 8) prefab = gm.finalBoss;
            else if (gm.tournamentLineUps != null)
            {
                var lineUps = Traverse.Create(gm.tournamentLineUps).Field("lineUps").GetValue<List<LineUp>>();
                int cur = TournamentLineUps.currentLineUp;
                if (lineUps != null && cur >= 0 && cur < lineUps.Count && lineUps[cur].lineUp != null && g < lineUps[cur].lineUp.Count) prefab = lineUps[cur].GetTeam(g);
            }
            var name = prefab != null ? prefab.GetComponent<OpponentTeam>()?.teamName : null;
            return string.IsNullOrEmpty(name) ? "???" : name;
        }

        // the run's bracket after winning slot `won` (8 = the final)
        public static BracketModel Classic(int won)
        {
            var m = new BracketModel();
            string mode = draftTeam != null ? "DRAFT CUP  ·  " + draftTeam.ToUpperInvariant() : cupDay != null ? "DAILY CUP " + cupDay : RunState.Mode == RunMode.Loop ? $"LOOP {RunState.Loop}" : GameManager.Instance != null && GameManager.Instance.numberOfPlayers == 2 ? "CO-OP" : "CLASSIC";
            m.Champion = won >= 8;
            if (draftTeam != null) m.You = draftTeam.ToUpperInvariant();
            m.Title = m.Champion ? "CHAMPIONS!" : draftTeam != null ? mode : $"{mode}  ·  BRACKET";
            for (int g = 1; g <= 8; g++)
            {
                var r = new BracketRound { Name = Title(g) };
                r.Matches.Add(new BracketMatch { A = draftTeam != null ? draftTeam.ToUpperInvariant() : m.You, B = played[g] ?? Upcoming(g), Winner = g <= won ? 0 : -1, Note = SlotClass(g) });
                m.Rounds.Add(r);
            }
            if (!m.Champion)
            {
                m.FromRound = won - 1; m.ToRound = won;
                var next = m.Rounds[won].Matches[0];
                m.Sub = $"NEXT: {m.Rounds[won].Name}  ·  VS {next.B.ToUpperInvariant()}";
            }
            else m.Sub = $"{mode}: ALL 8 MATCHES WON";
            return m;
        }
    }
}
