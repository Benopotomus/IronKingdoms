using System.Collections.Generic;

namespace IronKingdoms.Combat
{
    public sealed class CombatSimulationResult
    {
        public CombatSimulationResult()
        {
            LogLines = new List<string>();
        }

        public string WinnerName { get; set; } = string.Empty;
        public int RoundsCompleted { get; set; }
        public List<string> LogLines { get; }

        public void AddLine(string line)
        {
            LogLines.Add(line);
        }

        public override string ToString()
        {
            return string.Join("\n", LogLines);
        }
    }
}
