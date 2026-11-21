using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GamePanelHUDCompass.VisitApi
{
    /// <summary>
    /// VisitAPI 剧情区域（服务端 /visitapi/zones 的 data 数组元素）。
    /// 字段名与 VisitAPI.Native.QuestZones.Zone 的 JSON 键一一对应（小写），
    /// 解析口径照抄 VisitAPI 自己的 TryParse，避免两边理解不一致。
    /// </summary>
    internal class VisitZone
    {
        public string Id;

        public string Type;

        public List<string> Locations = new List<string>();

        public Vector3 Position;

        public Vector3 Size = Vector3.one;

        public Quaternion Rotation = Quaternion.identity;

        /// <summary>null 表示不限时段；否则 [from, to]，跨零点时 from &gt; to。</summary>
        public float[] Hours;

        /// <summary>交互提示（有则表示该区域需要「使用」按键触发，例如对讲机）。</summary>
        public string InteractPrompt;

        /// <summary>交互点的世界坐标；null 表示用区域中心。</summary>
        public Vector3? InteractAt;

        /// <summary>是否属于放置类区域（VisitAPI 用它决定生成 PlaceItemTrigger 还是 ExperienceTrigger）。</summary>
        public bool IsPlaceItem => string.Equals(Type, "placeitem", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 解析服务端返回体。VisitAPI 返回 { data: [ ... ] }，格式不符时返回 null。
        /// 单个区域解析失败只跳过该条，不影响其余。
        /// </summary>
        public static List<VisitZone> ParseAll(string body)
        {
            if (string.IsNullOrEmpty(body))
                return null;

            JToken data;
            try
            {
                data = JObject.Parse(body)["data"];
            }
            catch (Exception)
            {
                return null;
            }

            if (!(data is JArray array))
                return null;

            var result = new List<VisitZone>(array.Count);

            foreach (var token in array)
            {
                if (!(token is JObject item))
                    continue;

                try
                {
                    result.Add(ParseOne(item));
                }
                catch (Exception)
                {
                    // 单条坏数据不拖垮整批
                }
            }

            return result;
        }

        private static VisitZone ParseOne(JObject item)
        {
            var zone = new VisitZone
            {
                Id = Str(item["id"]),
                Type = Str(item["type"]) ?? "visit",
                Position = Vec(item["position"], Vector3.zero),
                Size = Vec(item["size"], Vector3.one)
            };

            zone.Type = zone.Type.ToLowerInvariant();

            if (item["locations"] is JArray locations)
            {
                foreach (var location in locations)
                {
                    var value = Str(location);

                    if (!string.IsNullOrEmpty(value))
                        zone.Locations.Add(value);
                }
            }

            if (item["rotation"] is JObject rotation)
            {
                zone.Rotation = new Quaternion(
                    F(rotation, "x"), F(rotation, "y"), F(rotation, "z"),
                    rotation["w"] != null ? F(rotation, "w") : 1f);
            }

            if (item["hours"] is JObject hours)
            {
                zone.Hours = new[] { F(hours, "from"), F(hours, "to") };
            }

            if (item["subtitles"] is JObject subtitles && subtitles["interact"] is JObject interact)
            {
                zone.InteractPrompt = Str(interact["prompt"]);

                if (interact["at"] is JObject at)
                    zone.InteractAt = Vec(at, zone.Position);
            }

            return zone;
        }

        /// <summary>
        /// 该区域是否覆盖当前地图。
        ///
        /// 内容包里同一张图会混着写 "Shoreline" / "shoreline"、"tarkovstreets" / "TarkovStreets"，
        /// 归零地更是分 "sandbox" 与 "sandbox_high" 两种（同一张图的不同层，区域坐标是同一套）。
        /// 只做 OrdinalIgnoreCase 会漏掉这些别名，所以额外做一层地图归一。
        /// </summary>
        public bool CoversLocation(string locationId)
        {
            if (string.IsNullOrEmpty(locationId))
                return false;

            var wanted = NormalizeLocation(locationId);

            for (var i = 0; i < Locations.Count; i++)
            {
                if (string.IsNullOrEmpty(Locations[i]))
                    continue;

                if (string.Equals(Locations[i], locationId, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (string.Equals(NormalizeLocation(Locations[i]), wanted, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 地图 id 归一：把同一张图的别名收敛到同一个键。
        /// 归零地的 sandbox 与 sandbox_high 共用一套区域坐标，视为等价。
        /// </summary>
        internal static string NormalizeLocation(string location)
        {
            if (string.IsNullOrEmpty(location))
                return string.Empty;

            var value = location.Trim().ToLowerInvariant();

            switch (value)
            {
                case "sandbox_high":
                case "sandboxhigh":
                    return "sandbox";
                default:
                    return value;
            }
        }

        /// <summary>
        /// 时段判定，口径照抄 VisitAPI 的 QuestZones.HoursOk：
        /// 无时段 / 两个端点都为 0 / 两端相等 → 恒通过；from &lt; to 取区间内，否则取跨零点区间。
        /// </summary>
        public bool HoursOk(DateTime? gameTime)
        {
            if (Hours == null || Hours.Length != 2)
                return true;

            var from = Hours[0];
            var to = Hours[1];

            if ((from <= 0f && to <= 0f) || Mathf.Approximately(from, to))
                return true;

            if (!gameTime.HasValue)
                return true;

            var hourOfDay = gameTime.Value.Hour + gameTime.Value.Minute / 60f;

            return from < to
                ? hourOfDay >= from && hourOfDay <= to
                : hourOfDay >= from || hourOfDay <= to;
        }

        private static string Str(JToken token)
        {
            return token == null ? null : token.Value<string>();
        }

        private static float F(JObject obj, string key)
        {
            var token = obj[key];

            if (token == null)
                return 0f;

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return token.Value<float>();

            return float.TryParse(token.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : 0f;
        }

        private static Vector3 Vec(JToken token, Vector3 fallback)
        {
            if (!(token is JObject obj))
                return fallback;

            return new Vector3(F(obj, "x"), F(obj, "y"), F(obj, "z"));
        }
    }
}
