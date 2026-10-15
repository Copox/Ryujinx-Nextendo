using System;
using System.Threading.Tasks;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo] What the interface does before a game started from the HOME Menu runs, as it
    /// does before a game started from the game list: today, bring the game's online data (the
    /// BCAT schedules of Splatoon 2 and 3) up to date. Without it a game started from the HOME
    /// Menu found its BCAT empty, and Splatoon 3 ended on "a communication error occurred" at the
    /// first schedule change.
    /// </summary>
    public static class NextendoHomeLaunch
    {
        /// <summary>Set by the interface; receives the application id.</summary>
        public static Func<ulong, Task> BeforeStart { get; set; }

        /// <summary>Runs <see cref="BeforeStart"/>, bounded so that a slow network never keeps the
        /// game from starting.</summary>
        public static void Run(ulong applicationId)
        {
            try
            {
                BeforeStart?.Invoke(applicationId)?.Wait(TimeSpan.FromSeconds(30));
            }
            catch (Exception)
            {
                // the game starts with what it has
            }
        }
    }
}
