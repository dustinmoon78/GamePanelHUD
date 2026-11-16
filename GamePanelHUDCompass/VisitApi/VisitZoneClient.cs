using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SPT.Common.Http;

namespace GamePanelHUDCompass.VisitApi
{
    /// <summary>
    /// VisitAPI 区域表的拉取与缓存。
    ///
    /// 走服务端聚合接口而不是读内容包里的 JSON：VisitAPI-Server 的 ZoneSpawnRouter
    /// 会把 EFT11 / Sora_Chapter / TarkovaCola 等多个内容包里的 zones 合并且按 id 去重
    /// （dedupeById: true），本地读包会漏掉合并与去重逻辑。
    ///
    /// 请求在后台线程发起，结果经 <see cref="TryTake"/> 在主线程取走——
    /// RequestHandler.PostJson 是同步阻塞的，不能在 Unity 主线程调用。
    /// </summary>
    internal static class VisitZoneClient
    {
        private const string Route = "/visitapi/zones";

        private static volatile bool _requested;
        private static volatile bool _running;
        private static volatile bool _reportedFailure;

        private static List<VisitZone> _pending;

        /// <summary>服务端聚合结果；null 表示还没拉到（或拉取失败）。</summary>
        public static List<VisitZone> Zones { get; private set; }

        /// <summary>是否已经尝试过拉取（失败也算，避免每帧重试）。</summary>
        public static bool Attempted => _requested;

        /// <summary>
        /// 发起拉取（幂等，重复调用不会再发请求）。
        /// VisitAPI 未安装时服务端返回 404，这里只记一条日志并永久放弃。
        /// </summary>
        public static void Request()
        {
            if (_requested)
                return;

            _requested = true;
            _running = true;

            Task.Run(() =>
            {
                try
                {
                    var body = RequestHandler.PostJson(Route, "{}");
                    var zones = VisitZone.ParseAll(body);

                    if (zones == null)
                    {
                        ReportFailure("响应格式不符（缺少 data 数组）");

                        return;
                    }

                    _pending = zones;
                }
                catch (Exception ex)
                {
                    ReportFailure(ex.GetBaseException().Message);
                }
                finally
                {
                    _running = false;
                }
            });
        }

        /// <summary>主线程调用：后台拉取完成则接走结果并返回 true。</summary>
        public static bool TryTake(out List<VisitZone> zones)
        {
            zones = _pending;

            if (zones == null)
                return false;

            _pending = null;
            Zones = zones;

            return true;
        }

        /// <summary>是否仍在拉取中。</summary>
        public static bool Loading => _running;

        /// <summary>战局切换时复用缓存，不重复请求。</summary>
        public static void Reset()
        {
            _pending = null;
        }

        private static void ReportFailure(string reason)
        {
            if (_reportedFailure)
                return;

            _reportedFailure = true;

            Log?.LogWarning(
                "[Compass] VisitAPI 区域表拉取失败（未装 VisitAPI 或接口未就绪），剧情区域标记不启用: " + reason);
        }

        /// <summary>日志源由插件在 Awake 时注入，避免依赖 BepInEx 的全局静态日志</summary>
        internal static BepInEx.Logging.ManualLogSource Log;
    }
}
