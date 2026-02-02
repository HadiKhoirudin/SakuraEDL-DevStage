// ============================================================================
// LoveAlways - Authentication Strategy Interface
// IAuthStrategy - Handles special authentication logic for different manufacturers
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation & some fixes by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using LoveAlways.Qualcomm.Protocol;
using System.Threading;
using System.Threading.Tasks;

namespace LoveAlways.Qualcomm.Authentication
{
    /// <summary>
    /// Authentication Strategy Interface
    /// </summary>
    public interface IAuthStrategy
    {
        /// <summary>
        /// Strategy Name
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Perform Authentication
        /// </summary>
        /// <param name="client">Firehose Client</param>
        /// <param name="programmerPath">Programmer File Path</param>
        /// <param name="ct">Cancellation Token</param>
        /// <returns>True if authentication succeeded</returns>
        Task<bool> AuthenticateAsync(FirehoseClient client, string programmerPath, CancellationToken ct = default(CancellationToken));
    }
}
