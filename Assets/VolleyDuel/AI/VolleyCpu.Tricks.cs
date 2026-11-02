using VolleyDuel.Sim;

namespace VolleyDuel.AI
{
    public sealed partial class VolleyCpu
    {
        // Signature Tricks, Finishers and Denials (task 4 fills these in).

        bool TryServeSpecial(ref GameState seen, int next) => false;

        bool TryAttackTrick(ref GameState seen, int next, int press) => false;

        bool TryDefenseTrick(ref GameState seen, int next, int press) => false;

        void ThinkFinisher(ref GameState seen, int next)
        {
        }
    }
}
