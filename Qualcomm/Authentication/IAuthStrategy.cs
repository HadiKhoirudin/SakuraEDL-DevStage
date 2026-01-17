// ============================================================================
// LoveAlways - 认证策略接口
// IAuthStrategy - 用于处理不同厂商的特殊认证逻辑
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using LoveAlways.Qualcomm.Protocol;

namespace LoveAlways.Qualcomm.Authentication
{
    /// <summary>
    /// 认证策略接口
    /// </summary>
    public interface IAuthStrategy
    {
        /// <summary>
        /// 策略名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 执行认证
        /// </summary>
        /// <param name="client">Firehose 客户端</param>
        /// <param name="programmerPath">Programmer 文件路径</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>是否认证成功</returns>
        Task<bool> AuthenticateAsync(FirehoseClient client, string programmerPath, CancellationToken ct = default(CancellationToken));
    }
}
