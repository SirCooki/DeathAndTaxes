using System.Collections.Generic;
using Jotunn.Entities;

namespace DeathAndTaxes
{
    /// Test kit: "dt_trophies" spawns every trophy the tables use, each at its table maximum (common 10, uncommon 5,
    /// rare 3, unique 1; the highest if a trophy is on several tables), plus 2 of every boss trophy (discover + remind).
    /// A cheat command: only works with devcommands on. Uses the game's own "spawn" command for each stack.
    public class TrophyKitCommand : ConsoleCommand
    {
        public override string Name => "dt_trophies";
        public override string Help => "Death & Taxes test kit: spawns every table trophy (table maximum) and 2 of each boss trophy";
        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            var amounts = new Dictionary<string, int>();
            var order = new List<string>();
            foreach (var def in TributeDefinitions.All)
                foreach (var tier in def.Tiers)
                    foreach (var trophy in tier.Trophies)
                    {
                        if (!amounts.ContainsKey(trophy)) { order.Add(trophy); amounts[trophy] = 0; }
                        if (tier.MaxPerTrophy > amounts[trophy]) amounts[trophy] = tier.MaxPerTrophy;
                    }
            foreach (var def in TributeDefinitions.All)
                if (!amounts.ContainsKey(def.BossTrophy)) { order.Add(def.BossTrophy); amounts[def.BossTrophy] = 2; }

            // A key bind runs this from the chat window, where "spawn" isn't allowed: always spawn through the console
            Terminal console = Console.instance ? (Terminal)Console.instance : context;
            foreach (var trophy in order)
                console.TryRunCommand($"spawn {trophy} {amounts[trophy]}", silentFail: false, skipAllowedCheck: false);
            console.AddString($"Death & Taxes: spawned {order.Count} trophy stacks");
        }
    }
}
